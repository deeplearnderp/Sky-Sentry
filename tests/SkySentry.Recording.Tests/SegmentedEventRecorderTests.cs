using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;

namespace SkySentry.Recording.Tests;

public class SegmentedEventRecorderTests
{
    private static readonly CameraCaptureFormat Format = new(64, 64, 30);
    private static RecordingOptions Options(string root) => new(
        TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100),
        Path.Combine(root, "buffer"), Path.Combine(root, "events"));
    private static string MetadataPath(RecordingOptions options) =>
        Directory.GetFiles(options.EventsDirectory, "metadata.json", SearchOption.AllDirectories).Single();

    [Fact]
    public async Task OptionalTrackMetadataIsCapturedAndProviderFailureDoesNotBlockRecording()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root);
            var reference = new SkySentry.Core.Analysis.TrackEventReference("track-27", "device", "Camera 1",
                "Manual Save Event", TimeSpan.FromSeconds(4), 8.7, 30);
            var recorder = new SegmentedEventRecorder([], options, NullLogger.Instance, () => [reference]);
            var result = await recorder.TriggerEventAsync();
            Assert.Equal(reference, Assert.Single(result.AssociatedTracks));
            var serialized = JsonSerializer.Deserialize<SkyEventMetadata>(await File.ReadAllTextAsync(MetadataPath(options)))!;
            Assert.Equal(reference, Assert.Single(serialized.AssociatedTracks));
            var failingProvider = new SegmentedEventRecorder([], options, NullLogger.Instance, () => throw new InvalidOperationException("Analysis failed"));
            var saved = await failingProvider.TriggerEventAsync();
            Assert.Empty(saved.AssociatedTracks);
            Assert.Equal(EventLifecycleState.Idle, failingProvider.State);
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task DualIndependentRecordingSucceedsAndCorruptCameraIsIsolated()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root);
            using var first = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            using var second = new SegmentedCameraRecorder("Camera 2", options, NullLogger.Instance);
            first.Start(Format, new("device-c920", "C920", 0, []));
            second.Start(Format, new("device-pixy", "EMEET PIXY", 1, []));
            using var cts = new CancellationTokenSource();
            var feeds = Task.WhenAll(TestCameraFeeder.RunAsync(first, Format, cts.Token), TestCameraFeeder.RunAsync(second, Format, cts.Token));
            try
            {
                Assert.True(await TestCameraFeeder.WaitUntilAsync(() => first.GetSegmentsSnapshot().Count >= 3 && second.GetSegmentsSnapshot().Count >= 3, TimeSpan.FromSeconds(5)));
                var recorder = new SegmentedEventRecorder([first, second], options, NullLogger.Instance);
                var complete = await recorder.TriggerEventAsync();
                Assert.All(complete.Cameras, camera => Assert.True(camera.Success, camera.FailureReason));
                Assert.Equal("device-c920", complete.Cameras[0].DeviceId);
                Assert.Equal("EMEET PIXY", complete.Cameras[1].DeviceFriendlyName);
                var persisted = JsonSerializer.Deserialize<SkyEventMetadata>(await File.ReadAllTextAsync(Directory.GetFiles(options.EventsDirectory, "metadata.json", SearchOption.AllDirectories).Single()))!;
                Assert.Equal("device-pixy", persisted.Cameras[1].DeviceId);
                recorder.StateChanged += (_, state) =>
                {
                    if (state == EventLifecycleState.Finalizing)
                        foreach (var segment in second.GetSegmentsSnapshot()) File.WriteAllText(segment.FilePath, "corrupt video");
                };
                var partial = await recorder.TriggerEventAsync();
                Assert.True(partial.Cameras[0].Success);
                Assert.False(partial.Cameras[1].Success);
                Assert.NotNull(partial.Cameras[1].FailureReason);
            }
            finally { cts.Cancel(); await feeds; }
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task OneCamera_EventHasPreAndPostVideoMetadataThumbnailAndLifecycle()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root);
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            camera.Start(Format);
            using var feedCts = new CancellationTokenSource();
            var feed = TestCameraFeeder.RunAsync(camera, Format, feedCts.Token);
            try
            {
                Assert.True(await TestCameraFeeder.WaitUntilAsync(() => camera.GetSegmentsSnapshot().Count >= 4, TimeSpan.FromSeconds(5)));
                var recorder = new SegmentedEventRecorder([camera], options, NullLogger.Instance);
                var states = new List<EventLifecycleState>();
                recorder.StateChanged += (_, state) => states.Add(state);
                var metadata = await recorder.TriggerEventAsync();
                var result = Assert.Single(metadata.Cameras);
                Assert.True(result.Success, result.FailureReason);
                Assert.True(result.ActualPreEventDuration > TimeSpan.Zero);
                Assert.True(result.ActualPostEventDuration >= options.PostEventDuration);
                var path = MetadataPath(options);
                var roundtrip = JsonSerializer.Deserialize<SkyEventMetadata>(await File.ReadAllTextAsync(path))!;
                Assert.Equal(metadata.EventId, roundtrip.EventId);
                Assert.Equal(result.ActualPreEventDuration, roundtrip.Cameras[0].ActualPreEventDuration);
                var directory = Path.GetDirectoryName(path)!;
                Assert.True(File.Exists(Path.Combine(directory, result.ThumbnailRelativePath!)));
                using var video = new VideoCapture(Path.Combine(directory, result.VideoRelativePath!));
                Assert.True(video.IsOpened());
                var captureSpan = (result.RecordingEndMonotonic - result.RecordingStartMonotonic).TotalSeconds;
                Assert.InRange(video.FrameCount / video.Fps, captureSpan, captureSpan + 0.1);
                Assert.InRange(Math.Abs(video.Fps - result.PlaybackFrameRate), 0, 0.05);
                using var frame = new Mat();
                Assert.True(video.Read(frame) && !frame.Empty());
                Assert.Equal(new[] { EventLifecycleState.Triggered, EventLifecycleState.CapturingPostEvent,
                    EventLifecycleState.Finalizing, EventLifecycleState.Completed, EventLifecycleState.Idle }, states);
                // A second event must not reuse the first event's state or output folder.
                var second = await recorder.TriggerEventAsync();
                Assert.NotEqual(metadata.EventId, second.EventId);
                Assert.True(second.Cameras[0].Success);
            }
            finally { feedCts.Cancel(); await feed; }
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task OverlapIsRejectedAndShutdownFlushesCurrentSegment()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root) with { PostEventDuration = TimeSpan.FromSeconds(30) };
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            camera.Start(Format);
            TestCameraFeeder.FeedBurst(camera, Format, 5, TimeSpan.FromMilliseconds(20));
            await camera.FlushAsync();
            // These frames stay in the current, not yet closed segment.
            TestCameraFeeder.FeedBurst(camera, Format, 3, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100));
            var recorder = new SegmentedEventRecorder([camera], options, NullLogger.Instance);
            var active = recorder.TriggerEventAsync();
            await Assert.ThrowsAsync<EventAlreadyInProgressException>(() => recorder.TriggerEventAsync());
            await recorder.ShutdownAsync(TimeSpan.FromSeconds(5));
            var metadata = await active;
            Assert.True(metadata.Cameras[0].Success);
            Assert.Equal(DateTimeOffset.UnixEpoch.Offset, metadata.TriggeredAtUtc.Offset);
            Assert.True(metadata.Cameras[0].RecordingEndUtc >= metadata.Cameras[0].RecordingStartUtc);
            Assert.Equal(EventLifecycleState.Idle, recorder.State);
            camera.Stop();
            Assert.Empty(Directory.GetFiles(options.RollingBufferDirectory, "seg-*.mp4", SearchOption.AllDirectories));
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task FailedCameraDoesNotPreventOtherCameraSuccess()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root);
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            using var failed = new SegmentedCameraRecorder("Camera 2", options, NullLogger.Instance);
            camera.Start(Format);
            using var cts = new CancellationTokenSource();
            var feed = TestCameraFeeder.RunAsync(camera, Format, cts.Token);
            try
            {
                Assert.True(await TestCameraFeeder.WaitUntilAsync(() => camera.GetSegmentsSnapshot().Count >= 2, TimeSpan.FromSeconds(5)));
                var recorder = new SegmentedEventRecorder([camera, failed], options, NullLogger.Instance);
                var metadata = await recorder.TriggerEventAsync();
                Assert.True(metadata.Cameras[0].Success);
                Assert.False(metadata.Cameras[1].Success);
                Assert.NotNull(metadata.Cameras[1].FailureReason);
            }
            finally { cts.Cancel(); await feed; }
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task NoActiveCameraWritesFailureMetadataAndReleasesEventLock()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root);
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            var recorder = new SegmentedEventRecorder([camera], options, NullLogger.Instance);
            var states = new List<EventLifecycleState>();
            recorder.StateChanged += (_, state) => states.Add(state);
            var metadata = await recorder.TriggerEventAsync();
            Assert.False(metadata.Cameras[0].Success);
            Assert.Contains(EventLifecycleState.Failed, states);
            Assert.True(File.Exists(MetadataPath(options)));
            Assert.Equal(EventLifecycleState.Idle, recorder.State);
            Assert.False((await recorder.TriggerEventAsync()).Cameras[0].Success);
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }

    [Fact]
    public async Task StoppedCameraRetainsPinnedFootageUntilEventCompletes()
    {
        var root = TestCameraFeeder.CreateTempDir();
        try
        {
            var options = Options(root) with { PostEventDuration = TimeSpan.FromSeconds(30) };
            using var camera = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
            camera.Start(Format);
            TestCameraFeeder.FeedBurst(camera, Format, 12, TimeSpan.FromMilliseconds(20));
            var recorder = new SegmentedEventRecorder([camera], options, NullLogger.Instance);
            var task = recorder.TriggerEventAsync();
            camera.Stop();
            Assert.NotEmpty(camera.GetSegmentsSnapshot());
            Assert.Throws<InvalidOperationException>(() => camera.Start(Format));
            var metadata = await task;
            Assert.True(metadata.Cameras[0].Success);
            Assert.Empty(camera.GetSegmentsSnapshot());
            camera.Start(Format);
            Assert.True(camera.IsRunning);
        }
        finally { TestCameraFeeder.CleanupTempDir(root); }
    }
}
