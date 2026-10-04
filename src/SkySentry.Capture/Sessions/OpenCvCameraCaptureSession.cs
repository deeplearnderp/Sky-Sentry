using System.Runtime.InteropServices;
using DirectShowLib;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkySentry.Core.Cameras;
using SkySentry.Core.Clock;
using SkySentry.Core.Frames;

namespace SkySentry.Capture.Sessions;

/// <summary>
/// <see cref="ICameraCaptureSession"/> implementation backed by OpenCvSharp's
/// <see cref="VideoCapture"/> (Media Foundation/DirectShow under the hood on
/// Windows). Runs its own background capture loop on a dedicated long-running
/// task; never touches the UI thread. All device-level failures are caught and
/// surfaced via <see cref="StateChanged"/> rather than thrown, so one camera
/// failing can never take down another session or the app.
/// </summary>
public sealed class OpenCvCameraCaptureSession(
    string sessionLabel,
    ILogger<OpenCvCameraCaptureSession> logger) : ICameraCaptureSession
{
    private const int MaxConsecutiveReadFailures = 10;
    // OpenCV DSHOW owns shared native device-registry state. Serialize lifecycle,
    // while frame reads stay independent on the two capture threads.
    private static readonly object DriverLifecycleGate = new();

    private readonly object _gate = new();
    private VideoCapture? _videoCapture;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private IMonotonicClock? _clock;
    private long _nextSequenceNumber;
    private long _readFailures;
    public long ReadFailureCount => Interlocked.Read(ref _readFailures);
    private volatile bool _disposed;

    public string SessionLabel { get; } = sessionLabel;

    public CameraDeviceInfo? SelectedDevice { get; private set; }

    public CameraCaptureFormat? SelectedFormat { get; private set; }

    public CameraSessionState State { get; private set; } = CameraSessionState.Stopped;

    public event EventHandler<CameraSessionStateChangedEventArgs>? StateChanged;

    public event EventHandler<CapturedFrame>? FrameCaptured;

    public void Configure(CameraDeviceInfo device, CameraCaptureFormat format)
    {
        lock (_gate)
        {
            if (State != CameraSessionState.Stopped)
            {
                throw new InvalidOperationException(
                    $"{SessionLabel}: cannot reconfigure while state is {State}; stop the session first.");
            }

            SelectedDevice = device;
            SelectedFormat = format;
        }

        logger.LogInformation(
            "{Session}: configured device {Device} at {Format}", SessionLabel, device, format);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        CameraDeviceInfo device;
        CameraCaptureFormat format;

        lock (_gate)
        {
            if (SelectedDevice is null || SelectedFormat is null)
            {
                throw new InvalidOperationException($"{SessionLabel}: no device configured.");
            }

            if (State is CameraSessionState.Running or CameraSessionState.Starting)
            {
                return Task.CompletedTask;
            }

            device = SelectedDevice;
            format = SelectedFormat.Value;
        }

        SetState(CameraSessionState.Starting, null);

        _loopCts = new CancellationTokenSource();
        _clock = new MonotonicClock();
        _nextSequenceNumber = 0;
        Interlocked.Exchange(ref _readFailures, 0);

        // The capture loop owns its own long-running background thread so the
        // caller (UI thread) is never blocked on device I/O.
        _loopTask = Task.Factory.StartNew(
            () => RunCaptureLoop(device, format, _loopCts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_gate)
        {
            cts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Session}: capture loop task faulted during stop", SessionLabel);
            }
        }

        cts.Dispose();
        SetState(CameraSessionState.Stopped, null);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("{Session}: restarting", SessionLabel);
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RunCaptureLoop(CameraDeviceInfo device, CameraCaptureFormat format, CancellationToken token)
    {
        VideoCapture? capture = null;
        var consecutiveFailures = 0;

        try
        {
            lock (DriverLifecycleGate)
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                int currentIndex;
                try
                {
                    var identities = devices.Select((d, i) => new CameraDeviceInfo(
                        string.IsNullOrWhiteSpace(d.DevicePath) ? $"{d.Name}#{i}" : d.DevicePath,
                        d.Name ?? $"Camera {i}", i, [])).ToArray();
                    currentIndex = CameraDeviceResolver.ResolveIndex(device.DeviceId, identities);
                }
                finally { foreach (var candidate in devices) candidate.Dispose(); }
                capture = new VideoCapture(currentIndex, VideoCaptureAPIs.DSHOW);

                if (format.Width > 0 && format.Height > 0)
                {
                    capture.Set(VideoCaptureProperties.FrameWidth, format.Width);
                    capture.Set(VideoCaptureProperties.FrameHeight, format.Height);
                }

                if (format.FrameRate > 0)
                {
                    capture.Set(VideoCaptureProperties.Fps, format.FrameRate);
                }

                // High-resolution uncompressed UVC modes may negotiate only 5–10 FPS.
                // Request MJPEG after size/FPS, which otherwise reopen DSHOW in a default subtype.
                if (format.Width >= 1280 && device.DeviceId.Contains("usb#", StringComparison.OrdinalIgnoreCase))
                {
                    var accepted = capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                    logger.LogInformation("{Session}: MJPEG transport request accepted={Accepted}", SessionLabel, accepted);
                }

                if (!capture.IsOpened())
                {
                    SetState(CameraSessionState.Error, $"Unable to open device '{device.FriendlyName}'.");
                    logger.LogError("{Session}: failed to open device {Device}", SessionLabel, device);
                    return;
                }

            }
            lock (_gate)
            {
                _videoCapture = capture;
            }

            SetState(CameraSessionState.Running, null);
            logger.LogInformation("{Session}: stream started on {Device}", SessionLabel, device);

            using var frame = new Mat();
            while (!token.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
                    consecutiveFailures++;
                    Interlocked.Increment(ref _readFailures);
                    logger.LogWarning(
                        "{Session}: frame read failure ({Count}/{Max})",
                        SessionLabel, consecutiveFailures, MaxConsecutiveReadFailures);

                    if (consecutiveFailures >= MaxConsecutiveReadFailures)
                    {
                        SetState(CameraSessionState.Disconnected, "Camera stopped responding.");
                        logger.LogError("{Session}: camera disconnected (too many read failures)", SessionLabel);
                        return;
                    }

                    Thread.Sleep(100);
                    continue;
                }

                consecutiveFailures = 0;
                EmitFrame(frame);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Session}: capture loop crashed", SessionLabel);
            SetState(CameraSessionState.Error, ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _videoCapture = null;
            }

            lock (DriverLifecycleGate)
            {
                capture?.Release();
                capture?.Dispose();
            }
            logger.LogInformation("{Session}: stream stopped", SessionLabel);
        }
    }

    private void EmitFrame(Mat frame)
    {
        var clock = _clock;
        if (clock is null)
        {
            return;
        }

        var stride = (int)frame.Step();
        var height = frame.Rows;
        var width = frame.Cols;
        var buffer = new byte[stride * height];
        Marshal.Copy(frame.Data, buffer, 0, buffer.Length);

        var sequence = Interlocked.Increment(ref _nextSequenceNumber) - 1;
        var timestamp = new FrameTimestamp(sequence, clock.Elapsed, clock.UtcNowApproximate);
        var captured = new CapturedFrame(buffer, width, height, stride, FramePixelFormat.Bgr24, timestamp);

        FrameCaptured?.Invoke(this, captured);
    }

    private void SetState(CameraSessionState newState, string? message)
    {
        CameraSessionState previous;
        lock (_gate)
        {
            previous = State;
            State = newState;
        }

        if (previous == newState && message is null)
        {
            return;
        }

        StateChanged?.Invoke(this, new CameraSessionStateChangedEventArgs(previous, newState, message));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Session}: error stopping session during dispose", SessionLabel);
        }
    }
}
