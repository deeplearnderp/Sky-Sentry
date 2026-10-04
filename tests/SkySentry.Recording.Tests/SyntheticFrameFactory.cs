using SkySentry.Core.Frames;

namespace SkySentry.Recording.Tests;

/// <summary>
/// Builds synthetic <see cref="CapturedFrame"/> instances directly (no camera
/// hardware or OpenCvSharp capture needed) so the segmented recording pipeline's
/// automated tests remain hardware-free, consistent with the rest of the test
/// suite. Only <see cref="HardwareSmokeTests"/>-style tests should need a real
/// camera.
/// </summary>
internal static class SyntheticFrameFactory
{
    public static CapturedFrame Create(
        int width, int height, long sequence, TimeSpan monotonic, DateTimeOffset wallClockUtc, byte colorValue = 128)
    {
        var stride = width * 3;
        var pixels = new byte[stride * height];
        Array.Fill(pixels, colorValue);
        return new CapturedFrame(pixels, width, height, stride, FramePixelFormat.Bgr24, new FrameTimestamp(sequence, monotonic, wallClockUtc));
    }
}
