namespace SkySentry.Core.Analysis;

public readonly record struct PixelPoint(double X, double Y)
{
    public double DistanceTo(PixelPoint other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}
public readonly record struct PixelBox(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public PixelPoint Center => new(X + Width / 2, Y + Height / 2);
    public double IntersectionOverUnion(PixelBox other)
    {
        var intersection = Math.Max(0, Math.Min(X + Width, other.X + other.Width) - Math.Max(X, other.X)) *
                           Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y));
        var union = Area + other.Area - intersection;
        return union > 0 ? intersection / union : 0;
    }
}
public sealed record MotionDetection(PixelBox Bounds, double Confidence);
public sealed record IgnoreRegion(double X, double Y, double Width, double Height)
{
    public void Validate()
    {
        if (!double.IsFinite(X + Y + Width + Height) || X < 0 || Y < 0 || Width <= 0 || Height <= 0 ||
            X + Width > 1 || Y + Height > 1) throw new ArgumentException("Ignore rectangles must lie in normalized [0,1] coordinates.");
    }
}
public sealed record CameraCalibration(string DeviceId, int FrameWidth, int FrameHeight, double HorizontalFovDegrees, double VerticalFovDegrees)
{
    public bool IsProvisional { get; init; } = true;
    public string ProjectionModel { get; init; } = "LinearFovApproximation";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId) || FrameWidth <= 0 || FrameHeight <= 0 ||
            !double.IsFinite(HorizontalFovDegrees) || !double.IsFinite(VerticalFovDegrees) ||
            HorizontalFovDegrees <= 0 || HorizontalFovDegrees >= 180 || VerticalFovDegrees <= 0 || VerticalFovDegrees >= 180 ||
            ProjectionModel != "LinearFovApproximation")
            throw new ArgumentException("Calibration requires device, frame dimensions, and finite FOV values between 0 and 180 degrees.");
    }
    public bool Matches(string deviceId, int width, int height) => DeviceId == deviceId && FrameWidth == width && FrameHeight == height;
}
public sealed record AnalysisOptions
{
    public bool Enabled { get; init; } = true;
    public double MaxFramesPerSecond { get; init; } = 10;
    public int MaxProcessingWidth { get; init; } = 640;
    public double BackgroundVarianceThreshold { get; init; } = 25;
    public int BackgroundHistory { get; init; } = 150;
    public int WarmupFrames { get; init; } = 10;
    public double MinimumBlobArea { get; init; } = 20;
    public double MaximumBlobAreaFraction { get; init; } = 0.15;
    public double GlobalMotionFraction { get; init; } = 0.35;
    public double AssociationDistancePixels { get; init; } = 80;
    public int MissedFrameTolerance { get; init; } = 5;
    public TimeSpan TrackTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public int MaxTracks { get; init; } = 64;
    public int MaxTrajectoryPoints { get; init; } = 120;
    public int MaxMeasurementSamples { get; init; } = 60;
    public IgnoreRegion[] IgnoreRegions { get; init; } = [];
    public void Validate()
    {
        if (!double.IsFinite(MaxFramesPerSecond) || MaxFramesPerSecond <= 0 || MaxFramesPerSecond > 30 ||
            MaxProcessingWidth < 32 || MaxProcessingWidth > 1920 || BackgroundHistory < 2 || BackgroundHistory > 10000 ||
            !double.IsFinite(BackgroundVarianceThreshold) || BackgroundVarianceThreshold <= 0 ||
            WarmupFrames < 0 || WarmupFrames > 1000 || !double.IsFinite(MinimumBlobArea) || MinimumBlobArea <= 0 ||
            !double.IsFinite(MaximumBlobAreaFraction) || MaximumBlobAreaFraction <= 0 || MaximumBlobAreaFraction > 1 ||
            !double.IsFinite(GlobalMotionFraction) || GlobalMotionFraction <= 0 || GlobalMotionFraction > 1 ||
            !double.IsFinite(AssociationDistancePixels) || AssociationDistancePixels <= 0 ||
            MissedFrameTolerance < 0 || MissedFrameTolerance > 300 || TrackTimeout <= TimeSpan.Zero || TrackTimeout > TimeSpan.FromMinutes(1) ||
            MaxTracks < 1 || MaxTracks > 256 || MaxTrajectoryPoints < 2 || MaxTrajectoryPoints > 1000 ||
            MaxMeasurementSamples < 2 || MaxMeasurementSamples > 1000 || IgnoreRegions is null || IgnoreRegions.Length > 64)
            throw new ArgumentException("Analysis configuration is outside supported bounds.");
        foreach (var region in IgnoreRegions) region.Validate();
    }
}
public sealed record OverlayOptions(bool Boxes = true, bool Ids = true, bool Trajectories = true, bool Speeds = true, bool Diagnostics = false);
public sealed record MotionMeasurement(double PixelDisplacement, double PixelSpeed, double VelocityX, double VelocityY,
    double? AngularDisplacement, double? AngularSpeed, double? AngularAcceleration, double? DirectionDegrees,
    double? DirectionChangeDegrees, double ApparentAreaChange);
public sealed record TrajectorySample(PixelPoint Centroid, TimeSpan Monotonic);
public sealed record TrackSnapshot(string TrackId, string CameraId, string CameraLabel, DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc, TimeSpan FirstMonotonic, TimeSpan LastMonotonic, PixelPoint Centroid, PixelBox Bounds,
    double Confidence, int MissedFrames, MotionMeasurement? Measurement, double? AverageAngularSpeed, double? PeakAngularSpeed,
    double TotalPixelDisplacement, double? TotalAngularDisplacement, IReadOnlyList<TrajectorySample> Trajectory,
    IReadOnlyList<MotionMeasurement> MeasurementHistory)
{
    public TimeSpan Duration => LastMonotonic - FirstMonotonic;
}
public sealed record AnalysisSnapshot(int FrameWidth, int FrameHeight, TimeSpan Monotonic, IReadOnlyList<TrackSnapshot> Tracks,
    string Status, double AnalysisFps, long DroppedFrames, long SuppressedFrames);
public sealed record TrackEventReference(string TrackId, string CameraId, string CameraLabel, string TriggerReason,
    TimeSpan Duration, double? PeakAngularVelocity, double? DirectionChangeDegrees);
