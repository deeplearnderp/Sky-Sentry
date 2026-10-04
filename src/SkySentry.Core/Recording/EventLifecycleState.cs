namespace SkySentry.Core.Recording;

/// <summary>
/// Explicit event-save lifecycle (see docs/RECORDING_PIPELINE.md), replacing
/// scattered booleans with a single, observable state machine:
/// <para>
/// Idle -&gt; Triggered -&gt; CapturingPostEvent -&gt; Finalizing -&gt; Completed (or Failed)
/// -&gt; back to Idle.
/// </para>
/// Only one event may be in flight at a time (see
/// docs/DECISIONS.md "overlapping trigger behavior") — a trigger received while
/// not <see cref="Idle"/> is rejected rather than merged or queued.
/// </summary>
public enum EventLifecycleState
{
    /// <summary>No event in progress; cameras are only feeding the rolling buffer.</summary>
    Idle,

    /// <summary>Trigger just received; pre-event segments are being identified/protected from cleanup.</summary>
    Triggered,

    /// <summary>Pre-event window secured; waiting for the configured post-event duration to elapse.</summary>
    CapturingPostEvent,

    /// <summary>Post-event window complete; concatenating segments and writing the event package.</summary>
    Finalizing,

    /// <summary>Event package written successfully (for at least one camera).</summary>
    Completed,

    /// <summary>Event could not be saved for any camera, or finalizing threw.</summary>
    Failed,
}
