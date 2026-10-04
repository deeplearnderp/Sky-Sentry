using SkySentry.Core.Cameras;

namespace SkySentry.Core.Tests.Cameras;

/// <summary>
/// Covers <see cref="CameraCaptureFormat"/>'s rational frame-rate math, since
/// driver-reported rates are often non-integer (e.g. 29.97fps == 30000/1001).
/// </summary>
public class CameraCaptureFormatTests
{
    [Theory]
    [InlineData(30, 1, 30.0)]
    [InlineData(30000, 1001, 29.97)]
    [InlineData(24000, 1001, 23.976)]
    public void FrameRate_ComputesRationalValue(int numerator, int denominator, double expected)
    {
        var format = new CameraCaptureFormat(1920, 1080, numerator, denominator);

        Assert.Equal(expected, format.FrameRate, precision: 2);
    }

    [Fact]
    public void FrameRate_ReturnsZero_WhenDenominatorIsZero()
    {
        var format = new CameraCaptureFormat(1920, 1080, 30, 0);

        Assert.Equal(0, format.FrameRate);
    }

    [Fact]
    public void ToString_IncludesResolutionAndFrameRate()
    {
        var format = new CameraCaptureFormat(1280, 720, 30, 1);

        Assert.Equal("1280x720 @ 30fps", format.ToString());
    }
}
