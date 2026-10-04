using SkySentry.Core.Recording;

namespace SkySentry.Core.Tests.Recording;

public class RecordingOptionsTests
{
    [Theory]
    [InlineData("buffer", "buffer")]
    [InlineData("buffer", "buffer/events")]
    [InlineData("events/buffer", "events")]
    public void Validate_RejectsOverlappingPaths(string buffer, string events)
    {
        Assert.Throws<ArgumentException>(() => RecordingOptions.CreateDefault(buffer, events).Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidByteBudgetsAndExcessiveDurations()
    {
        Assert.Throws<ArgumentException>(() => (Defaults() with { MaxBufferBytesPerCamera = 1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults() with { MaxQueuedFrameBytes = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults() with { PostEventDuration = TimeSpan.MaxValue }).Validate());
    }
    private static RecordingOptions Defaults() => RecordingOptions.CreateDefault("buffer", "events");

    [Fact]
    public void CreateDefault_HasThirtySecondPreAndPostDurations()
    {
        var options = Defaults();

        Assert.Equal(TimeSpan.FromSeconds(30), options.PreEventDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PostEventDuration);
    }

    [Fact]
    public void Validate_AcceptsDefaults()
    {
        var exception = Record.Exception(() => Defaults().Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositivePreEventDuration(int seconds)
    {
        var options = Defaults() with { PreEventDuration = TimeSpan.FromSeconds(seconds) };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositivePostEventDuration(int seconds)
    {
        var options = Defaults() with { PostEventDuration = TimeSpan.FromSeconds(seconds) };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveSegmentDuration(int seconds)
    {
        var options = Defaults() with { SegmentDuration = TimeSpan.FromSeconds(seconds) };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_RejectsSegmentDurationLongerThanPreEventDuration()
    {
        var options = Defaults() with { SegmentDuration = TimeSpan.FromSeconds(60) };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_RejectsEmptyRollingBufferDirectory(string value)
    {
        var options = Defaults() with { RollingBufferDirectory = value };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_RejectsEmptyEventsDirectory(string value)
    {
        var options = Defaults() with { EventsDirectory = value };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }
}
