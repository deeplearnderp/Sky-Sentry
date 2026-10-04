using SkySentry.Core.Clock;

namespace SkySentry.Core.Tests.Clock;

/// <summary>
/// Covers <see cref="MonotonicClock"/>'s monotonicity guarantee — the property
/// relied on throughout the capture pipeline to order frames and compute
/// durations regardless of wall-clock adjustments.
/// </summary>
public class MonotonicClockTests
{
    [Fact]
    public void Elapsed_NeverDecreasesAcrossSuccessiveReads()
    {
        var clock = new MonotonicClock();

        var previous = clock.Elapsed;
        for (var i = 0; i < 50; i++)
        {
            var current = clock.Elapsed;
            Assert.True(current >= previous, "Elapsed must never decrease.");
            previous = current;
        }
    }

    [Fact]
    public void UtcNowApproximate_TracksReferencePlusElapsed()
    {
        var clock = new MonotonicClock();

        var approx = clock.UtcNowApproximate;

        Assert.True(approx >= clock.ReferenceUtc);
        Assert.True(approx - clock.ReferenceUtc <= clock.Elapsed + TimeSpan.FromSeconds(1));
    }
}
