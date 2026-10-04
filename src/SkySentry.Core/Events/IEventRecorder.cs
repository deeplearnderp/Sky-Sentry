using SkySentry.Core.Recording;

namespace SkySentry.Core.Events;

/// <summary>
/// Orchestrates saving an event: securing the pre-event window, capturing the
/// configured post-event duration, and writing the final event package (see
/// docs/EVENT_FORMAT.md, docs/RECORDING_PIPELINE.md). Implemented in
/// SkySentry.Recording (<c>SegmentedEventRecorder</c>).
/// </summary>
public interface IEventRecorder
{
    /// <summary>Current position in the event-save lifecycle; <see cref="EventLifecycleState.Idle"/> when no event is in progress.</summary>
    EventLifecycleState State { get; }

    /// <summary>Raised on every lifecycle transition — handlers must not block (see docs/CAMERA_PIPELINE.md bounded-delivery pattern).</summary>
    event EventHandler<EventLifecycleState>? StateChanged;

    /// <summary>
    /// Triggers a new event save. Secures the pre-event window immediately,
    /// waits for the configured post-event duration, then finalizes and returns
    /// the written metadata. At least one camera must be running and have
    /// buffered footage, or no camera succeeds, for this to throw; a single
    /// successfully-saved camera is sufficient for overall success.
    /// </summary>
    /// <exception cref="EventAlreadyInProgressException">An event is already in progress (see docs/DECISIONS.md).</exception>
    Task<SkyEventMetadata> TriggerEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// If an event is in progress, attempts to finalize it immediately using
    /// whatever footage is already available, within <paramref name="timeout"/>.
    /// Called during application shutdown so Sky Sentry never hangs indefinitely
    /// waiting for a full post-event window to complete (see docs/DECISIONS.md
    /// "shutdown behavior"). No-op if no event is in progress.
    /// </summary>
    Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
