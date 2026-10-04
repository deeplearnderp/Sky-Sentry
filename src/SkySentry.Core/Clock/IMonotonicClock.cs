namespace SkySentry.Core.Clock;

/// <summary>
/// Abstraction over a monotonic high-resolution clock, used to generate frame
/// timestamps that are safe to compare for ordering/elapsed-time purposes even
/// though wall-clock time can jump (NTP sync, DST, user changes system time).
/// </summary>
public interface IMonotonicClock
{
    /// <summary>
    /// UTC wall-clock time captured once when the clock was constructed/started.
    /// Used as a reference point to translate monotonic elapsed time back into an
    /// approximate wall-clock timestamp for display/metadata purposes.
    /// </summary>
    DateTimeOffset ReferenceUtc { get; }

    /// <summary>
    /// Monotonic elapsed time since <see cref="ReferenceUtc"/>. Always
    /// non-decreasing; safe for measuring durations and ordering events even
    /// across system clock adjustments.
    /// </summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// Convenience: <see cref="ReferenceUtc"/> + <see cref="Elapsed"/>. This is an
    /// approximation of "now" in wall-clock terms, suitable for metadata/display,
    /// but ordering/duration comparisons should use <see cref="Elapsed"/> directly.
    /// </summary>
    DateTimeOffset UtcNowApproximate { get; }
}
