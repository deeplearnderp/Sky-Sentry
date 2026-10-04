namespace SkySentry.Core.Frames;

/// <summary>
/// An immutable, owned copy of a single captured video frame plus its timing
/// metadata. Kept free of any OpenCV/UI dependency so it can flow through
/// SkySentry.Core, SkySentry.Buffering, and unit tests without pulling in native
/// capture libraries. SkySentry.Capture is responsible for converting from its
/// native <c>Mat</c> representation into this type.
/// </summary>
/// <param name="Pixels">
/// Owned pixel buffer (row-major, no padding beyond <see cref="Stride"/>). Callers
/// must not mutate this array; treat the frame as immutable once constructed.
/// </param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Stride">Bytes per row (may exceed Width * bytes-per-pixel due to alignment).</param>
/// <param name="Format">Pixel layout of <see cref="Pixels"/>.</param>
/// <param name="Timestamp">Capture timing metadata.</param>
public sealed record CapturedFrame(
    byte[] Pixels,
    int Width,
    int Height,
    int Stride,
    FramePixelFormat Format,
    FrameTimestamp Timestamp);
