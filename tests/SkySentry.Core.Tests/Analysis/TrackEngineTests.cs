using SkySentry.Core.Analysis;

namespace SkySentry.Core.Tests.Analysis;

public class TrackEngineTests
{
    private static MotionDetection Detection(double x, double y = 0) => new(new(x, y, 10, 10), 0.8);
    private static IReadOnlyList<TrackSnapshot> Update(TrackEngine tracker, double seconds, params MotionDetection[] detections) =>
        tracker.Update(detections, TimeSpan.FromSeconds(seconds), DateTimeOffset.UnixEpoch.AddSeconds(-seconds));
    [Fact]
    public void TrackIdsPersistThroughMovementMissAndDirectionChange()
    {
        var engine = new TrackEngine("camera", "Camera 1", new() { MissedFrameTolerance = 2, TrackTimeout = TimeSpan.FromSeconds(5) });
        var first = Assert.Single(Update(engine, 0, Detection(0)));
        var second = Assert.Single(Update(engine, 1, Detection(10)));
        Assert.Equal(first.TrackId, second.TrackId);
        var missed = Assert.Single(Update(engine, 2));
        Assert.Equal(1, missed.MissedFrames);
        var resumed = Assert.Single(Update(engine, 3, Detection(20, 10)));
        Assert.Equal(first.TrackId, resumed.TrackId);
        Assert.Equal(0, resumed.MissedFrames);
        Assert.NotNull(resumed.Measurement!.DirectionChangeDegrees);
        // Reversing wall-clock time above must not affect positive monotonic speed.
        Assert.True(resumed.Measurement.PixelSpeed > 0);
    }
    [Fact]
    public void ToleranceAndTimeoutExpireTracks()
    {
        var engine = new TrackEngine("camera", "Camera 1", new() { MissedFrameTolerance = 1 });
        Update(engine, 0, Detection(0));
        Assert.Single(Update(engine, 0.1));
        Assert.Empty(Update(engine, 0.2));
        var next = Assert.Single(Update(engine, 0.3, Detection(0)));
        var expired = Assert.Single(Update(engine, 2, Detection(0)));
        Assert.NotEqual(next.TrackId, expired.TrackId);
    }
    [Fact]
    public void AssociationIsOneToOneAndUsesPrediction()
    {
        var engine = new TrackEngine("camera", "Camera 1", new() { AssociationDistancePixels = 15, TrackTimeout = TimeSpan.FromSeconds(5) });
        var first = Update(engine, 0, Detection(0), Detection(100));
        var second = Update(engine, 1, Detection(10), Detection(90));
        var predicted = Update(engine, 2, Detection(20), Detection(80));
        Assert.Equal(first.Select(t => t.TrackId), predicted.Select(t => t.TrackId));
        Assert.Equal(2, second.Count);
        var far = Update(engine, 3, Detection(300));
        Assert.Contains(far, t => !first.Any(old => old.TrackId == t.TrackId));
    }
    [Fact]
    public void TrajectoryMeasurementHistoryAndActiveTracksStayBounded()
    {
        var options = new AnalysisOptions { MaxTrajectoryPoints = 4, MaxMeasurementSamples = 3, MaxTracks = 2, TrackTimeout = TimeSpan.FromSeconds(5) };
        var engine = new TrackEngine("camera", "Camera 1", options, new("camera", 1000, 500, 100, 50));
        for (var i = 0; i < 20; i++) Update(engine, i * 0.1, Detection(i), Detection(100 + i), Detection(300 + i));
        var tracks = Update(engine, 2, Detection(20), Detection(120));
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, t => { Assert.Equal(4, t.Trajectory.Count); Assert.Equal(3, t.MeasurementHistory.Count); Assert.True(t.TotalPixelDisplacement > 4); });
        Assert.Equal(1, tracks[0].AverageAngularSpeed!.Value, 7);
        Assert.Equal(1, tracks[0].PeakAngularSpeed!.Value, 7);
    }
    [Fact]
    public void NonIncreasingTimestampDoesNotProduceInvalidMeasurements()
    {
        var engine = new TrackEngine("camera", "Camera 1", new());
        var first = Update(engine, 1, Detection(0));
        var same = Update(engine, 1, Detection(100));
        Assert.Equal(first[0].Centroid, same[0].Centroid);
        Assert.Null(same[0].Measurement);
    }
}
