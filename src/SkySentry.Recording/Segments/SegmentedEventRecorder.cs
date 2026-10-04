using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkySentry.Core;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Recording;

namespace SkySentry.Recording.Segments;

/// <summary>
/// <see cref="IEventRecorder"/> implementation built directly on
/// <see cref="ISegmentedCameraRecorder"/>: pre-event footage is "already there"
/// in each camera's rolling segment buffer, so triggering an event is just
/// protecting the right segments from cleanup, waiting for the configured
/// post-event duration to elapse, then concatenating the selected segments into
/// a single clip per camera. See docs/RECORDING_PIPELINE.md.
/// </summary>
/// <remarks>
/// Overlapping-trigger policy: a second <see cref="TriggerEventAsync"/> call
/// made while one is already in progress is rejected immediately with
/// <see cref="EventAlreadyInProgressException"/> (see docs/DECISIONS.md) — the
/// simplest, easiest-to-reason-about choice, enforced here with a non-blocking
/// semaphore acquisition rather than scattered boolean flags.
/// </remarks>
public sealed class SegmentedEventRecorder : IEventRecorder
{
    private static readonly TimeSpan PostEventPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PostEventSafetyGrace = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly IReadOnlyList<ISegmentedCameraRecorder> _cameras;
    private readonly RecordingOptions _options;
    private readonly ILogger _logger;
    private readonly Func<IReadOnlyList<SkySentry.Core.Analysis.TrackEventReference>>? _trackProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private EventLifecycleState _state = EventLifecycleState.Idle;
    private CancellationTokenSource? _shutdownEarlyCts;

    public SegmentedEventRecorder(IReadOnlyList<ISegmentedCameraRecorder> cameras, RecordingOptions options, ILogger logger, Func<IReadOnlyList<SkySentry.Core.Analysis.TrackEventReference>>? trackProvider = null)
    {
        options.Validate();
        _cameras = cameras;
        _options = options;
        _logger = logger;
        _trackProvider = trackProvider;
    }

