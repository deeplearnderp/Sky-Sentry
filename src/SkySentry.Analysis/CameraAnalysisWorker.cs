using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkySentry.Core.Analysis;
using SkySentry.Core.Frames;

namespace SkySentry.Analysis;

/// <summary>Low-priority, rate-limited analysis. No callbacks to capture or WPF.</summary>
public sealed class CameraAnalysisWorker : IDisposable
{
    private readonly LatestAnalysisQueue _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private readonly AnalysisOptions _options;
    private AnalysisSnapshot? _latest;
    private long _throttled;
    private int _disposed;
    public CameraAnalysisWorker(string cameraId, string label, AnalysisOptions options, CameraCalibration? calibration, ILogger logger)
    {
        options.Validate(); calibration?.Validate(); _options = options;
        _task = Task.Factory.StartNew(() => Run(cameraId, label, calibration, logger), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public AnalysisSnapshot? Latest => Volatile.Read(ref _latest);
    public long DroppedFrames => _queue.Dropped + Interlocked.Read(ref _throttled);
    public void Submit(CapturedFrame frame) { if (_options.Enabled) _queue.Submit(frame); }
    private void Run(string cameraId, string label, CameraCalibration? calibration, ILogger logger)
    {

        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            using var detector = new MotionDetector(_options);
            TrackEngine? tracker = null;
            TimeSpan? last = null;
            int width = 0, height = 0;
            var clock = new Stopwatch();
            long processed = 0;
            var lastProcessingElapsed = TimeSpan.Zero;
            while (!_cts.IsCancellationRequested)
            {
                // Pace processing by real elapsed time too, even for a burst source.
                var interval = TimeSpan.FromSeconds(1 / _options.MaxFramesPerSecond);
                var delay = processed > 0 ? interval - (clock.Elapsed - lastProcessingElapsed) : TimeSpan.Zero;
                if (delay > TimeSpan.Zero && _cts.Token.WaitHandle.WaitOne(delay)) break;
                CapturedFrame? frame;
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    wait.CancelAfter(_options.TrackTimeout);
                    try { frame = _queue.TakeAsync(wait.Token).AsTask().GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                    {
                        tracker = null;
                        Volatile.Write(ref _latest, new AnalysisSnapshot(width, height, last ?? TimeSpan.Zero, [],
                            "Waiting for frames", processed / Math.Max(0.001, clock.Elapsed.TotalSeconds), DroppedFrames, detector.SuppressedFrames));
                        continue;
                    }
                }
                if (frame is null) break;
                if (!clock.IsRunning) clock.Start();
                if (last is { } previous && frame.Timestamp.MonotonicElapsed <= previous)
                { Interlocked.Increment(ref _throttled); continue; }
                if (tracker is null || width != frame.Width || height != frame.Height)
                {
                    width = frame.Width; height = frame.Height;
                    var matching = calibration?.Matches(cameraId, width, height) == true ? calibration : null;
                    tracker = new TrackEngine(cameraId, label, _options, matching);
                }
                last = frame.Timestamp.MonotonicElapsed;
                lastProcessingElapsed = clock.Elapsed;
                var detections = detector.Detect(frame);
                var tracks = tracker.Update(detections, last.Value, frame.Timestamp.WallClockUtc);
                processed++;
                Volatile.Write(ref _latest, new AnalysisSnapshot(width, height, last.Value, tracks, detector.Status,
                    processed / Math.Max(0.001, clock.Elapsed.TotalSeconds), DroppedFrames, detector.SuppressedFrames));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Camera}: analysis stopped; recording continues.", label);
            Volatile.Write(ref _latest, new AnalysisSnapshot(0, 0, TimeSpan.Zero, [], "Analysis error: " + ex.Message, 0, DroppedFrames, 0));
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel(); _queue.Dispose();
        _task.GetAwaiter().GetResult();
        _cts.Dispose();
    }
}
