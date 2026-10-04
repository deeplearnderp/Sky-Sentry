using SkySentry.Core.Analysis;
using SkySentry.Core.Frames;

namespace SkySentry.Core.Tests.Analysis;

public class CalibrationAndMeasurementTests
{
    private static CameraCalibration Calibration => new("camera", 1000, 500, 100, 50);
    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(180, 50)]
    [InlineData(90, 0)]
    [InlineData(double.NaN, 40)]
    [InlineData(90, double.PositiveInfinity)]
    public void FovRejectsInvalidValues(double horizontal, double vertical) =>
        Assert.Throws<ArgumentException>(() => (Calibration with { HorizontalFovDegrees = horizontal, VerticalFovDegrees = vertical }).Validate());
    [Fact]
    public void CalibrationMustMatchDeviceAndResolution()
    {
        Assert.True(Calibration.Matches("camera", 1000, 500));
        Assert.False(Calibration.Matches("other", 1000, 500));
        Assert.False(Calibration.Matches("camera", 640, 480));
        Assert.Throws<ArgumentException>(() => (Calibration with { FrameWidth = 0 }).Validate());
    }
    [Fact]
    public void DiagonalPixelDisplacementConvertsBothAxes()
    {
        var angle = new LinearFovProjection().Displacement(new(0, 0), new(30, 40), Calibration);
        Assert.Equal(5, angle, 8);
    }
    [Fact]
    public void VelocityAccelerationDirectionAndSizeUseMonotonicInterval()
    {
        var service = new MotionMeasurementService();
        var first = service.Measure(new(0, 0, 10, 10), new(10, 0, 10, 10), TimeSpan.FromSeconds(1), Calibration);
        Assert.Equal(10, first.PixelSpeed);
        Assert.Equal(1, first.AngularSpeed);
        Assert.Null(first.AngularAcceleration);
        var next = service.Measure(new(10, 0, 10, 10), new(10, 20, 10, 10), TimeSpan.FromSeconds(1), Calibration, first);
        Assert.Equal(2, next.AngularSpeed);
        Assert.Equal(1, next.AngularAcceleration);
        Assert.Equal(90, next.DirectionDegrees);
        Assert.Equal(90, next.DirectionChangeDegrees);
        Assert.Equal(20, next.PixelDisplacement);
        Assert.Throws<ArgumentException>(() => service.Measure(new(), new(), TimeSpan.Zero, Calibration));
    }
    [Fact]
    public void UncalibratedAngularValuesRemainUnavailable()
    {
        var result = new MotionMeasurementService().Measure(new(0, 0, 10, 10), new(10, 0, 10, 10), TimeSpan.FromSeconds(1), null);
        Assert.Equal(10, result.PixelSpeed);
        Assert.Null(result.AngularSpeed);
        Assert.Null(result.AngularDisplacement);
    }
    [Theory]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, 20)]
    [InlineData(0, 180, 180)]
    [InlineData(270, 0, 90)]
    public void DirectionChangesWrapAt360(double from, double to, double expected) =>
        Assert.Equal(expected, MotionMeasurementService.DirectionChange(from, to), 8);
    [Fact]
    public void StationaryDirectionIsUnavailableAndAreaChangeIsFractional()
    {
        var service = new MotionMeasurementService();
        var result = service.Measure(new(0, 0, 10, 10), new(-5, -5, 20, 20), TimeSpan.FromSeconds(1), Calibration);
        Assert.Equal(0, result.PixelSpeed);
        Assert.Null(result.DirectionDegrees);
        Assert.Equal(3, result.ApparentAreaChange);
    }
    [Fact]
    public async Task AnalysisQueueKeepsOnlyLatestAndCountsDiscardedFrames()
    {
        using var queue = new LatestAnalysisQueue();
        for (var i = 0; i < 100; i++)
            Assert.True(queue.Submit(new CapturedFrame(new byte[3], 1, 1, 3, FramePixelFormat.Bgr24, new(i, TimeSpan.FromSeconds(i), DateTimeOffset.UtcNow))));
        Assert.Equal(99, queue.Dropped);
        var frame = await queue.TakeAsync(CancellationToken.None);
        Assert.Equal(99, frame!.Timestamp.SequenceNumber);
        queue.Dispose();
        Assert.Null(await queue.TakeAsync(CancellationToken.None));
    }
    [Fact]
    public void OverlayConfigurationAndAnalysisBoundsAreExplicit()
    {
        Assert.Equal(new OverlayOptions(), new OverlayOptions(true, true, true, true, false));
        var hidden = new OverlayOptions(false, false, false, false, false);
        Assert.False(hidden.Boxes || hidden.Ids || hidden.Speeds || hidden.Trajectories || hidden.Diagnostics);
        new AnalysisOptions().Validate();
        Assert.Throws<ArgumentException>(() => (new AnalysisOptions { MaxTracks = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (new AnalysisOptions { MaxFramesPerSecond = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (new AnalysisOptions { IgnoreRegions = [new(-0.1, 0, 1, 1)] }).Validate());
    }
}
