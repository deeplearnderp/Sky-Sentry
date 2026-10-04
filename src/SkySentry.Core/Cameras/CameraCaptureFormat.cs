namespace SkySentry.Core.Cameras;

/// <summary>
/// A capture format supported by a camera device: resolution + frame rate.
/// Frame rate is expressed as a rational (numerator/denominator) because many
/// UVC devices report non-integer rates (e.g. 29.97 fps) more precisely this way.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="FrameRateNumerator">Frame rate numerator (e.g. 30000).</param>
/// <param name="FrameRateDenominator">Frame rate denominator (e.g. 1001 for 29.97fps).</param>
public readonly record struct CameraCaptureFormat(
    int Width,
    int Height,
    int FrameRateNumerator,
    int FrameRateDenominator = 1)
{
    public double FrameRate => FrameRateDenominator == 0
        ? 0
        : (double)FrameRateNumerator / FrameRateDenominator;

    public override string ToString() => $"{Width}x{Height} @ {FrameRate:0.##}fps";
}
