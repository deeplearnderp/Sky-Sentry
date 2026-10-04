using SkySentry.Core.Frames;
using SkySentry.Buffering;

namespace SkySentry.Core.Tests.Buffering;

/// <summary>
/// Covers <see cref="InMemoryFrameBuffer"/>'s eviction behavior using synthetic
/// frames with controlled timestamps — no real camera or hardware required.
/// </summary>
public class InMemoryFrameBufferTests
{
    private static CapturedFrame MakeFrame(long sequence, TimeSpan elapsed) => new(
        Pixels: Array.Empty<byte>(),
        Width: 1,
        Height: 1,
        Stride: 3,
        Format: FramePixelFormat.Bgr24,
        Timestamp: new FrameTimestamp(sequence, elapsed, DateTimeOffset.UnixEpoch + elapsed));

    [Fact]
    public void Add_RetainsFramesWithinRetentionWindow()
    {
        var buffer = new InMemoryFrameBuffer(TimeSpan.FromSeconds(5));

        buffer.Add(MakeFrame(0, TimeSpan.FromSeconds(0)));
        buffer.Add(MakeFrame(1, TimeSpan.FromSeconds(2)));
        buffer.Add(MakeFrame(2, TimeSpan.FromSeconds(4)));

        var snapshot = buffer.GetSnapshot();

        Assert.Equal(3, snapshot.Count);
    }

    [Fact]
    public void Add_EvictsFramesOlderThanRetentionWindow()
    {
        var buffer = new InMemoryFrameBuffer(TimeSpan.FromSeconds(5));

        buffer.Add(MakeFrame(0, TimeSpan.FromSeconds(0)));
        buffer.Add(MakeFrame(1, TimeSpan.FromSeconds(2)));
        // Relative to this frame (t=12), the retention cutoff is t=7, so both
        // earlier frames (t=0 and t=2) fall outside the 5s window and are evicted.
        buffer.Add(MakeFrame(2, TimeSpan.FromSeconds(12)));

        var snapshot = buffer.GetSnapshot();

        Assert.Single(snapshot);
        Assert.Equal(2, snapshot[0].Timestamp.SequenceNumber);
    }

    [Fact]
    public void Add_EvictsOnlyFramesOutsideWindow_KeepsFramesWithinIt()
    {
        var buffer = new InMemoryFrameBuffer(TimeSpan.FromSeconds(5));

        buffer.Add(MakeFrame(0, TimeSpan.FromSeconds(0)));
        buffer.Add(MakeFrame(1, TimeSpan.FromSeconds(4)));
        // Cutoff relative to t=6 is t=1, so frame 0 (t=0) is evicted but
        // frame 1 (t=4) remains within the window.
        buffer.Add(MakeFrame(2, TimeSpan.FromSeconds(6)));

        var snapshot = buffer.GetSnapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.DoesNotContain(snapshot, f => f.Timestamp.SequenceNumber == 0);
        Assert.Contains(snapshot, f => f.Timestamp.SequenceNumber == 1);
        Assert.Contains(snapshot, f => f.Timestamp.SequenceNumber == 2);
    }

    [Fact]
    public void Clear_RemovesAllFrames()
    {
        var buffer = new InMemoryFrameBuffer(TimeSpan.FromSeconds(30));
        buffer.Add(MakeFrame(0, TimeSpan.Zero));

        buffer.Clear();

        Assert.Empty(buffer.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_ReturnsFramesInInsertionOrder()
    {
        var buffer = new InMemoryFrameBuffer(TimeSpan.FromSeconds(30));
        buffer.Add(MakeFrame(0, TimeSpan.FromSeconds(0)));
        buffer.Add(MakeFrame(1, TimeSpan.FromSeconds(1)));
        buffer.Add(MakeFrame(2, TimeSpan.FromSeconds(2)));

        var snapshot = buffer.GetSnapshot();

        Assert.Equal([0L, 1L, 2L], snapshot.Select(f => f.Timestamp.SequenceNumber));
    }
}
