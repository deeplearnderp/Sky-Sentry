using System.Globalization;
using SkySentry.Core.Analysis;

namespace SkySentry.App.ViewModels;

public sealed class TrackRowViewModel
{
    public TrackSnapshot Track { get; }
    public TrackRowViewModel(TrackSnapshot track) { Track = track; }
    public string Id => "#" + Track.TrackId;
    public string Camera => Track.CameraLabel;
    public string Duration => $"{Track.Duration.TotalSeconds:F1}s";
    public string Speed => Format(Track.Measurement?.AngularSpeed, "°/s");
    public string Average => Format(Track.AverageAngularSpeed, "°/s");
    public string Peak => Format(Track.PeakAngularSpeed, "°/s");
    public string Acceleration => Format(Track.Measurement?.AngularAcceleration, "°/s²");
    public string DirectionChange => Format(Track.Measurement?.DirectionChangeDegrees, "°");
    public string Size => $"{Track.Bounds.Width:F0} × {Track.Bounds.Height:F0}";
    public string Classification => "--";
    public string AnomalyScore => "--";
    public string Detail => $"{Id} · {Camera}\nFirst seen: {Track.FirstSeenUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\nDuration: {Duration} · missed: {Track.MissedFrames}\n" +
        $"Pixel speed: {Format(Track.Measurement?.PixelSpeed, "px/s")} · displacement: {Track.TotalPixelDisplacement:F1}px\n" +
        $"Angular speed: {Speed} · average: {Average} · peak: {Peak}\n" +
        $"Angular displacement: {Format(Track.TotalAngularDisplacement, "°")} · acceleration: {Acceleration}\n" +
        $"Direction: {Format(Track.Measurement?.DirectionDegrees, "°")} (clockwise from right) · change: {DirectionChange}\n" +
        $"Size: {Size} px · area: {Track.Bounds.Area:F0}px² · change: {Format(Track.Measurement?.ApparentAreaChange * 100, "%")}\n" +
        "Classification: -- · Aircraft match: -- · Satellite match: -- · Anomaly score: --\nAngular values are approximate; FOV calibration is required.";
    public static string Format(double? value, string unit) => value is { } number && double.IsFinite(number)
        ? number.ToString("F2", CultureInfo.CurrentCulture) + unit : "--";
}
