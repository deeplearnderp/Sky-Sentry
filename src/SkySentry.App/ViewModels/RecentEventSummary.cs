namespace SkySentry.App.ViewModels;

/// <summary>
/// One row in the Recent Events list (see docs/EVENT_FORMAT.md). Deliberately
/// minimal for Phase 2 — a full event review/playback UI is not required yet.
/// </summary>
public sealed record RecentEventSummary(
    DateTimeOffset TriggeredAtUtc,
    string EventId,
    string Status,
    bool Camera1Saved,
    bool Camera2Saved,
    string EventFolder);
