using SkySentry.Core.Recording;

namespace SkySentry.Core.Events;

/// <summary>
/// Placeholder for future per-track analysis output (motion/classification/anomaly
/// subsystems). Intentionally empty in Phase 1/2 — present so
/// <see cref="SkyEventMetadata"/>'s shape does not need to change when those
/// subsystems are added later.
/// </summary>
public sealed record TrackAnalysisResult(
    string TrackId,
    string Classification,
    double? AnomalyScore);

/// <summary>
/// Durable metadata describing one recorded event, serialized to
/// <c>metadata.json</c> inside the event's folder. See docs/EVENT_FORMAT.md.
/// </summary>
/// <param name="EventId">Unique, filesystem-safe event identifier.</param>
/// <param name="TriggeredAtUtc">Wall-clock time the event was triggered (e.g. Save Event pressed).</param>
/// <param name="CreatedAtUtc">Wall-clock time the event package finished being written.</param>
/// <param name="ApplicationVersion">Sky Sentry version that produced this event, for future compatibility checks.</param>
/// <param name="RequestedPreEventDuration">The pre-event duration that was configured at trigger time.</param>
/// <param name="RequestedPostEventDuration">The post-event duration that was configured at trigger time.</param>
/// <param name="Cameras">Per-camera outcome (one entry per camera that was considered).</param>
/// <param name="AnalysisResults">Empty in Phase 1/2; populated by future detection/classification/anomaly subsystems.</param>
public sealed record SkyEventMetadata(
    string EventId,
    DateTimeOffset TriggeredAtUtc,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    TimeSpan RequestedPreEventDuration,
    TimeSpan RequestedPostEventDuration,
    IReadOnlyList<CameraRecordingResult> Cameras,
    IReadOnlyList<TrackAnalysisResult> AnalysisResults)
{
    public IReadOnlyList<SkySentry.Core.Analysis.TrackEventReference> AssociatedTracks { get; init; } = [];
    public string TriggerReason { get; init; } = "Manual Save Event";
}
