using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Core.Cameras;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;

namespace SkySentry.Recording.Tests;

public class SegmentedCameraRecorderTests
{
    private static readonly CameraCaptureFormat Format = new(64, 64, 30);

    private static RecordingOptions Options(string bufferRoot, TimeSpan preEvent, TimeSpan segmentDuration) =>
        new(
            PreEventDuration: preEvent,
            PostEventDuration: TimeSpan.FromSeconds(30),
            SegmentDuration: segmentDuration,
            RollingBufferDirectory: bufferRoot,
            EventsDirectory: Path.Combine(bufferRoot, "..", "events"));

    [Fact]
    public async Task EventRetentionKeepsRequestedWindowWithoutPinningLaterFootage()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);
            TestCameraFeeder.FeedBurst(recorder, Format, 22, TimeSpan.FromMilliseconds(100));
            await recorder.FlushAsync();
            var trigger = recorder.CaptureTriggerPoint(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            TestCameraFeeder.FeedBurst(recorder, Format, 20, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2.2));
            await recorder.FlushAsync();
            var pinned = recorder.GetSegmentsSnapshot().Where(s => s.EndMonotonic >= trigger - TimeSpan.FromSeconds(1) && s.StartMonotonic <= trigger + TimeSpan.FromSeconds(1)).ToArray();
            Assert.NotEmpty(pinned);
            TestCameraFeeder.FeedBurst(recorder, Format, 60, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(4.2));
            await recorder.FlushAsync();
            var kept = recorder.GetSegmentsSnapshot();
            Assert.All(pinned, segment => Assert.True(File.Exists(segment.FilePath)));
            Assert.DoesNotContain(kept, segment => segment.StartMonotonic > trigger + TimeSpan.FromSeconds(1) && segment.EndMonotonic < TimeSpan.FromSeconds(8));
            Assert.Contains(kept, segment => segment.StartMonotonic >= TimeSpan.FromSeconds(9));
            Assert.True(recorder.IsRunning, recorder.FailureReason);
            recorder.EndEventRetention();
            Assert.All(pinned, segment => Assert.False(File.Exists(segment.FilePath)));
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }
    [Fact]
    public async Task SegmentRollover_ProducesMultipleSegmentFiles()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(tempRoot, preEvent: TimeSpan.FromSeconds(10), segmentDuration: TimeSpan.FromMilliseconds(200));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);

            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 40, stepMonotonic: TimeSpan.FromMilliseconds(50));

            var reached = await TestCameraFeeder.WaitUntilAsync(() => recorder.GetSegmentsSnapshot().Count >= 8, TimeSpan.FromSeconds(5));
            Assert.True(reached, $"Expected at least 8 segments; got {recorder.GetSegmentsSnapshot().Count}");

            var segments = recorder.GetSegmentsSnapshot();
            Assert.True(segments.Count >= 8);

            for (var i = 1; i < segments.Count; i++)
            {
                Assert.True(segments[i].SequenceNumber > segments[i - 1].SequenceNumber);
                Assert.True(segments[i].StartMonotonic >= segments[i - 1].EndMonotonic);
            }

            foreach (var segment in segments)
            {
                Assert.True(File.Exists(segment.FilePath));
            }
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }

    [Fact]
    public async Task StaleSegments_ArePrunedOnceBeyondRetentionFloor()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(tempRoot, preEvent: TimeSpan.FromMilliseconds(600), segmentDuration: TimeSpan.FromMilliseconds(200));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);

            // 60 frames * 50ms = 3s of monotonic footage, far more than the
            // ~800ms retention floor (pre-event + one segment margin), so older
            // segments must be pruned automatically.
            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 60, stepMonotonic: TimeSpan.FromMilliseconds(50));

            var reachedSteadyState = await TestCameraFeeder.WaitUntilAsync(
                () => recorder.GetSegmentsSnapshot().Count > 0 && recorder.GetSegmentsSnapshot()[^1].EndMonotonic >= TimeSpan.FromMilliseconds(2700) && recorder.GetSegmentsSnapshot().Count <= 6,
                TimeSpan.FromSeconds(5));
            Assert.True(reachedSteadyState);

            var segments = recorder.GetSegmentsSnapshot();
            Assert.True(segments.Count <= 6, $"Expected pruning to bound segment count; got {segments.Count}");

            var earliestSegmentFile = Path.Combine(options.RollingBufferDirectory, ".skysentry-segments", "Camera_1", "seg-000000000000.mp4");
            Assert.False(File.Exists(earliestSegmentFile));
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }

    [Fact]
    public async Task EventRetention_ProtectsSegmentsUntilReleased()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(tempRoot, preEvent: TimeSpan.FromMilliseconds(400), segmentDuration: TimeSpan.FromMilliseconds(200));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);

            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 10, stepMonotonic: TimeSpan.FromMilliseconds(50));
            var filledInitial = await TestCameraFeeder.WaitUntilAsync(() => recorder.GetSegmentsSnapshot().Count >= 2, TimeSpan.FromSeconds(5));
            Assert.True(filledInitial);

            var earliestSegmentPath = recorder.GetSegmentsSnapshot()[0].FilePath;
            Assert.True(File.Exists(earliestSegmentPath));

            recorder.BeginEventRetention(TimeSpan.Zero);

            // Feed far more footage than the retention floor would normally allow,
            // to prove the held segment survives the cleanup passes that run as
            // part of every subsequent segment rollover.
            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 60, stepMonotonic: TimeSpan.FromMilliseconds(50), startMonotonic: TimeSpan.FromMilliseconds(500));
            await TestCameraFeeder.WaitUntilAsync(() => recorder.GetSegmentsSnapshot().Count >= 10, TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(earliestSegmentPath), "Segment under an active retention hold must not be pruned.");

            recorder.EndEventRetention();

            var pruned = await TestCameraFeeder.WaitUntilAsync(() => !File.Exists(earliestSegmentPath), TimeSpan.FromSeconds(5));
            Assert.True(pruned, "Releasing the retention hold must trigger an immediate cleanup pass.");
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }

    [Fact]
    public void Start_ClearsStaleLeftoverFilesFromAbnormalPriorShutdown()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var cameraDir = Path.Combine(tempRoot, ".skysentry-segments", "Camera_1");
            Directory.CreateDirectory(cameraDir);
            var staleFile = Path.Combine(cameraDir, "seg-000000000099.mp4");
            File.WriteAllText(staleFile, "stale leftover from a crash");

            // A sibling directory outside this camera's own buffer subdirectory
            // must never be touched by Start().
            var unrelatedDir = Path.Combine(tempRoot, "..", "unrelated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unrelatedDir);
            var unrelatedFile = Path.Combine(unrelatedDir, "do-not-touch.txt");
            File.WriteAllText(unrelatedFile, "must survive");

            try
            {
                var options = Options(tempRoot, preEvent: TimeSpan.FromSeconds(10), segmentDuration: TimeSpan.FromMilliseconds(200));
                using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);

                recorder.Start(Format);

                Assert.False(File.Exists(staleFile), "Start() must clear stale segment files left from an abnormal prior shutdown.");
                Assert.True(File.Exists(unrelatedFile), "Start() must never touch directories outside its own camera buffer subdirectory.");

                recorder.Stop();
            }
            finally
            {
                TestCameraFeeder.CleanupTempDir(unrelatedDir);
            }
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }

    [Fact]
    public async Task AchievedFps_ReflectsWrittenFrameRate()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(tempRoot, preEvent: TimeSpan.FromSeconds(10), segmentDuration: TimeSpan.FromSeconds(5));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);

            // 10 frames spaced 100ms apart => roughly 10fps achieved.
            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 10, stepMonotonic: TimeSpan.FromMilliseconds(100));

            var reached = await TestCameraFeeder.WaitUntilAsync(() => recorder.AchievedFps > 0, TimeSpan.FromSeconds(5));
            Assert.True(reached);

            Assert.InRange(recorder.AchievedFps, 5.0, 20.0);
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }

    [Fact]
    public void Stop_ClearsSegmentFilesForThisCameraOnly()
    {
        var tempRoot = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(tempRoot, preEvent: TimeSpan.FromSeconds(10), segmentDuration: TimeSpan.FromMilliseconds(200));
            using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            recorder.Start(Format);
            TestCameraFeeder.FeedBurst(recorder, Format, frameCount: 10, stepMonotonic: TimeSpan.FromMilliseconds(50));

            recorder.Stop();

            Assert.False(recorder.IsRunning);
            Assert.Empty(recorder.GetSegmentsSnapshot());
        }
        finally
        {
            TestCameraFeeder.CleanupTempDir(tempRoot);
        }
    }
}
