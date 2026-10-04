using SkySentry.Core.Recording;

namespace SkySentry.Core.Tests.Recording;

public class EventWindowMathTests
{
    private static RecordedSegment Segment(long sequence, double startSeconds, double endSeconds, int frameCount = 10) =>
        new(
            FilePath: $"seg-{sequence:D6}.mp4",
            SequenceNumber: sequence,
            StartMonotonic: TimeSpan.FromSeconds(startSeconds),
            EndMonotonic: TimeSpan.FromSeconds(endSeconds),
            StartWallClockUtc: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(startSeconds),
            EndWallClockUtc: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(endSeconds),
            FrameCount: frameCount);

    [Fact]
    public void SelectSegmentsInWindow_ReturnsOnlyOverlappingSegments()
    {
        var segments = new[]
        {
            Segment(0, 0, 2),
            Segment(1, 2, 4),
            Segment(2, 4, 6),
            Segment(3, 6, 8),
        };

        var selected = EventWindowMath.SelectSegmentsInWindow(
            segments, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        Assert.Equal([1L, 2L], selected.Select(s => s.SequenceNumber));
    }

    [Fact]
    public void SelectSegmentsInWindow_EmptyWhenNoOverlap()
    {
        var segments = new[] { Segment(0, 0, 2) };

        var selected = EventWindowMath.SelectSegmentsInWindow(
            segments, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12));

        Assert.Empty(selected);
    }

    [Fact]
    public void CalculateActualPreEventDuration_ClampsToRequested()
    {
        var segments = new[] { Segment(0, 0, 100) };

        var actual = EventWindowMath.CalculateActualPreEventDuration(
            segments, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), actual);
    }

    [Fact]
    public void CalculateActualPreEventDuration_ReturnsAchievedWhenLessThanRequested()
    {
        var segments = new[] { Segment(0, 90, 100) };

        var actual = EventWindowMath.CalculateActualPreEventDuration(
            segments, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(10), actual);
    }

    [Fact]
    public void CalculateActualPreEventDuration_ZeroWhenNoSegments()
    {
        var actual = EventWindowMath.CalculateActualPreEventDuration(
            [], TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.Zero, actual);
    }

    [Fact]
    public void CalculateActualPreEventDuration_ZeroWhenEarliestSegmentStartsAtOrAfterTrigger()
    {
        var segments = new[] { Segment(0, 100, 110) };

        var actual = EventWindowMath.CalculateActualPreEventDuration(
            segments, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.Zero, actual);
    }

    [Fact]
    public void CalculateActualPostEventDuration_ReturnsCoverageAfterTrigger()
    {
        var segments = new[] { Segment(0, 100, 130) };

        var actual = EventWindowMath.CalculateActualPostEventDuration(segments, TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.FromSeconds(30), actual);
    }

    [Fact]
    public void CalculateActualPostEventDuration_ZeroWhenNothingAfterTrigger()
    {
        var segments = new[] { Segment(0, 0, 50) };

        var actual = EventWindowMath.CalculateActualPostEventDuration(segments, TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.Zero, actual);
    }

    [Fact]
    public void CalculateRetentionFloor_SubtractsPreEventAndSegmentMargin()
    {
        var floor = EventWindowMath.CalculateRetentionFloor(
            TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(68), floor);
    }

    [Fact]
    public void CalculateRetentionFloor_NeverNegative()
    {
        var floor = EventWindowMath.CalculateRetentionFloor(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, floor);
    }
}
