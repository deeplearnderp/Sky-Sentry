namespace SkySentry.Core.Recording;

/// <summary>
/// Describes one finalized (closed, fully written) rolling-buffer segment file on
/// disk. Segments are the unit of retention/cleanup and the unit of concatenation
/// when an event is saved — see docs/RECORDING_PIPELINE.md.
/// </summary>
/// <param name="FilePath">Absolute path to the segment's video file.</param>
/// <param name="SequenceNumber">Monotonically increasing per-camera segment counter (first segment = 0).</param>
/// <param name="StartMonotonic">Monotonic elapsed time (session-relative) of the segment's first frame.</param>
/// <param name="EndMonotonic">Monotonic elapsed time (session-relative) of the segment's last frame.</param>
/// <param name="StartWallClockUtc">Approximate UTC wall-clock time of the segment's first frame.</param>
/// <param name="EndWallClockUtc">Approximate UTC wall-clock time of the segment's last frame.</param>
/// <param name="FrameCount">Number of frames written into this segment.</param>
public sealed record RecordedSegment(
    string FilePath,
    long SequenceNumber,
    TimeSpan StartMonotonic,
    TimeSpan EndMonotonic,
    DateTimeOffset StartWallClockUtc,
    DateTimeOffset EndWallClockUtc,
    int FrameCount);
