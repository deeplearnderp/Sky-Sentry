namespace SkySentry.Core.Recording;

/// <summary>
/// Pure, hardware/IO-free helper functions for reasoning about a trigger point
/// against a camera's known segments: which segments cover a requested
/// pre/post-event window, and what duration was actually achieved. Kept in Core
/// (no OpenCvSharp dependency) so this logic — the trickiest part of the
/// segmented-recording design to get right — can be unit tested fast and in
/// isolation from real video encode/decode. See docs/RECORDING_PIPELINE.md.
/// </summary>
public static class EventWindowMath
{
    /// <summary>
    /// Returns, in order, every segment that overlaps
    /// [<paramref name="windowStart"/>, <paramref name="windowEnd"/>]. A segment
    /// overlaps the window if it starts before the window ends and ends after the
    /// window starts.
    /// </summary>
    public static IReadOnlyList<RecordedSegment> SelectSegmentsInWindow(
        IReadOnlyList<RecordedSegment> segments, TimeSpan windowStart, TimeSpan windowEnd)
    {
        var result = new List<RecordedSegment>();
        foreach (var segment in segments)
        {
            if (segment.StartMonotonic < windowEnd && segment.EndMonotonic > windowStart)
            {
                result.Add(segment);
            }
        }

        return result;
    }

    /// <summary>
    /// Actual pre-event duration achieved: how much footage before
    /// <paramref name="triggerMonotonic"/> is actually covered by the given
    /// (already-selected) segments. Never exceeds <paramref name="requestedPreEventDuration"/>
    /// even if more/older segments happen to be present, and is zero if no
    /// segment starts before the trigger.
    /// </summary>
    public static TimeSpan CalculateActualPreEventDuration(
        IReadOnlyList<RecordedSegment> segments, TimeSpan triggerMonotonic, TimeSpan requestedPreEventDuration)
    {
        if (segments.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var earliestStart = segments.Min(s => s.StartMonotonic);
        if (earliestStart >= triggerMonotonic)
        {
            return TimeSpan.Zero;
        }

        var achieved = triggerMonotonic - earliestStart;
        return achieved > requestedPreEventDuration ? requestedPreEventDuration : achieved;
    }

    /// <summary>
    /// Actual post-event duration achieved: how much footage after
    /// <paramref name="triggerMonotonic"/> is covered by the given segments. Zero
    /// if nothing was recorded after the trigger.
    /// </summary>
    public static TimeSpan CalculateActualPostEventDuration(
        IReadOnlyList<RecordedSegment> segments, TimeSpan triggerMonotonic)
    {
        if (segments.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var latestEnd = segments.Max(s => s.EndMonotonic);
        return latestEnd <= triggerMonotonic ? TimeSpan.Zero : latestEnd - triggerMonotonic;
    }

    /// <summary>
    /// The retention floor (cutoff) stale-segment cleanup should apply: segments
    /// that end before this point may be safely deleted. One extra
    /// <paramref name="segmentDuration"/> of margin is kept so segment-boundary
    /// rounding never truncates the requested pre-event window.
    /// </summary>
    public static TimeSpan CalculateRetentionFloor(
        TimeSpan latestKnownMonotonic, TimeSpan preEventDuration, TimeSpan segmentDuration)
    {
        var floor = latestKnownMonotonic - preEventDuration - segmentDuration;
        return floor < TimeSpan.Zero ? TimeSpan.Zero : floor;
    }
}
