namespace SkySentry.Core.Frames;

/// <summary>Raw pixel layout of a <see cref="CapturedFrame"/> buffer.</summary>
public enum FramePixelFormat
{
    /// <summary>8-bit, 3 channels, Blue-Green-Red order (OpenCV's native default).</summary>
    Bgr24,
}
