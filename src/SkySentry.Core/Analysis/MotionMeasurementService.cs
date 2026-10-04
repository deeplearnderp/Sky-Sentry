namespace SkySentry.Core.Analysis;

/// <summary>Replaceable projection boundary; no WPF or OpenCV formulas.</summary>
public interface IAngularProjection
{
    double Displacement(PixelPoint from, PixelPoint to, CameraCalibration calibration);
}
public sealed class LinearFovProjection : IAngularProjection
{
    public double Displacement(PixelPoint from, PixelPoint to, CameraCalibration calibration)
    {
        calibration.Validate();
        var dx = (to.X - from.X) * calibration.HorizontalFovDegrees / calibration.FrameWidth;
        var dy = (to.Y - from.Y) * calibration.VerticalFovDegrees / calibration.FrameHeight;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
public sealed class MotionMeasurementService(IAngularProjection? projection = null)
{
    private readonly IAngularProjection _projection = projection ?? new LinearFovProjection();
    public MotionMeasurement Measure(PixelBox previous, PixelBox current, TimeSpan elapsed,
        CameraCalibration? calibration, MotionMeasurement? prior = null)
    {
        if (elapsed <= TimeSpan.Zero) throw new ArgumentException("Motion requires positive monotonic elapsed time.");
        var seconds = elapsed.TotalSeconds;
        var dx = current.Center.X - previous.Center.X;
        var dy = current.Center.Y - previous.Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        double? direction = distance > 0.000001 ? (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360 : null;
        var angular = calibration is null ? (double?)null : _projection.Displacement(previous.Center, current.Center, calibration);
        var speed = angular / seconds;
        return new MotionMeasurement(distance, distance / seconds, dx / seconds, dy / seconds, angular, speed,
            prior?.AngularSpeed is { } previousSpeed && speed is { } currentSpeed ? (currentSpeed - previousSpeed) / seconds : null,
            direction, direction is { } next && prior?.DirectionDegrees is { } old ? DirectionChange(old, next) : null,
            previous.Area > 0 ? (current.Area - previous.Area) / previous.Area : 0);
    }
    public static double DirectionChange(double previousDegrees, double currentDegrees) =>
        Math.Abs((currentDegrees - previousDegrees + 540) % 360 - 180);
}
