using SkySentry.Core.Buffering;
using SkySentry.Core.Frames;

namespace SkySentry.Buffering;

/// <summary>
/// Simplest reliable <see cref="IFrameBuffer"/> implementation: a bounded
/// in-memory queue of <see cref="CapturedFrame"/>s, evicted by elapsed monotonic
/// time once the configured retention window is exceeded.
///
/// Tradeoff (documented per the Phase 1 brief): this keeps full decoded frames in
/// RAM rather than encoding to a compact format, which bounds memory by
/// retention-window x frame-size x fps rather than by an absolute byte limit. For
/// Phase 1's proof-of-concept scope (manual "Save Event" test, modest
/// resolutions/durations) this is acceptable and keeps the implementation simple
/// and easy to reason about/test. It is expected to be replaced in Phase 2 by a
/// segmented, disk-backed implementation of the same <see cref="IFrameBuffer"/>
/// interface for cheaper/longer retention — no consumer code should need to change
/// when that happens.
/// </summary>
public sealed class InMemoryFrameBuffer(TimeSpan retentionWindow) : IFrameBuffer
{
    private readonly object _gate = new();
    private readonly Queue<CapturedFrame> _frames = new();

    public TimeSpan RetentionWindow { get; } = retentionWindow;

    public void Add(CapturedFrame frame)
    {
        lock (_gate)
        {
            _frames.Enqueue(frame);
            EvictOlderThanWindow(frame.Timestamp.MonotonicElapsed);
        }
    }

    public IReadOnlyList<CapturedFrame> GetSnapshot()
    {
        lock (_gate)
        {
            return _frames.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
        }
    }

    /// <summary>Must be called while holding <see cref="_gate"/>.</summary>
    private void EvictOlderThanWindow(TimeSpan latestElapsed)
    {
        var cutoff = latestElapsed - RetentionWindow;
        while (_frames.Count > 0 && _frames.Peek().Timestamp.MonotonicElapsed < cutoff)
        {
            _frames.Dequeue();
        }
    }
}
