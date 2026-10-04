using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Core.Cameras;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;

namespace SkySentry.Recording.Tests;

public class RecordingBudgetTests
{
    [Fact]
    public void QueueDropsOverBudgetWithoutBlockingAndTriggerUsesCurrentFrame()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = RecordingOptions.CreateDefault(Path.Combine(root, "buffer"), Path.Combine(root, "events"))
                with { MaxQueuedFrameBytes = 64 * 64 * 3 };
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            camera.Start(new CameraCaptureFormat(64, 64, 30));
            for (var i = 0; i < 1000; i++)
                camera.AddFrame(SyntheticFrameFactory.Create(64, 64, i, TimeSpan.FromMilliseconds(i), DateTimeOffset.UtcNow));
            Assert.True(camera.DroppedFrameCount > 0);
            Assert.Equal(TimeSpan.FromMilliseconds(999), camera.CaptureTriggerPoint(options.PreEventDuration));
            camera.Stop();
            camera.EndEventRetention();
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task DiskBudgetStopsRecordingRatherThanGrowingProtectedBuffer()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = RecordingOptions.CreateDefault(Path.Combine(root, "buffer"), Path.Combine(root, "events"))
                with { MaxBufferBytesPerCamera = 16 * 1024 * 1024 };
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            camera.Start(new CameraCaptureFormat(64, 64, 30));
            // Simulate on-disk pressure without encoding hours of footage.
            var reserved = Path.Combine(options.RollingBufferDirectory, ".skysentry-segments", "Camera_1");
            using (var file = File.Create(Path.Combine(reserved, "seg-budget.mp4"))) file.SetLength(9 * 1024 * 1024);
            camera.AddFrame(SyntheticFrameFactory.Create(64, 64, 0, TimeSpan.Zero, DateTimeOffset.UtcNow));
            Assert.True(await TestCameraFeeder.WaitUntilAsync(() => !camera.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.Contains("budget", camera.FailureReason!);
            Assert.True(Directory.GetFiles(reserved, "seg-*.mp4").Sum(p => new FileInfo(p).Length) <= options.MaxBufferBytesPerCamera);
            camera.Stop();
            Assert.Empty(Directory.GetFiles(reserved, "seg-*.mp4"));
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public void SecondInstanceCannotDeleteLiveSegmentsAndUnrelatedFilesSurviveRecovery()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = RecordingOptions.CreateDefault(Path.Combine(root, "buffer"), Path.Combine(root, "events"));
            using var first = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            using var second = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            first.Start(new CameraCaptureFormat(64, 64, 30));
            var reserved = Path.Combine(options.RollingBufferDirectory, ".skysentry-segments", "Camera_1");
            var unrelated = Path.Combine(reserved, "keep.txt");
            File.WriteAllText(unrelated, "not a recording segment");
            Assert.Throws<IOException>(() => second.Start(new CameraCaptureFormat(64, 64, 30)));
            first.Stop();
            second.Start(new CameraCaptureFormat(64, 64, 30));
            Assert.True(File.Exists(unrelated));
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }
}