    public EventLifecycleState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            if (StateChanged is { } handlers)
                foreach (EventHandler<EventLifecycleState> handler in handlers.GetInvocationList())
                    try { handler(this, value); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Event lifecycle observer failed."); }
        }
    }

    public event EventHandler<EventLifecycleState>? StateChanged;

    public async Task<SkyEventMetadata> TriggerEventAsync(CancellationToken cancellationToken = default)
    {
        if (!await _lock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new EventAlreadyInProgressException(
                "An event is already being captured or finalized; please wait for it to complete.");
        }

        var triggerPoints = new Dictionary<ISegmentedCameraRecorder, TimeSpan>();

        try
        {
            State = EventLifecycleState.Triggered;
            var eventId = Guid.NewGuid().ToString("N");
            var triggeredAtUtc = DateTimeOffset.UtcNow;
            var sourceDevices = _cameras.ToDictionary(camera => camera.CameraLabel, camera => camera.SourceDevice);
            IReadOnlyList<SkySentry.Core.Analysis.TrackEventReference> associatedTracks = [];
            try { associatedTracks = _trackProvider?.Invoke().Take(512).ToArray() ?? []; }
            catch (Exception ex) { _logger.LogWarning(ex, "Track snapshot unavailable; preserving recording."); }

            _shutdownEarlyCts = new CancellationTokenSource();

            // Secure the pre-event window immediately, before doing anything else
            // that could race with each camera's normal stale-segment cleanup.
            foreach (var camera in _cameras)
            {
                if (!camera.IsRunning || camera.ActiveFormat is null)
                {
                    continue;
                }

                triggerPoints[camera] = camera.CaptureTriggerPoint(_options.PreEventDuration, _options.PostEventDuration);
            }

            State = EventLifecycleState.CapturingPostEvent;

            if (triggerPoints.Count > 0)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownEarlyCts.Token);
                await WaitForPostEventWindowAsync(triggerPoints, linked.Token).ConfigureAwait(false);
            }

            foreach (var camera in triggerPoints.Keys)
            {
                try { await camera.FlushAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "{Camera}: final flush failed; attempting to preserve closed segments.", camera.CameraLabel); }
            }
            State = EventLifecycleState.Finalizing;
            var metadata = await FinalizeEventAsync(eventId, triggeredAtUtc, triggerPoints, associatedTracks, sourceDevices).ConfigureAwait(false);

            State = metadata.Cameras.Any(c => c.Success) ? EventLifecycleState.Completed : EventLifecycleState.Failed;
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event trigger/finalize failed");
            State = EventLifecycleState.Failed;
            throw;
        }
        finally
        {
            foreach (var camera in triggerPoints.Keys)
            {
                try
                {
                    camera.EndEventRetention();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release event retention for {Camera}", camera.CameraLabel);
                }
            }

            _shutdownEarlyCts?.Dispose();
            _shutdownEarlyCts = null;
            State = EventLifecycleState.Idle;
            _lock.Release();
        }
    }

    public async Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Ask any in-flight post-event wait to finish early so finalize proceeds
        // immediately with whatever has been captured so far — Sky Sentry must
        // never hang indefinitely on shutdown (see docs/DECISIONS.md).
        _shutdownEarlyCts?.Cancel();

        try
        {
            if (await _lock.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                // Acquiring the lock means any in-flight trigger has finished (or
                // there was none running); release it immediately, we were only
                // using it as a completion signal.
                _lock.Release();
            }
            else
            {
                throw new TimeoutException("Event is still finalizing; camera buffers have been preserved. Try closing again.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Shutdown cancelled while waiting for an in-progress event to finalize.");
        }
    }

    private async Task WaitForPostEventWindowAsync(
        Dictionary<ISegmentedCameraRecorder, TimeSpan> triggerPoints, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var limit = _options.PostEventDuration + _options.SegmentDuration + PostEventSafetyGrace;

        while (elapsed.Elapsed < limit)
        {
            var allSatisfied = true;

            foreach (var (camera, triggerMonotonic) in triggerPoints)
            {
                if (!camera.IsRunning)
                {
                    // Camera stopped mid-wait; nothing more will arrive for it, so
                    // don't block the other camera's post-event capture on it.
                    continue;
                }

                var segments = camera.GetSegmentsSnapshot();
                var latestEnd = segments.Count > 0 ? segments[^1].EndMonotonic : TimeSpan.Zero;
                if (latestEnd < triggerMonotonic + _options.PostEventDuration)
                {
                    allSatisfied = false;
                    break;
                }
            }

            if (allSatisfied)
            {
                return;
            }

            try
            {
                await Task.Delay(PostEventPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // Shutdown requested an early finalize.
            }
        }

        _logger.LogWarning(
            "Post-event capture window timed out before all cameras reached the target duration; finalizing with whatever is available.");
    }

    private async Task<SkyEventMetadata> FinalizeEventAsync(
        string eventId, DateTimeOffset triggeredAtUtc, Dictionary<ISegmentedCameraRecorder, TimeSpan> triggerPoints, IReadOnlyList<SkySentry.Core.Analysis.TrackEventReference> associatedTracks, IReadOnlyDictionary<string, CameraDeviceInfo?> sourceDevices)
    {
        var eventDirectory = CreateEventDirectory(eventId, triggeredAtUtc);
        var results = new List<CameraRecordingResult>();

        foreach (var camera in _cameras)
        {
            if (!triggerPoints.TryGetValue(camera, out var triggerMonotonic))
            {
                results.Add(FailedResult(camera.CameraLabel, camera.FailureReason ?? "Camera was not running when the event was triggered."));
                continue;
            }

            try
            {
                var result = await FinalizeCameraAsync(camera, eventDirectory, triggerMonotonic).ConfigureAwait(false);
                results.Add(result);
                try { camera.EndEventRetention(); }
                catch (Exception releaseError) { _logger.LogWarning(releaseError, "Will retry releasing {Camera} retention", camera.CameraLabel); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Camera}: failed to finalize event clip", camera.CameraLabel);
                results.Add(FailedResult(camera.CameraLabel, $"Exception while finalizing: {ex.Message}"));
            }
        }

        results = results.Select(result =>
        {
            var source = sourceDevices[result.CameraLabel];
            return result with { DeviceId = source?.DeviceId, DeviceFriendlyName = source?.FriendlyName };
        }).ToList();
        var metadata = new SkyEventMetadata(
            eventId,
            triggeredAtUtc,
            DateTimeOffset.UtcNow,
            AppVersion.Current,
            _options.PreEventDuration,
            _options.PostEventDuration,
            results,
            Array.Empty<TrackAnalysisResult>()) { AssociatedTracks = associatedTracks };

        var metadataPath = Path.Combine(eventDirectory, "metadata.json");
        var json = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        await File.WriteAllTextAsync(metadataPath + ".tmp", json).ConfigureAwait(false);
        File.Move(metadataPath + ".tmp", metadataPath, overwrite: true);

        _logger.LogInformation(
            "Event {EventId} finalized: {SuccessCount}/{Total} camera(s) saved.",
            eventId, results.Count(r => r.Success), results.Count);

        return metadata;
    }

    private async Task<CameraRecordingResult> FinalizeCameraAsync(
        ISegmentedCameraRecorder camera, string eventDirectory, TimeSpan triggerMonotonic)
    {
        var format = camera.ActiveFormat;
        if (format is null)
        {
            return FailedResult(camera.CameraLabel, "Camera has no active capture format.");
        }

        var allSegments = camera.GetSegmentsSnapshot();
        var rawWindowStart = triggerMonotonic - _options.PreEventDuration;
        var windowStart = rawWindowStart < TimeSpan.Zero ? TimeSpan.Zero : rawWindowStart;
        var windowEnd = triggerMonotonic + _options.PostEventDuration;

        var selected = EventWindowMath.SelectSegmentsInWindow(allSegments, windowStart, windowEnd)
            .OrderBy(s => s.SequenceNumber)
            .ToList();

        if (selected.Count == 0)
        {
            return FailedResult(camera.CameraLabel, "No buffered footage was available for this camera.");
        }

        var actualPre = EventWindowMath.CalculateActualPreEventDuration(selected, triggerMonotonic, _options.PreEventDuration);
        var actualPost = EventWindowMath.CalculateActualPostEventDuration(selected, triggerMonotonic);

        var safeLabel = SanitizeFileName(camera.CameraLabel);
        var videoRelativePath = $"{safeLabel}.mp4";
        var thumbnailRelativePath = $"{safeLabel}-thumbnail.jpg";
        var videoPath = Path.Combine(eventDirectory, videoRelativePath);
        var thumbnailPath = Path.Combine(eventDirectory, thumbnailRelativePath);

        var producedOutput = await Task.Run(
            () => ConcatenateSegments(selected, videoPath, thumbnailPath, format.Value, triggerMonotonic)).ConfigureAwait(false);

        if (!producedOutput)
        {
            return FailedResult(camera.CameraLabel, "Segment files were missing or unreadable at finalize time.");
        }

        return new CameraRecordingResult(
            camera.CameraLabel,
            Success: true,
            FailureReason: null,
            VideoRelativePath: videoRelativePath,
            ThumbnailRelativePath: thumbnailRelativePath,
            Width: format.Value.Width,
            Height: format.Value.Height,
            RequestedFps: format.Value.FrameRate,
            AchievedFps: camera.AchievedFps,
            ActualPreEventDuration: actualPre,
            ActualPostEventDuration: actualPost,
            RecordingStartUtc: selected[0].StartWallClockUtc,
            RecordingEndUtc: selected[^1].EndWallClockUtc,
            DroppedFrameCount: camera.DroppedFrameCount)
        {
            TriggerMonotonic = triggerMonotonic,
            RecordingStartMonotonic = selected[0].StartMonotonic,
            RecordingEndMonotonic = selected[^1].EndMonotonic,
            PlaybackFrameRate = CalculatePlaybackFps(selected, format.Value.FrameRate),
            Warning = camera.FailureReason
        };
    }

    /// <summary>
    /// Decodes each selected segment and re-encodes its frames into a single
    /// destination clip (simpler and more robust than file-level/container-level
    /// concatenation, at the cost of a decode+reencode pass — acceptable for
    /// short clips; see docs/RECORDING_PIPELINE.md). Also grabs a thumbnail from
    /// the segment nearest the trigger point. Returns false if no frames at all
    /// could be read (e.g. every selected segment file had already been deleted).
    /// </summary>
    private static double CalculatePlaybackFps(IReadOnlyList<RecordedSegment> segments, double requestedFps)
    {
        // OpenCV has constant-FPS output. Set the event rate from capture timestamps
        // so playback duration matches the captured span even when capture runs slowly.
        var seconds = (segments[^1].EndMonotonic - segments[0].StartMonotonic).TotalSeconds + 1 / requestedFps;
        return segments.Sum(s => (long)s.FrameCount) / seconds;
    }
    private static bool ConcatenateSegments(
        IReadOnlyList<RecordedSegment> segments, string outputPath, string thumbnailPath,
        CameraCaptureFormat format, TimeSpan triggerMonotonic)
    {
        var fps = CalculatePlaybackFps(segments, format.FrameRate);
        using var writer = new VideoWriter(
            outputPath, FourCC.FromFourChars('m', 'p', '4', 'v'), fps, new OpenCvSharp.Size(format.Width, format.Height));

        if (!writer.IsOpened())
        {
            throw new InvalidOperationException($"Failed to open event output writer for '{outputPath}'.");
        }

        var thumbnailSegment = segments
            .OrderBy(s => Math.Abs(((s.StartMonotonic + s.EndMonotonic).Ticks / 2) - triggerMonotonic.Ticks))
            .First();

        var framesWritten = 0;
        var thumbnailWritten = false;

        foreach (var segment in segments)
        {
            if (!File.Exists(segment.FilePath))
            {
                throw new IOException("A protected segment is missing: " + segment.FilePath);
            }

            using var capture = new VideoCapture(segment.FilePath);
            if (!capture.IsOpened()) throw new IOException("A protected segment is unreadable: " + segment.FilePath);

            using var frame = new Mat();
            var midpointFrameIndex = segment.FrameCount / 2;
            var frameIndexInSegment = 0;

            while (capture.Read(frame) && !frame.Empty())
            {
                writer.Write(frame);
                framesWritten++;

                if (!thumbnailWritten && ReferenceEquals(segment, thumbnailSegment) && frameIndexInSegment == midpointFrameIndex)
                {
                    if (!Cv2.ImWrite(thumbnailPath, frame)) throw new IOException("Unable to write thumbnail.");
                    thumbnailWritten = true;
                }

                frameIndexInSegment++;
            }
            if (frameIndexInSegment != segment.FrameCount) throw new IOException("A segment could not be completely decoded.");
        }

        if (framesWritten == 0)
        {
            return false;
        }

        if (!thumbnailWritten)
        {
            TryWriteFallbackThumbnail(segments, thumbnailPath);
        }

        return true;
    }

    private static void TryWriteFallbackThumbnail(IReadOnlyList<RecordedSegment> segments, string thumbnailPath)
    {
        foreach (var segment in segments)
        {
            if (!File.Exists(segment.FilePath))
            {
                continue;
            }

            using var capture = new VideoCapture(segment.FilePath);
            using var frame = new Mat();
            if (capture.IsOpened() && capture.Read(frame) && !frame.Empty())
            {
                if (!Cv2.ImWrite(thumbnailPath, frame)) throw new IOException("Unable to write thumbnail.");
                return;
            }
        }
    }

    private string CreateEventDirectory(string eventId, DateTimeOffset triggeredAtUtc)
    {
        var directory = Path.Combine(
            _options.EventsDirectory,
            triggeredAtUtc.Year.ToString("D4"),
            triggeredAtUtc.Month.ToString("D2"),
            triggeredAtUtc.Day.ToString("D2"),
            eventId);

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static CameraRecordingResult FailedResult(string cameraLabel, string reason) =>
        new(
            cameraLabel, Success: false, FailureReason: reason,
            VideoRelativePath: null, ThumbnailRelativePath: null,
            Width: 0, Height: 0, RequestedFps: 0, AchievedFps: 0,
            ActualPreEventDuration: TimeSpan.Zero, ActualPostEventDuration: TimeSpan.Zero,
            RecordingStartUtc: default, RecordingEndUtc: default, DroppedFrameCount: 0);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).ToArray();
        var sanitized = new string(chars).Replace(' ', '_');
        return sanitized.Length == 0 ? "camera" : sanitized;
    }
}
