namespace SkySentry.Core.Recording;

/// <summary>
/// Configuration for the segmented rolling-buffer recording pipeline (see
/// docs/RECORDING_PIPELINE.md). Replaces Phase 1's fixed 30-second in-memory
/// retention constant with explicit, validated, configurable values.
/// </summary>
/// <param name="PreEventDuration">
/// Target duration of pre-trigger footage to retain/preserve. Default 30s per the
/// Phase 2 brief. The buffer may hold slightly more than this (see
/// <see cref="SegmentDuration"/> margin) and may hold less if the camera has not
/// been running long enough yet — callers must treat this as a target, not a
/// guarantee.
/// </param>
/// <param name="PostEventDuration">Target duration of footage to keep capturing after a trigger. Default 30s.</param>
/// <param name="SegmentDuration">
/// Length of each individual rolling-buffer segment file. Short segments (1-5s)
/// bound both the granularity of retention (how much "extra" footage beyond the
/// target window gets kept) and the amount of work lost if the process crashes
/// mid-segment. Default 2s.
/// </param>
/// <param name="RollingBufferDirectory">
/// Root directory for transient, continuously-overwritten segment files. Must be
/// a Sky Sentry-owned directory — never a parent/shared/system directory — since
/// stale segments are deleted automatically.
/// </param>
/// <param name="EventsDirectory">Root directory for permanently saved events (see docs/EVENT_FORMAT.md).</param>
public sealed record RecordingOptions(
    TimeSpan PreEventDuration,
    TimeSpan PostEventDuration,
    TimeSpan SegmentDuration,
    string RollingBufferDirectory,
    string EventsDirectory)
{
    public long MaxBufferBytesPerCamera { get; init; } = 256L * 1024 * 1024;
    public long MaxQueuedFrameBytes { get; init; } = 32L * 1024 * 1024;
    public static RecordingOptions CreateDefault(string rollingBufferDirectory, string eventsDirectory) =>
        new(
            PreEventDuration: TimeSpan.FromSeconds(30),
            PostEventDuration: TimeSpan.FromSeconds(30),
            SegmentDuration: TimeSpan.FromSeconds(2),
            RollingBufferDirectory: rollingBufferDirectory,
            EventsDirectory: eventsDirectory);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if any value is nonsensical. Called
    /// once at startup (and whenever configuration is reloaded) rather than
    /// trusting arbitrary values to flow into the recording pipeline unchecked.
    /// </summary>
    public void Validate()
    {
        if (MaxBufferBytesPerCamera < 16L * 1024 * 1024 || MaxQueuedFrameBytes < 1024 || MaxQueuedFrameBytes > 128L * 1024 * 1024)
            throw new ArgumentException("Invalid recording byte budget.");
        if (PreEventDuration > TimeSpan.FromMinutes(10) || PostEventDuration > TimeSpan.FromMinutes(10) || SegmentDuration > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Recording durations exceed supported bounds.");
        if (PreEventDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("PreEventDuration must be positive.", nameof(PreEventDuration));
        }

        if (PostEventDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("PostEventDuration must be positive.", nameof(PostEventDuration));
        }

        if (SegmentDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("SegmentDuration must be positive.", nameof(SegmentDuration));
        }

        if (SegmentDuration > PreEventDuration)
        {
            throw new ArgumentException(
                "SegmentDuration must not exceed PreEventDuration, or the rolling buffer could not retain a full pre-event window.",
                nameof(SegmentDuration));
        }

        if (string.IsNullOrWhiteSpace(RollingBufferDirectory))
        {
            throw new ArgumentException("RollingBufferDirectory must not be empty.", nameof(RollingBufferDirectory));
        }

        if (string.IsNullOrWhiteSpace(EventsDirectory))
        {
            throw new ArgumentException("EventsDirectory must not be empty.", nameof(EventsDirectory));
        }
        var buffer = Path.GetFullPath(RollingBufferDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var events = Path.GetFullPath(EventsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(buffer, events, StringComparison.OrdinalIgnoreCase) ||
            buffer.StartsWith(events + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            events.StartsWith(buffer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Buffer and event roots must be separate directories.");
    }
}
