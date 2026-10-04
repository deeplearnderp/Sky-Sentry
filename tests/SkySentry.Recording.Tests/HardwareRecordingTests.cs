using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SkySentry.Capture.Discovery;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;
using Xunit.Abstractions;

namespace SkySentry.Recording.Tests;

public class HardwareRecordingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealCamera_DefaultPrePostSecondEventRestartAndCleanup_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("SKYSENTRY_RUN_RECORDING_HARDWARE_TESTS") != "1") return;
        var devices = new DirectShowCameraDiscoveryService(NullLogger<SkySentry.Capture.Discovery.DirectShowCameraDiscoveryService>.Instance).DiscoverDevices();
        foreach (var found in devices) output.WriteLine("Discovered: " + found.FriendlyName);
        var device = devices.FirstOrDefault(d => d.FriendlyName.Contains("C920", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(device);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkySentry", "validation", Guid.NewGuid().ToString("N"));
        var options = RecordingOptions.CreateDefault(Path.Combine(root, "buffer"), Path.Combine(root, "events"));
        var format = new CameraCaptureFormat(640, 480, 30);
        using var recorder = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
        using var session = new OpenCvCameraCaptureSession("Hardware Phase 2", NullLogger<OpenCvCameraCaptureSession>.Instance);
        session.FrameCaptured += (_, frame) => recorder.AddFrame(frame);
        session.Configure(device!, format);
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        recorder.Start(format);
        await session.StartAsync();
        Assert.True(await TestCameraFeeder.WaitUntilAsync(() => recorder.GetSegmentsSnapshot().LastOrDefault()?.EndMonotonic >= TimeSpan.FromSeconds(33), TimeSpan.FromSeconds(45)),
            "Camera did not fill the 30-second rolling buffer.");
        var bytesAtTrigger = Directory.GetFiles(options.RollingBufferDirectory, "seg-*.mp4", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        var events = new SegmentedEventRecorder([recorder], options, NullLogger.Instance);
        var first = await events.TriggerEventAsync();
        var result = Assert.Single(first.Cameras);
        Assert.True(result.Success, result.FailureReason);
        Assert.True(result.ActualPreEventDuration >= TimeSpan.FromSeconds(29));
        Assert.True(result.ActualPostEventDuration >= TimeSpan.FromSeconds(30));
        var metadataFile = Directory.GetFiles(options.EventsDirectory, "metadata.json", SearchOption.AllDirectories).Single();
        var videoPath = Path.Combine(Path.GetDirectoryName(metadataFile)!, result.VideoRelativePath!);
        using (var video = new VideoCapture(videoPath))
        using (var frame = new Mat())
        {
            Assert.True(video.IsOpened());
            var decoded = 0;
            while (video.Read(frame) && !frame.Empty()) decoded++;
            output.WriteLine($"First clip: {decoded} decoded frames; {decoded / video.Fps:F2}s playback.");
            Assert.True(decoded >= 30 * 50, "Playable output should cover approximately 60 seconds.");
        }
        var secondTask = events.TriggerEventAsync();
        await Task.Delay(1000);
        await events.ShutdownAsync(TimeSpan.FromSeconds(15));
        var second = await secondTask;
        Assert.True(second.Cameras[0].Success);
        Assert.NotEqual(first.EventId, second.EventId);
        await session.StopAsync();
        recorder.Stop();
        Assert.Empty(Directory.GetFiles(options.RollingBufferDirectory, "seg-*.mp4", SearchOption.AllDirectories));
        recorder.Start(format);
        await session.StartAsync();
        Assert.True(await TestCameraFeeder.WaitUntilAsync(() => recorder.GetSegmentsSnapshot().Count > 0, TimeSpan.FromSeconds(10)));
        await session.StopAsync();
        recorder.Stop();
        Assert.Empty(Directory.GetFiles(options.RollingBufferDirectory, "seg-*.mp4", SearchOption.AllDirectories));
        process.Refresh();
        var report = new
        {
            Device = device!.FriendlyName, Format = format.ToString(), EventRoot = options.EventsDirectory,
            First = first, Second = second, BufferBytesAtFirstTrigger = bytesAtTrigger,
            DroppedFramesFirstEvent = result.DroppedFrameCount, ElapsedSeconds = timer.Elapsed.TotalSeconds,
            CpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            DualPhysicalCameraRecordingVerified = false, RestartAndShutdownCleanupVerified = true
        };
        await File.WriteAllTextAsync(Path.Combine(root, "validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine("Hardware evidence: " + Path.Combine(root, "validation.json"));
        output.WriteLine($"CPU seconds: {report.CpuSeconds:F2}; peak working set: {report.PeakWorkingSetBytes / 1048576.0:F1} MiB; rolling bytes: {bytesAtTrigger}");
    }
}
