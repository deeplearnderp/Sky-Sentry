namespace SkySentry.Core;

/// <summary>
/// Sky Sentry's own version string, recorded into saved event metadata (see
/// docs/EVENT_FORMAT.md) so future tooling can reason about which pipeline
/// produced a given event. Bumped manually per phase/milestone rather than tied
/// to assembly versioning, since Phase 1/2/3 are the meaningful compatibility
/// boundaries for this project, not individual builds.
/// </summary>
public static class AppVersion
{
    public const string Current = "0.3.0-phase3";
}
