using OpenCvSharp;
using SkySentry.Core.Analysis;
using SkySentry.Core.Frames;

namespace SkySentry.Analysis;

public sealed class MotionDetector : IDisposable
{
    private readonly AnalysisOptions _options;
    private readonly BackgroundSubtractorMOG2 _background;
    private int _seen;
    public string Status { get; private set; } = "Warming background";
    public long SuppressedFrames { get; private set; }
    public MotionDetector(AnalysisOptions options)
    {
        options.Validate(); _options = options;
        _background = BackgroundSubtractorMOG2.Create(options.BackgroundHistory, options.BackgroundVarianceThreshold, false);
    }
    public IReadOnlyList<MotionDetection> Detect(CapturedFrame frame)
    {
        using var source = new Mat(frame.Height, frame.Width, MatType.CV_8UC3);
        for (var y = 0; y < frame.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, y * frame.Stride, source.Data + y * (int)source.Step(), frame.Width * 3);
        var width = Math.Min(frame.Width, _options.MaxProcessingWidth);
        var height = Math.Max(1, (int)Math.Round(frame.Height * width / (double)frame.Width));
        using var resized = new Mat();
        using var gray = new Mat();
        using var mask = new Mat();
        Cv2.Resize(source, resized, new Size(width, height));
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
        _background.Apply(gray, mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        ApplyIgnoreMask(mask, _options.IgnoreRegions);
        if (++_seen <= _options.WarmupFrames) { Status = "Warming background"; return []; }
        using var allowed = new Mat(height, width, MatType.CV_8UC1, Scalar.All(255));
        ApplyIgnoreMask(allowed, _options.IgnoreRegions);
        var allowedPixels = Cv2.CountNonZero(allowed);
        if (allowedPixels == 0) { Status = "Entire frame ignored"; return []; }
        if (Cv2.CountNonZero(mask) / (double)allowedPixels > _options.GlobalMotionFraction)
        { SuppressedFrames++; Status = "Global change suppressed"; return []; }
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var result = new List<MotionDetection>();
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < _options.MinimumBlobArea || area > allowedPixels * _options.MaximumBlobAreaFraction) continue;
            var box = Cv2.BoundingRect(contour);
            var sx = frame.Width / (double)width; var sy = frame.Height / (double)height;
            result.Add(new(new PixelBox(box.X * sx, box.Y * sy, box.Width * sx, box.Height * sy), Math.Clamp(area / (box.Width * box.Height), 0, 1)));
        }
        Status = "Monitoring motion";
        return result.OrderByDescending(d => d.Bounds.Area).Take(_options.MaxTracks).ToArray();
    }
    public static void ApplyIgnoreMask(Mat mask, IReadOnlyList<IgnoreRegion> regions)
    {
        foreach (var region in regions)
        {
            region.Validate();
            var x = (int)Math.Floor(region.X * mask.Width); var y = (int)Math.Floor(region.Y * mask.Height);
            var right = Math.Min(mask.Width, (int)Math.Ceiling((region.X + region.Width) * mask.Width));
            var bottom = Math.Min(mask.Height, (int)Math.Ceiling((region.Y + region.Height) * mask.Height));
            Cv2.Rectangle(mask, new Rect(x, y, right - x, bottom - y), Scalar.All(0), -1);
        }
    }
    public void Dispose() => _background.Dispose();
}
