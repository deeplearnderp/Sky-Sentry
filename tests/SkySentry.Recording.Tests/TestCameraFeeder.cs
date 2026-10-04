using SkySentry.Core.Cameras;
using SkySentry.Core.Recording;

namespace SkySentry.Recording.Tests;

/// <summary>
/// Streams synthetic frames into an <see cref="ISegmentedCameraRecorder"/>,
/// advancing the frame's monotonic timestamp roughly in lockstep with real
/// elapsed time. This lets tests exercise real-time-dependent behavior (segment
/// rollover, post-event wait polling) without needing a real camera, while still
/// behaving like a continuously running capture session.
/// </summary>
internal static class TestCameraFeeder
{
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(20);

    public static async Task RunAsync(ISegmentedCameraRecorder camera, CameraCaptureFormat format, CancellationToken cancellationToken)
    {
        long sequence = 0;
        var monotonic = TimeSpan.Zero;
        var wallClockStart = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = SyntheticFrameFactory.Create(format.Width, format.Height, sequence, monotonic, wallClockStart + monotonic);
                camera.AddFrame(frame);
                sequence++;
                monotonic += StepInterval;
                await Task.Delay(StepInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the test stops the feed.
        }
    }

    /// <summary>Feeds a fixed number of frames back-to-back with no real-time delay, for fast deterministic tests.</summary>
    public static void FeedBurst(ISegmentedCameraRecorder camera, CameraCaptureFormat format, int frameCount, TimeSpan stepMonotonic, TimeSpan? startMonotonic = null)
    {
        var monotonic = startMonotonic ?? TimeSpan.Zero;
        var wallClockStart = DateTimeOffset.UtcNow;

        for (var i = 0; i < frameCount; i++)
        {
            var frame = SyntheticFrameFactory.Create(format.Width, format.Height, i, monotonic, wallClockStart + monotonic);
            camera.AddFrame(frame);
            Thread.Sleep(10);
            monotonic += stepMonotonic;
        }
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    public static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "SkySentryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void CleanupTempDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; files may still be briefly locked by a disposing VideoWriter.
        }
    }
}
