namespace SkySentry.Core.Events;

/// <summary>
/// Thrown when <c>IEventRecorder.TriggerEventAsync</c> is called while an event
/// is already in progress. Sky Sentry's chosen policy (see docs/DECISIONS.md) is
/// to reject overlapping triggers rather than merge or queue them — this is the
/// simplest behavior that is easy to reason about and test.
/// </summary>
public sealed class EventAlreadyInProgressException(string message) : InvalidOperationException(message);
