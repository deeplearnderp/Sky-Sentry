using SkySentry.Core.Frames;
using System.Threading.Channels;

namespace SkySentry.Core.Analysis;

public sealed class LatestAnalysisQueue : IDisposable
{
    private readonly Channel<CapturedFrame> _channel;
    private long _dropped;
    public LatestAnalysisQueue()
    {
        _channel = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true },
            _ => Interlocked.Increment(ref _dropped));
    }
    public long Dropped => Interlocked.Read(ref _dropped);
    public bool Submit(CapturedFrame frame) => _channel.Writer.TryWrite(frame);
    public async ValueTask<CapturedFrame?> TakeAsync(CancellationToken token)
    {
        while (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            if (_channel.Reader.TryRead(out var frame)) return frame;
        return null;
    }
    public void Dispose() => _channel.Writer.TryComplete();
}
