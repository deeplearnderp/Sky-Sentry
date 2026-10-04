using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SkySentry.Analysis;
using SkySentry.Capture.Discovery;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Analysis;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;
using Xunit.Abstractions;

namespace SkySentry.Analysis.Tests;

public class HardwareAnalysisTests(ITestOutputHelper output)
{
    [Fact]
    public async Task C920_AnalysisAndRecordingTogether_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("SKYSENTRY_RUN_ANALYSIS_HARDWARE_TESTS") != "1") return;
        var device = new DirectShowCameraDiscoveryService(NullLogger<DirectShowCameraDiscoveryService>.Instance).DiscoverDevices()
            .FirstOrDefault(d => d.FriendlyName.Contains("C920", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(device);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkySentry", "validation-phase3", Guid.NewGuid().ToString("N"));
        var options = RecordingOptions.CreateDefault(Path.Combine(root, "buffer"), Path.Combine(root, "events"));
        using var recording = new SegmentedCameraRecorder("Camera 1", options, NullLogger.Instance);
        using var analysis = new CameraAnalysisWorker(device!.DeviceId, "Camera 1", new(), null, NullLogger.Instance);
        using var session = new OpenCvCameraCaptureSession("Phase 3 hardware", NullLogger<OpenCvCameraCaptureSession>.Instance);
        var format = new CameraCaptureFormat(640, 480, 30);
        long captured = 0; var enabled = false;
        var maxTracks = 0; var trackIds = new HashSet<string>(); var statuses = new HashSet<string>();
        session.FrameCaptured += (_, frame) =>
        {
            recording.AddFrame(frame); Interlocked.Increment(ref captured);
            if (Volatile.Read(ref enabled)) analysis.Submit(frame);
        };
        session.Configure(device, format);
        recording.Start(format); await session.StartAsync();
        await Task.Delay(5000); // Exclude device startup from the comparison.
        var baselineStartFrames = Interlocked.Read(ref captured);
        var timer = Stopwatch.StartNew();
        await Task.Delay(5000);
        var baselineFrames = Interlocked.Read(ref captured);
        var baselineFps = (baselineFrames - baselineStartFrames) / timer.Elapsed.TotalSeconds;
        Volatile.Write(ref enabled, true);
        using var process = Process.GetCurrentProcess();
        var cpuAtStart = process.TotalProcessorTime;
        var analysisStart = timer.Elapsed;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (analysis.Latest is { } current)
            {
                maxTracks = Math.Max(maxTracks, current.Tracks.Count);
                foreach (var track in current.Tracks) trackIds.Add(track.TrackId);
                statuses.Add(current.Status);
            }
        }
        var events = new SegmentedEventRecorder([recording], options, NullLogger.Instance,
            () => (analysis.Latest?.Tracks ?? []).Select(t => new TrackEventReference(t.TrackId, t.CameraId, t.CameraLabel,
                "Manual Save Event", t.Duration, t.PeakAngularSpeed, t.Measurement?.DirectionChangeDegrees)).ToArray());
        var saved = await events.TriggerEventAsync();
        Assert.True(saved.Cameras[0].Success, saved.Cameras[0].FailureReason);
        Assert.True(saved.Cameras[0].ActualPreEventDuration >= TimeSpan.FromSeconds(29));
        Assert.True(saved.Cameras[0].ActualPostEventDuration >= TimeSpan.FromSeconds(30));
        var elapsed = timer.Elapsed - analysisStart;
        var analysisCaptureFps = (Interlocked.Read(ref captured) - baselineFrames) / elapsed.TotalSeconds;
        var cpu = (process.TotalProcessorTime - cpuAtStart).TotalSeconds;
        var latest = analysis.Latest!;
        Assert.NotNull(latest);
        Assert.DoesNotContain("error", latest.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(latest.AnalysisFps > 1);
        var metadataPath = Directory.GetFiles(options.EventsDirectory, "metadata.json", SearchOption.AllDirectories).Single();
        var decoded = 0;
        using (var video = new VideoCapture(Path.Combine(Path.GetDirectoryName(metadataPath)!, saved.Cameras[0].VideoRelativePath!)))
        using (var frame = new Mat())
        { Assert.True(video.IsOpened()); while (video.Read(frame) && !frame.Empty()) decoded++; }
        Assert.True(decoded > 1500);
        await session.StopAsync();
        recording.Stop();
        analysis.Dispose();
        Assert.Empty(Directory.GetFiles(options.RollingBufferDirectory, "seg-*.mp4", SearchOption.AllDirectories));
        process.Refresh();
        var report = new { Device = device.FriendlyName, Format = format.ToString(), BaselineCaptureFps = baselineFps,
            AnalysisEnabledCaptureFps = analysisCaptureFps, AnalysisFps = latest.AnalysisFps, CpuSeconds = cpu,
            AnalysisElapsedSeconds = elapsed.TotalSeconds, PeakWorkingSetBytes = process.PeakWorkingSet64,
            DroppedAnalysisFrames = latest.DroppedFrames, DroppedRecordingFrames = saved.Cameras[0].DroppedFrameCount,
            MaximumConcurrentTracks = maxTracks, DistinctTrackIds = trackIds.Count, SuppressedGlobalFrames = latest.SuppressedFrames,
            ObservedStatuses = statuses.ToArray(), DecodedEventFrames = decoded, Event = saved,
            HumanControlledObjectMotionVerified = false, DualPhysicalCameraVerified = false, CalibrationVerified = false };
        var reportPath = Path.Combine(root, "validation.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(JsonSerializer.Serialize(report));
        output.WriteLine("Hardware evidence: " + reportPath);
    }
}
