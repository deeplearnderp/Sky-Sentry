using SkySentry.Core.Frames;

namespace SkySentry.Core.Cameras;

/// <summary>Raised whenever a session's <see cref="CameraSessionState"/> changes.</summary>
public sealed class CameraSessionStateChangedEventArgs(
    CameraSessionState previousState,
    CameraSessionState newState,
    string? message) : EventArgs
{
    public CameraSessionState PreviousState { get; } = previousState;
    public CameraSessionState NewState { get; } = newState;

    /// <summary>Optional human-readable detail (e.g. exception message), for logging/UI.</summary>
    public string? Message { get; } = message;
}

/// <summary>
/// An independent, single-camera capture session. Each physical camera gets its
/// own session instance; sessions do not share state, and a failure in one must
/// never affect another session's ability to run. Implemented in SkySentry.Capture.
/// </summary>
public interface ICameraCaptureSession : IDisposable
{
    /// <summary>Stable label for this session slot, e.g. "Camera 1".</summary>
    string SessionLabel { get; }

    /// <summary>The device currently selected for this session, if any.</summary>
    CameraDeviceInfo? SelectedDevice { get; }

    /// <summary>The capture format currently selected/negotiated, if any.</summary>
    CameraCaptureFormat? SelectedFormat { get; }

    CameraSessionState State { get; }

    /// <summary>Raised on every state transition (for UI status + logging).</summary>
    event EventHandler<CameraSessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised when a new frame has been captured and converted. Handlers must
    /// return quickly (e.g. push into a bounded mailbox/buffer) — this event fires
    /// on a background capture thread, never the UI thread.
    /// </summary>
    event EventHandler<CapturedFrame>? FrameCaptured;

    /// <summary>
    /// Selects the device and format this session will use. Must be called while
    /// <see cref="State"/> is <see cref="CameraSessionState.Stopped"/>.
    /// </summary>
    void Configure(CameraDeviceInfo device, CameraCaptureFormat format);

    /// <summary>
    /// Opens the configured device and begins the background capture loop.
    /// Throws <see cref="InvalidOperationException"/> if no device has been configured.
    /// Does not throw on device-level failures — those surface via
    /// <see cref="StateChanged"/> transitioning to <see cref="CameraSessionState.Error"/>
    /// or <see cref="CameraSessionState.Disconnected"/>.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the capture loop and closes the device, if running.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Convenience: stop then start again with the same configuration.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);
}
