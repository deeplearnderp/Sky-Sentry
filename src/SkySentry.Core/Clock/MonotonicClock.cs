using System.Diagnostics;

namespace SkySentry.Core.Clock;

/// <summary>
/// Default <see cref="IMonotonicClock"/> implementation backed by
/// <see cref="Stopwatch"/>. One instance should be shared process-wide (e.g. via DI
/// singleton) so all subsystems derive timestamps from the same reference point.
/// </summary>
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly Stopwatch _stopwatch;

    public MonotonicClock()
    {
        ReferenceUtc = DateTimeOffset.UtcNow;
        _stopwatch = Stopwatch.StartNew();
    }

    public DateTimeOffset ReferenceUtc { get; }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public DateTimeOffset UtcNowApproximate => ReferenceUtc + Elapsed;
}
