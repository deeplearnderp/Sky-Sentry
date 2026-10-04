using System.Windows.Input;
using SkySentry.Capture.Sessions;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SkySentry.Core.Cameras;
using SkySentry.Core.Frames;
using SkySentry.Core.Recording;
using SkySentry.Core.Analysis;
using SkySentry.Analysis;
using SkySentry.App.Services;

namespace SkySentry.App.ViewModels;

/// <summary>
/// Drives one camera panel: device/format selection, start/stop/restart, a
/// bounded "latest frame wins" preview pipeline (no unbounded queue — see
/// docs/CAMERA_PIPELINE.md), achieved-FPS measurement, and the segmented rolling
/// buffer backing that camera's "Save Event" contribution (see
/// docs/RECORDING_PIPELINE.md).
/// </summary>
public sealed class CameraPanelViewModel : ViewModelBase, IDisposable
{
    private readonly ICameraCaptureSession _session;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Func<CameraDeviceInfo, CameraPanelViewModel, bool> _isDeviceAlreadySelectedElsewhere;
    private readonly DispatcherTimer _uiTimer;

    private readonly AnalysisSettingsStore _analysisSettings;
    private CameraAnalysisWorker? _analysis;
    private AnalysisSnapshot? _analysisSnapshot;
    private AnalysisOptions _analysisOptions = new();
    private string _horizontalFov = "", _verticalFov = "";
    private string _calibrationStatus = "FOV unset — pixel measurements only";
    private bool _analysisEnabled = true;
    private OverlayOptions _overlays = new();
    private string? _highlightTrackId;
    private long _captureFrames, _lastCaptureFrames;
    private long _lastFrameUtcTicks;
    public DateTimeOffset? LatestFrameUtc => Interlocked.Read(ref _lastFrameUtcTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    public string CaptureDiagnostics => $"Read failures {(_session as OpenCvCameraCaptureSession)?.ReadFailureCount ?? 0} · device-side frame losses unavailable";
    private double _captureFps;
    private bool _disposed;
    private CapturedFrame? _latestFrame;
    private WriteableBitmap? _bitmap;

    private CameraDeviceInfo? _selectedDevice;
    private CameraCaptureFormat? _selectedFormat;
    private CameraSessionState _state = CameraSessionState.Stopped;
    private string _statusMessage = "No device selected.";
    private double _achievedFps;
    private bool _allowDuplicateDevice;
    private BitmapSource? _previewImage;

    public CameraPanelViewModel(
        string sessionLabel,
        ICameraCaptureSession session,
        ISegmentedCameraRecorder recorder,
        ILogger logger,
        Dispatcher dispatcher,
        Func<CameraDeviceInfo, CameraPanelViewModel, bool> isDeviceAlreadySelectedElsewhere, AnalysisSettingsStore? analysisSettings = null)
    {
        _analysisSettings = analysisSettings ?? new();
        ApplyCalibrationCommand = new RelayCommand(ApplyCalibration);
        SessionLabel = sessionLabel;
        _session = session;
        Recorder = recorder;
        _logger = logger;
        _dispatcher = dispatcher;
        _isDeviceAlreadySelectedElsewhere = isDeviceAlreadySelectedElsewhere;

        _session.StateChanged += OnSessionStateChanged;
        _session.FrameCaptured += OnFrameCaptured;

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => CanStop);

        // UI-side timer: pulls at most one frame per tick into the WriteableBitmap
        // and refreshes the achieved-FPS counter once per tick. Decouples render
        // rate from capture rate and guarantees the UI thread only ever does a
        // bounded, fixed amount of work regardless of camera FPS.
        _uiTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    public string SessionLabel { get; }

    /// <summary>Segmented rolling buffer backing this camera; read by the Save Event flow.</summary>
    public ISegmentedCameraRecorder Recorder { get; }

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = [];

    public ObservableCollection<CameraCaptureFormat> AvailableFormats { get; } = [];

    public CameraDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (value is not null && _isDeviceAlreadySelectedElsewhere(value, this) && !AllowDuplicateDevice)
            {
                StatusMessage = $"'{value.FriendlyName}' is already selected on another camera panel.";
                OnPropertyChanged(nameof(SelectedDevice));
                return;
            }

            if (SetField(ref _selectedDevice, value))
            {
                RefreshAvailableFormats();
                LoadAnalysisSettings();
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(CameraIdentity));
                OnPropertyChanged(nameof(CalibrationSummary));
                StatusMessage = value is null ? "No device selected." : $"Selected {value.FriendlyName}.";
                _logger.LogInformation("{Session}: camera selected {Device}", SessionLabel, value);
            }
        }
    }

    public CameraCaptureFormat? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetField(ref _selectedFormat, value) && value is not null)
            {
                _logger.LogInformation("{Session}: selected mode {Format}", SessionLabel, value);
                LoadAnalysisSettings();
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(CameraIdentity));
            }
        }
    }

    public bool AllowDuplicateDevice
    {
        get => _allowDuplicateDevice;
        set => SetField(ref _allowDuplicateDevice, value);
    }

    public CameraSessionState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(CanConfigure));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanConfigure => !CanStop;
    public bool IsConnected => State == CameraSessionState.Running;

    public bool CanStart =>
        State is CameraSessionState.Stopped or CameraSessionState.Error or CameraSessionState.Disconnected
        && SelectedDevice is not null && SelectedFormat is not null;

    public bool CanStop => State is CameraSessionState.Running or CameraSessionState.Starting;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public double AchievedFps
    {
        get => _achievedFps;
        private set => SetField(ref _achievedFps, value);
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetField(ref _previewImage, value);
    }

    public double CaptureFps { get => _captureFps; private set => SetField(ref _captureFps, value); }
    public AnalysisSnapshot? AnalysisSnapshot { get => _analysisSnapshot; private set => SetField(ref _analysisSnapshot, value); }
    public string HorizontalFov { get => _horizontalFov; set => SetField(ref _horizontalFov, value); }
    private bool _fovMeasured;
    public bool FovMeasured { get => _fovMeasured; set => SetField(ref _fovMeasured, value); }
    public string CameraIdentity => SelectedDevice?.FriendlyName ?? "No camera selected";
    public string CaptureMode => _bitmap is not null && IsConnected ? $"{_bitmap.PixelWidth}×{_bitmap.PixelHeight}" : SelectedFormat?.ToString() ?? "Mode unset";
    public string CalibrationSummary
    {
        get
        {
            if (SelectedDevice is null || SelectedFormat is null) return "Calibration: Missing";
            var width = IsConnected && _bitmap is not null ? _bitmap.PixelWidth : SelectedFormat.Value.Width;
            var height = IsConnected && _bitmap is not null ? _bitmap.PixelHeight : SelectedFormat.Value.Height;
            var calibration = _analysisSettings.ForDevice(SelectedDevice.DeviceId).ForResolution(SelectedDevice.DeviceId, width, height);
            return "Calibration: " + (calibration is null ? "Missing" : calibration.IsProvisional ? "Provisional" : "Configured") + " · " + CaptureMode;
        }
    }
    public string VerticalFov { get => _verticalFov; set => SetField(ref _verticalFov, value); }
    public string CalibrationStatus { get => _calibrationStatus; private set => SetField(ref _calibrationStatus, value); }
    public RelayCommand ApplyCalibrationCommand { get; }
    public OverlayOptions Overlays { get => _overlays; private set => SetField(ref _overlays, value); }
    public bool ShowBoxes { get => Overlays.Boxes; set => Overlays = Overlays with { Boxes = value }; }
    public bool ShowIds { get => Overlays.Ids; set => Overlays = Overlays with { Ids = value }; }
    public bool ShowTrajectories { get => Overlays.Trajectories; set => Overlays = Overlays with { Trajectories = value }; }
    public bool ShowSpeeds { get => Overlays.Speeds; set => Overlays = Overlays with { Speeds = value }; }
    public bool ShowDiagnostics { get => Overlays.Diagnostics; set => Overlays = Overlays with { Diagnostics = value }; }
    public string? HighlightTrackId { get => _highlightTrackId; set => SetField(ref _highlightTrackId, value); }
    public bool AnalysisEnabled
    {
        get => _analysisEnabled;
        set { if (SetField(ref _analysisEnabled, value)) RestartAnalysis(); }
    }
    public string AnalysisStatus => !AnalysisEnabled ? "Analysis paused" : AnalysisSnapshot?.Status ?? "Analysis waiting";
    public string BufferStatus => Recorder.FailureReason is { } error ? "Recording error: " + error :
        Recorder.IsRunning ? $"Rolling · {Recorder.GetSegmentsSnapshot().Count} segments · {Recorder.DroppedFrameCount} dropped" : "Recording idle";
    public IReadOnlyList<TrackEventReference> GetTrackReferences() =>
        (Volatile.Read(ref _analysis)?.Latest?.Tracks ?? []).Select(t => new TrackEventReference(t.TrackId, t.CameraId, t.CameraLabel,
            "Manual Save Event", t.Duration, t.PeakAngularSpeed, t.Measurement?.DirectionChangeDegrees)).ToArray();
    private void LoadAnalysisSettings()
    {
        if (SelectedDevice is null) return;
        var settings = _analysisSettings.ForDevice(SelectedDevice.DeviceId);
        _analysisOptions = settings.Options;
        _analysisEnabled = settings.Options.Enabled;
        OnPropertyChanged(nameof(AnalysisEnabled));
        var calibration = SelectedFormat is { } format ? settings.ForResolution(SelectedDevice.DeviceId, format.Width, format.Height) : null;
        FovMeasured = calibration?.IsProvisional == false;
        HorizontalFov = calibration?.HorizontalFovDegrees.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
        VerticalFov = calibration?.VerticalFovDegrees.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
        CalibrationStatus = calibration is null ? "FOV unset — pixel measurements only" : "Saved FOV; applies only to matching resolution";
    }
    private void RestartAnalysis()
    {
        OnPropertyChanged(nameof(CalibrationSummary));
        var old = Interlocked.Exchange(ref _analysis, null);
        old?.Dispose();
        AnalysisSnapshot = null;
        if (AnalysisEnabled && SelectedDevice is { } device && Recorder.IsRunning)
        {
            var settings = _analysisSettings.ForDevice(device.DeviceId);
            var worker = new CameraAnalysisWorker(device.DeviceId, SessionLabel, _analysisOptions with { Enabled = true },
                SelectedFormat is { } mode ? settings.ForResolution(device.DeviceId, mode.Width, mode.Height) : null, _logger);
            Interlocked.Exchange(ref _analysis, worker);
        }
        OnPropertyChanged(nameof(AnalysisStatus));
    }
    private void ApplyCalibration()
    {
        try
        {
            if (SelectedDevice is null || SelectedFormat is null) throw new ArgumentException("Select a camera and format first.");
            if (!double.TryParse(HorizontalFov, out var horizontal) || !double.TryParse(VerticalFov, out var vertical))
                throw new ArgumentException("Enter both FOV values in degrees.");
            var width = IsConnected && _bitmap is not null ? _bitmap.PixelWidth : SelectedFormat.Value.Width;
            var height = IsConnected && _bitmap is not null ? _bitmap.PixelHeight : SelectedFormat.Value.Height;
            var calibration = new CameraCalibration(SelectedDevice.DeviceId, width, height, horizontal, vertical) { IsProvisional = !FovMeasured };
            _analysisSettings.Save(SelectedDevice.DeviceId, calibration);
            CalibrationStatus = $"Approximate FOV · {width}×{height} · {horizontal:0.##}° × {vertical:0.##}°";
            RestartAnalysis();
        }
        catch (Exception ex) { CalibrationStatus = ex.Message; _logger.LogWarning(ex, "{Camera}: calibration rejected", SessionLabel); }
    }
    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand RestartCommand { get; }

    public void RefreshAvailableFormats()
    {
        AvailableFormats.Clear();
        if (_selectedDevice is null)
        {
            return;
        }

        if (_selectedDevice.SupportedFormats.Count > 0)
        {
            foreach (var format in _selectedDevice.SupportedFormats)
            {
                AvailableFormats.Add(format);
            }
        }
        else
        {
            // Driver didn't expose capabilities (see docs/CAMERA_PIPELINE.md) —
            // offer common fallback formats so the device is still usable.
            AvailableFormats.Add(new CameraCaptureFormat(1280, 720, 30));
            AvailableFormats.Add(new CameraCaptureFormat(640, 480, 30));
        }

        SelectedFormat = AvailableFormats.Count > 0 ? AvailableFormats[0] : null;
    }

    private async Task StartAsync()
    {
        if (SelectedDevice is null || SelectedFormat is null)
        {
            return;
        }

        try
        {
            await _session.StopAsync();
            Recorder.Stop();
            _session.Configure(SelectedDevice, SelectedFormat.Value);
            Recorder.Start(SelectedFormat.Value, SelectedDevice);
            _captureFrames = _lastCaptureFrames = 0;
            Interlocked.Exchange(ref _lastFrameUtcTicks, 0);
            RestartAnalysis();
            await _session.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Session}: failed to start", SessionLabel);
            StatusMessage = $"Failed to start: {ex.Message}";
            Recorder.Stop();
        }
    }

    private async Task StopAsync()
    {
        await _session.StopAsync();
        Recorder.Stop();
        Interlocked.Exchange(ref _analysis, null)?.Dispose();
        AnalysisSnapshot = null;
    }

    private async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    private void OnSessionStateChanged(object? sender, CameraSessionStateChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            State = e.NewState;
            if (e.NewState is CameraSessionState.Error or CameraSessionState.Disconnected)
            {
                Recorder.Stop();
                Interlocked.Exchange(ref _analysis, null)?.Dispose();
                AnalysisSnapshot = null;
            }
            if (e.Message is not null)
            {
                StatusMessage = e.Message;
            }
        });
    }

    private void OnFrameCaptured(object? sender, CapturedFrame frame)
    {
        // Runs on the background capture thread: store only the latest frame
        // (bounded, single-slot "mailbox") for preview rendering, and hand the
        // frame to the segmented recorder's own bounded encode queue. Never does
        // UI work here.
        Interlocked.Exchange(ref _latestFrame, frame);
        // Recording is submitted before any analysis work.
        Recorder.AddFrame(frame);
        Interlocked.Increment(ref _captureFrames);
        Interlocked.Exchange(ref _lastFrameUtcTicks, frame.Timestamp.WallClockUtc.UtcTicks);
        Volatile.Read(ref _analysis)?.Submit(frame);
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        AnalysisSnapshot = Volatile.Read(ref _analysis)?.Latest;
        var frame = Interlocked.Exchange(ref _latestFrame, null);
        if (frame is not null)
        {
            RenderFrame(frame);
        }
    }

    private void RenderFrame(CapturedFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null);
            PreviewImage = _bitmap;
            OnPropertyChanged(nameof(CalibrationSummary));
            OnPropertyChanged(nameof(CaptureMode));
        }

        var rect = new System.Windows.Int32Rect(0, 0, frame.Width, frame.Height);
        _bitmap.WritePixels(rect, frame.Pixels, frame.Stride, 0);
    }

    /// <summary>Call roughly once per second from the owning window's health timer.</summary>
    public void TickFpsWindow(TimeSpan elapsedSinceLastTick)
    {
        AchievedFps = Recorder.AchievedFps;
        var count = Interlocked.Read(ref _captureFrames);
        CaptureFps = elapsedSinceLastTick.TotalSeconds > 0 ? (count - _lastCaptureFrames) / elapsedSinceLastTick.TotalSeconds : 0;
        _lastCaptureFrames = count;
        OnPropertyChanged(nameof(BufferStatus));
        OnPropertyChanged(nameof(CaptureDiagnostics));
        OnPropertyChanged(nameof(CalibrationSummary));
        OnPropertyChanged(nameof(AnalysisStatus));
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _analysis, null)?.Dispose();
        _uiTimer.Stop();
        _session.StateChanged -= OnSessionStateChanged;
        _session.FrameCaptured -= OnFrameCaptured;
        _session.Dispose();
    }
}
