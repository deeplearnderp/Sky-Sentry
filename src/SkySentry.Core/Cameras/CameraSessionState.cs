namespace SkySentry.Core.Cameras;

/// <summary>Lifecycle state of a <c>ICameraCaptureSession</c>.</summary>
public enum CameraSessionState
{
    /// <summary>No device selected / session not yet started.</summary>
    Stopped,

    /// <summary>Opening the device and negotiating capture format.</summary>
    Starting,

    /// <summary>Device opened and delivering frames.</summary>
    Running,

    /// <summary>
    /// The device stopped responding or was unplugged while running. The session
    /// remains in this state (not Stopped) so the UI can distinguish "never started"
    /// from "lost connection", and so a reconnect attempt can be made later.
    /// </summary>
    Disconnected,

    /// <summary>An unrecoverable error occurred starting or running the session.</summary>
    Error,
}
