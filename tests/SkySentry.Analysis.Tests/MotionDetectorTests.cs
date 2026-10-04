using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SkySentry.Analysis;
using SkySentry.Core.Analysis;
using SkySentry.Core.Frames;

namespace SkySentry.Analysis.Tests;

public class MotionDetectorTests
{
    public static CapturedFrame Frame(int sequence, int? x = null, byte background = 0)
    {
        const int width = 160, height = 120, stride = width * 3;
        var pixels = new byte[stride * height];
        Array.Fill(pixels, background);
        if (x is { } left)
            for (var y = 40; y < 55; y++)
                for (var column = left; column < Math.Min(left + 15, width); column++)
                    for (var color = 0; color < 3; color++) pixels[y * stride + column * 3 + color] = 255;
        return new(pixels, width, height, stride, FramePixelFormat.Bgr24,
            new(sequence, TimeSpan.FromMilliseconds(sequence * 100), DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 100)));
    }
    [Fact]
    public async Task IdleInputExpiresOverlayTracksAndResolutionMismatchLeavesAnglesUnavailable()
    {
        using var worker = new CameraAnalysisWorker("camera", "Camera 1",
            new() { WarmupFrames = 2, TrackTimeout = TimeSpan.FromMilliseconds(400) },
            new("camera", 640, 480, 80, 60), NullLogger.Instance);
        for (var i = 0; i < 8; i++) { worker.Submit(Frame(i)); await Task.Delay(110); }
        for (var i = 8; i < 12; i++) { worker.Submit(Frame(i, 20 + (i - 8) * 3)); await Task.Delay(110); }
        Assert.NotEmpty(worker.Latest!.Tracks);
        Assert.All(worker.Latest.Tracks, t => Assert.Null(t.Measurement?.AngularSpeed));
        await Task.Delay(650);
        Assert.Empty(worker.Latest!.Tracks);
        Assert.Equal("Waiting for frames", worker.Latest.Status);
    }

    [Fact]
    public void SyntheticObjectDetectedAndIgnoredRegionSuppressesIt()
    {
        var options = new AnalysisOptions { WarmupFrames = 5, MinimumBlobArea = 10 };
        using var detector = new MotionDetector(options);
        using var masked = new MotionDetector(options with { IgnoreRegions = [new(0, 0.2, 0.5, 0.4)] });
        for (var i = 0; i < 10; i++) { detector.Detect(Frame(i)); masked.Detect(Frame(i)); }
        var result = detector.Detect(Frame(10, 20));
        Assert.NotEmpty(result);
        Assert.Contains(result, detection => detection.Bounds.Center.X > 15 && detection.Bounds.Center.X < 45);
        Assert.Empty(masked.Detect(Frame(10, 20)));
    }
    [Fact]
    public void RoiMaskZerosOnlyNormalizedExcludedPixels()
    {
        using var mask = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(255));
        MotionDetector.ApplyIgnoreMask(mask, [new(0, 0, 0.5, 1)]);
        Assert.Equal(5000, Cv2.CountNonZero(mask));
        Assert.Throws<ArgumentException>(() => new IgnoreRegion(0.8, 0, 0.5, 1).Validate());
    }
    [Fact]
    public void GlobalExposureChangesAreSuppressed()
    {
        using var detector = new MotionDetector(new() { WarmupFrames = 2 });
        for (var i = 0; i < 10; i++) detector.Detect(Frame(i));
        Assert.Empty(detector.Detect(Frame(10, background: 255)));
        Assert.Equal("Global change suppressed", detector.Status);
        Assert.Equal(1, detector.SuppressedFrames);
    }
    [Fact]
    public async Task BoundedWorkerProducesCalibratedPersistentTracksAndDisposes()
    {
        using var worker = new CameraAnalysisWorker("camera", "Camera 1", new() { WarmupFrames = 5, MinimumBlobArea = 10 },
            new("camera", 160, 120, 80, 60), NullLogger.Instance);
        for (var i = 0; i < 12; i++) { worker.Submit(Frame(i)); await Task.Delay(110); }
        string? id = null;
        for (var i = 12; i < 20; i++)
        {
            worker.Submit(Frame(i, 20 + (i - 12) * 3));
            await Task.Delay(110);
            if (worker.Latest?.Tracks.FirstOrDefault() is { } track)
            {
                id ??= track.TrackId;
                Assert.Equal(id, track.TrackId);
            }
        }
        Assert.NotNull(id);
        Assert.Contains(worker.Latest!.Tracks, t => t.Measurement?.AngularSpeed > 0);
        for (var i = 20; i < 2000; i++) worker.Submit(Frame(i));
        Assert.True(worker.DroppedFrames > 0);
    }
}
