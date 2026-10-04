using SkySentry.Core.Cameras;
using SkySentry.Core.Frames;

namespace SkySentry.Core.Recording;

/// <summary>
/// Owns one camera's continuous, bounded, disk-backed rolling segment buffer.
/// Replaces Phase 1's <c>IFrameBuffer</c> (which kept full raw frames in RAM) as
/// the production pre/post-event retention mechanism — see docs/DECISIONS.md and
/// docs/RECORDING_PIPELINE.md for the RAM-usage calculation that motivated this.
/// Implemented in SkySentry.Recording (<c>SegmentedCameraRecorder</c>), which
/// needs OpenCvSharp for encoding; this interface stays dependency-free so
/// consumers/tests in Core do not need to reference OpenCvSharp.
/// </summary>
public interface ISegmentedCameraRecorder : IDisposable
{
    /// <summary>Stable label for this camera, e.g. "Camera 1".</summary>
    string CameraLabel { get; }
    CameraDeviceInfo? SourceDevice { get; }
    string? FailureReason { get; }
    /// <summary>Atomically captures the latest point and pins the requested event interval. Null post duration retains the legacy unbounded hold.</summary>
    TimeSpan CaptureTriggerPoint(TimeSpan preEventDuration, TimeSpan? postEventDuration = null);
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>The format <see cref="Start"/> was called with, if running.</summary>
    CameraCaptureFormat? ActiveFormat { get; }

    /// <summary>
    /// Frames dropped because the internal bounded encode queue was full (the
    /// encoder could not keep up with the capture rate). Zero under normal load;
    /// it is a counter, not a queue.
    /// </summary>
    long DroppedFrameCount { get; }

    /// <summary>Measured average frames/second actually written since <see cref="Start"/>.</summary>
    double AchievedFps { get; }

    /// <summary>
    /// Begins rolling segment recording at the given format. Clears any leftover
    /// segment files from a previous run in this camera's buffer subdirectory
    /// first (defensive recovery after an abnormal prior shutdown — see
    /// docs/RECORDING_PIPELINE.md).
    /// </summary>
    void Start(CameraCaptureFormat format, CameraDeviceInfo? sourceDevice = null);

    /// <summary>
    /// Stops recording: flushes and finalizes the in-progress segment, then
    /// clears unpinned segment files for this camera (there is no "live" pre-event
    /// window once the camera is stopped).
    /// </summary>
    void Stop();

    /// <summary>
    /// Enqueues a newly captured frame for encoding. Never blocks the caller
    /// (typically the capture thread) — if the internal encode queue is full the
    /// frame is dropped and <see cref="DroppedFrameCount"/> is incremented.
    /// </summary>
    void AddFrame(CapturedFrame frame);

    /// <summary>
    /// Protects every segment whose <see cref="RecordedSegment.EndMonotonic"/> is
    /// at or after <paramref name="sinceMonotonic"/> from the normal stale-segment
    /// cleanup pass, until <see cref="EndEventRetention"/> is called. Only one
    /// retention hold is supported at a time (matches the single-concurrent-event
    /// policy — see docs/DECISIONS.md).
    /// </summary>
    void BeginEventRetention(TimeSpan sinceMonotonic);

    /// <summary>
    /// Releases the retention hold from <see cref="BeginEventRetention"/> and
    /// immediately runs a cleanup pass so reclaimed disk space is freed promptly
    /// rather than waiting for the next natural segment rollover.
    /// </summary>
    void EndEventRetention();

    /// <summary>Finalized segments currently on disk for this camera, oldest first.</summary>
    IReadOnlyList<RecordedSegment> GetSegmentsSnapshot();
}
