using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkySentry.Core.Cameras;
using SkySentry.Core.Frames;
using SkySentry.Core.Recording;

namespace SkySentry.Recording.Segments;

/// <summary>One bounded encode queue and an exclusively owned rolling directory per camera.</summary>
public sealed class SegmentedCameraRecorder : ISegmentedCameraRecorder
{
    private sealed record Work(CapturedFrame? Frame, TaskCompletionSource? Flush);
    private readonly RecordingOptions _options;
    private readonly ILogger _logger;
    private readonly string _bufferDirectory;
    private readonly object _gate = new();
    private readonly List<RecordedSegment> _segments = new();
    private Channel<Work>? _channel;
    private Task? _encodeTask;
    private FileStream? _ownership;
    private TimeSpan? _eventRetentionSince;
    private TimeSpan? _eventRetentionUntil;
    private TimeSpan _latest;
    private TimeSpan? _firstWritten;
    private TimeSpan _lastWritten;
    private long _written;
    private long _dropped;
    private long _sequence;
    private long _queuedBytes;
    private bool _disposed;
    public SegmentedCameraRecorder(string cameraLabel, RecordingOptions options, ILogger logger)
    {
        options.Validate();
        CameraLabel = cameraLabel;
        _options = options;
        _logger = logger;
        var label = new string(cameraLabel.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Replace(' ', '_');
        if (string.IsNullOrEmpty(label) || label is "." or "..") throw new ArgumentException("Invalid camera label.");
        _bufferDirectory = Path.Combine(Path.GetFullPath(options.RollingBufferDirectory), ".skysentry-segments", label);
    }
    public string CameraLabel { get; }
    public CameraDeviceInfo? SourceDevice { get; private set; }
    public bool IsRunning { get { lock (_gate) return _channel is not null; } }
    public CameraCaptureFormat? ActiveFormat { get; private set; }
    public string? FailureReason { get; private set; }
    public long DroppedFrameCount => Interlocked.Read(ref _dropped);
    public double AchievedFps
    {
        get { lock (_gate) return _firstWritten is { } first && _lastWritten > first ? (_written - 1) / (_lastWritten - first).TotalSeconds : 0; }
    }
    public void Start(CameraCaptureFormat format, CameraDeviceInfo? sourceDevice = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_channel is not null) return;
            if (_eventRetentionSince is not null) throw new InvalidOperationException("Wait for the active event before restarting this camera.");
            if (_encodeTask is not null) throw new InvalidOperationException("Stop the previous recording session before restarting.");
            if (format.Width <= 0 || format.Height <= 0 || format.FrameRate <= 0 ||
                (long)format.Width * format.Height * 3 > _options.MaxQueuedFrameBytes)
                throw new ArgumentException("Capture format is invalid or exceeds the frame queue byte budget.");
            Directory.CreateDirectory(_bufferDirectory);
            // Exclusive file ownership prevents two app instances from deleting each other's live footage.
            _ownership = new FileStream(Path.Combine(_bufferDirectory, ".owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                CleanupFiles();
                _segments.Clear();
                _latest = _lastWritten = TimeSpan.Zero;
                _firstWritten = null;
                _written = _dropped = _sequence = _queuedBytes = 0;
                ActiveFormat = format;
                SourceDevice = sourceDevice;
                FailureReason = null;
                var channel = Channel.CreateBounded<Work>(new BoundedChannelOptions(8)
                { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
                _channel = channel;
                _encodeTask = Task.Factory.StartNew(() => Encode(channel, format), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch { _ownership.Dispose(); _ownership = null; throw; }
        }
        _logger.LogInformation("{Camera}: segmented recording started ({Format})", CameraLabel, format);
    }
    public void AddFrame(CapturedFrame frame)
    {
        lock (_gate)
        {
            if (_channel is null) return;
            if (ActiveFormat is not { } format || frame.Width != format.Width || frame.Height != format.Height ||
                frame.Timestamp.MonotonicElapsed < _latest || frame.Pixels.LongLength > _options.MaxQueuedFrameBytes)
            {
                Interlocked.Increment(ref _dropped);
                FailureReason = "Capture frame format or timestamp changed; restart the camera.";
                return;
            }
            _latest = frame.Timestamp.MonotonicElapsed;
            if (_queuedBytes + frame.Pixels.LongLength > _options.MaxQueuedFrameBytes ||
                !_channel.Writer.TryWrite(new Work(frame, null)))
            { Interlocked.Increment(ref _dropped); return; }
            _queuedBytes += frame.Pixels.LongLength;
        }
    }
    public TimeSpan CaptureTriggerPoint(TimeSpan preEventDuration, TimeSpan? postEventDuration = null)
    {
        lock (_gate)
        {
            var since = _latest - preEventDuration;
            _eventRetentionSince = since < TimeSpan.Zero ? TimeSpan.Zero : since;
            _eventRetentionUntil = postEventDuration is { } post ? _latest + post : null;
            return _latest;
        }
    }
    public void BeginEventRetention(TimeSpan sinceMonotonic) { lock (_gate) { _eventRetentionSince = sinceMonotonic; _eventRetentionUntil = null; } }
    public void EndEventRetention()
    {
        lock (_gate)
        {
            _eventRetentionSince = null;
            _eventRetentionUntil = null;
            if (_channel is null && _encodeTask is null) CleanupStopped();
            else Prune();
        }
    }
    public IReadOnlyList<RecordedSegment> GetSegmentsSnapshot() { lock (_gate) return _segments.ToArray(); }
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Channel<Work>? channel;
        Task? encode;
        lock (_gate) { channel = _channel; encode = _encodeTask; }
        if (channel is null) { if (encode is not null) await encode.WaitAsync(cancellationToken).ConfigureAwait(false); return; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await channel.Writer.WriteAsync(new Work(null, done), cancellationToken).ConfigureAwait(false);
            await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException) { if (encode is not null) await encode.WaitAsync(cancellationToken).ConfigureAwait(false); }
    }
    public void Stop()
    {
        Task? task;
        lock (_gate)
        {
            _channel?.Writer.TryComplete();
            _channel = null;
            task = _encodeTask;
        }
        task?.GetAwaiter().GetResult();
        lock (_gate)
        {
            _encodeTask = null;
            if (_eventRetentionSince is null) CleanupStopped();
        }
    }
    private void Encode(Channel<Work> channel, CameraCaptureFormat format)
    {
        VideoWriter? writer = null;
        string? path = null;
        long sequence = 0;
        int count = 0;
        TimeSpan start = default, end = default;
        DateTimeOffset startUtc = default, endUtc = default;
        void CloseSegment()
        {
            if (writer is null) return;
            writer.Release();
            writer.Dispose();
            writer = null;
            if (count > 0 && path is not null)
            {
                lock (_gate)
                {
                    _segments.Add(new RecordedSegment(path, sequence, start, end, startUtc, endUtc, count));
                    Prune();
                    if (DiskBytes() > _options.MaxBufferBytesPerCamera)
                    {
                        File.Delete(path);
                        _segments.RemoveAll(s => s.FilePath == path);
                        throw new IOException("Rolling buffer disk budget exhausted.");
                    }
                }
            }
            path = null;
        }
        try
        {
            while (channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (channel.Reader.TryRead(out var work))
                {
                    if (work.Flush is { } flush)
                    {
                        try { CloseSegment(); flush.TrySetResult(); }
                        catch (Exception ex) { flush.TrySetException(ex); throw; }
                        continue;
                    }
                    var frame = work.Frame!;
                    lock (_gate) _queuedBytes -= frame.Pixels.LongLength;
                    // Stop accepting new encoded bytes before the budget is reached; reserve
                    // 8 MiB for a final encoded frame and the MP4 trailer.
                    if (DiskBytes() >= _options.MaxBufferBytesPerCamera - 8 * 1024 * 1024)
                        throw new IOException("Rolling buffer disk budget exhausted.");
                    if (writer is null)
                    {
                        sequence = _sequence++;
                        path = Path.Combine(_bufferDirectory, $"seg-{sequence:D12}.mp4");
                        writer = new VideoWriter(path, FourCC.FromFourChars('m', 'p', '4', 'v'),
                            format.FrameRate, new Size(format.Width, format.Height));
                        if (!writer.IsOpened()) throw new IOException($"Unable to open mp4v segment writer at {path} ({path.Length} path characters). Check output path length and codec availability.");
                        count = 0;
                        start = frame.Timestamp.MonotonicElapsed;
                        startUtc = frame.Timestamp.WallClockUtc;
                    }
                    using (var mat = CapturedFrameMatConverter.ToMat(frame)) writer.Write(mat);
                    count++;
                    end = frame.Timestamp.MonotonicElapsed;
                    endUtc = frame.Timestamp.WallClockUtc;
                    lock (_gate) { _firstWritten ??= end; _lastWritten = end; _written++; }
                    if (end - start >= _options.SegmentDuration) CloseSegment();
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate) { FailureReason = ex.Message; _channel = null; }
            channel.Writer.TryComplete(ex);
            _logger.LogError(ex, "{Camera}: recording failed", CameraLabel);
        }
        finally
        {
            try { CloseSegment(); }
            catch (Exception ex) { FailureReason = ex.Message; _logger.LogError(ex, "{Camera}: segment close failed", CameraLabel); }
            writer?.Dispose();
            while (channel.Reader.TryRead(out var pending)) pending.Flush?.TrySetException(new IOException(FailureReason ?? "Recorder stopped."));
            lock (_gate) { _queuedBytes = 0; _channel = null; }
        }
    }
    private long DiskBytes() => Directory.EnumerateFiles(_bufferDirectory, "seg-*.mp4").Sum(p => new FileInfo(p).Length);
    // Called under _gate: snapshot/pinning cannot race removal.
    private void Prune()
    {
        if (_segments.Count == 0) return;
        var floor = EventWindowMath.CalculateRetentionFloor(_segments[^1].EndMonotonic, _options.PreEventDuration, _options.SegmentDuration);

        // Keep the requested event window and the latest rolling window, not all
        // footage between them during a slow finalization.
        bool IsEventSegment(RecordedSegment segment) => _eventRetentionSince is { } since && segment.EndMonotonic >= since &&
            (_eventRetentionUntil is null || segment.StartMonotonic <= _eventRetentionUntil.Value);
        foreach (var segment in _segments.Where(s => s.EndMonotonic < floor && !IsEventSegment(s)).ToArray())
        {
            try { File.Delete(segment.FilePath); _segments.Remove(segment); }
            catch (IOException ex) { _logger.LogWarning(ex, "Will retry deleting {Path}", segment.FilePath); }
        }
    }
    private void CleanupFiles()
    {
        // Delete only our reserved segment filenames, never arbitrary contents or directories.
        foreach (var path in Directory.EnumerateFiles(_bufferDirectory, "seg-*.mp4")) File.Delete(path);
    }
    private void CleanupStopped()
    {
        if (_ownership is null) return;
        try { CleanupFiles(); _segments.Clear(); }
        finally { _ownership?.Dispose(); _ownership = null; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
