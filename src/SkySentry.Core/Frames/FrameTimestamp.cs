namespace SkySentry.Core.Frames;

/// <summary>
/// Timing metadata attached to every captured frame. Two clocks are recorded
/// deliberately:
/// <list type="bullet">
/// <item><description><see cref="MonotonicElapsed"/> — elapsed time since the
/// owning capture session started, from a <c>Stopwatch</c>-backed monotonic clock.
/// Always non-decreasing; this is what should be used to compare/align frames
/// between Camera 1 and Camera 2 once multi-camera correlation is implemented,
/// since it cannot be affected by wall-clock adjustments.</description></item>
/// <item><description><see cref="WallClockUtc"/> — the approximate UTC wall-clock
/// time the frame was captured, derived from the session's reference time plus
/// <see cref="MonotonicElapsed"/>. This is for human-facing display and metadata
/// only (timestamps in exported event files, UI), not for precise cross-camera
/// alignment.</description></item>
/// </list>
/// Two independent USB cameras are NOT assumed to deliver frames at exactly the
/// same wall-clock instant; this type exists so a future synchronization step can
/// reason about the actual gap between corresponding frames rather than assuming
/// it is zero.
/// </summary>
/// <param name="SequenceNumber">
/// Monotonically increasing per-session frame counter (first captured frame = 0).
/// Useful for detecting dropped frames and for log correlation.
/// </param>
/// <param name="MonotonicElapsed">Elapsed time since session start (Stopwatch-based).</param>
/// <param name="WallClockUtc">Approximate UTC capture time for display/metadata.</param>
public readonly record struct FrameTimestamp(
    long SequenceNumber,
    TimeSpan MonotonicElapsed,
    DateTimeOffset WallClockUtc);
