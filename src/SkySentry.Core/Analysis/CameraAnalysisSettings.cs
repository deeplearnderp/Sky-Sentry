namespace SkySentry.Core.Analysis;
public sealed record CameraAnalysisSettings(CameraCalibration? Calibration, AnalysisOptions Options)
{
    public CameraCalibration[] CalibrationProfiles { get; init; } = [];
    public CameraCalibration? ForResolution(string deviceId, int width, int height) =>
        CalibrationProfiles.FirstOrDefault(c => c.Matches(deviceId, width, height)) ??
        (Calibration?.Matches(deviceId, width, height) == true ? Calibration : null);
}
