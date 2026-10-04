using SkySentry.Core.Frames;

namespace SkySentry.Core.Buffering;

/// <summary>
/// A bounded, time-windowed rolling buffer of recently captured frames for one
/// camera. Implementations must enforce a hard bound (time window and/or frame
/// count) — this is the abstraction that keeps "continuously retain recent
/// footage" from growing memory/disk usage without limit.
///
/// Phase 1 ships a simple in-memory implementation (<c>InMemoryFrameBuffer</c> in
/// SkySentry.Buffering). Phase 2 is expected to introduce a segmented, disk-backed
/// implementation of this same interface for longer/cheaper retention — consumers
/// (recording, future event triggers) should not need to change.
/// </summary>
public interface IFrameBuffer
{
    /// <summary>Configured retention window; buffer must not retain frames older than this.</summary>
    TimeSpan RetentionWindow { get; }

    /// <summary>Adds a newly captured frame, evicting any frames that fall outside the retention window.</summary>
    void Add(CapturedFrame frame);

    /// <summary>
    /// Returns currently retained frames in capture order (oldest first). The
    /// actual span returned may be shorter than <see cref="RetentionWindow"/> if the
    /// session has not been running that long yet — callers must handle a partial
    /// window rather than treating it as an error.
    /// </summary>
    IReadOnlyList<CapturedFrame> GetSnapshot();

    /// <summary>Removes all buffered frames (e.g. on session stop/restart).</summary>
    void Clear();
}
