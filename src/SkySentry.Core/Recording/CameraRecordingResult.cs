namespace SkySentry.Core.Recording;

/// <summary>
/// Outcome of saving one camera's contribution to an event. A failure here
/// (e.g. the camera was not running, or had no buffered footage) never prevents
/// the event as a whole from succeeding if at least one other camera worked —
/// see docs/DECISIONS.md "single-camera events must still succeed".
/// </summary>
/// <param name="CameraLabel">e.g. "Camera 1".</param>
/// <param name="Success">Whether this camera's clip was written successfully.</param>
/// <param name="FailureReason">Human-readable reason, present only when <see cref="Success"/> is false.</param>
/// <param name="VideoRelativePath">Path to this camera's clip, relative to the event folder (null on failure).</param>
/// <param name="ThumbnailRelativePath">Path to this camera's thumbnail, relative to the event folder (null on failure).</param>
/// <param name="Width">Capture width in pixels.</param>
/// <param name="Height">Capture height in pixels.</param>
/// <param name="RequestedFps">Frame rate the camera was configured for.</param>
/// <param name="AchievedFps">Actual measured frame rate achieved by the capture/recording pipeline.</param>
/// <param name="ActualPreEventDuration">Actual pre-trigger footage duration included (may be less than requested).</param>
/// <param name="ActualPostEventDuration">Actual post-trigger footage duration included (may be less than requested).</param>
/// <param name="RecordingStartUtc">Wall-clock time of the first frame included in the saved clip.</param>
/// <param name="RecordingEndUtc">Wall-clock time of the last frame included in the saved clip.</param>
/// <param name="DroppedFrameCount">Frames dropped by the bounded recording pipeline for this camera (0 under normal load).</param>
public sealed record CameraRecordingResult(
    string CameraLabel,
    bool Success,
    string? FailureReason,
    string? VideoRelativePath,
    string? ThumbnailRelativePath,
    int Width,
    int Height,
    double RequestedFps,
    double AchievedFps,
    TimeSpan ActualPreEventDuration,
    TimeSpan ActualPostEventDuration,
    DateTimeOffset RecordingStartUtc,
    DateTimeOffset RecordingEndUtc,
    long DroppedFrameCount)
{
    public string? DeviceId { get; init; }
    public string? DeviceFriendlyName { get; init; }
    public TimeSpan TriggerMonotonic { get; init; }
    public TimeSpan RecordingStartMonotonic { get; init; }
    public TimeSpan RecordingEndMonotonic { get; init; }
    public double PlaybackFrameRate { get; init; }
    public string? Warning { get; init; }
}
