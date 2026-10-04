using System.IO;
using System.Text.Json;
using SkySentry.Core.Analysis;

namespace SkySentry.App.Services;

public sealed class AnalysisSettingsStore
{
    private sealed record Settings(AnalysisOptions DefaultOptions, Dictionary<string, CameraAnalysisSettings> Cameras);
    private readonly string _path = Path.Combine(AppPaths.RootDirectory, "analysis-settings.json");
    private Settings _settings = new(new(), new());
    public string? LoadWarning { get; private set; }
    public AnalysisSettingsStore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? throw new ArgumentException("Empty analysis settings.");
            loaded.DefaultOptions.Validate();
            if (loaded.Cameras is null || loaded.Cameras.Count > 128) throw new ArgumentException("Invalid camera settings.");
            foreach (var camera in loaded.Cameras.Values) { camera.Options.Validate(); camera.Calibration?.Validate();
                if (camera.CalibrationProfiles is null || camera.CalibrationProfiles.Length > 16) throw new ArgumentException("Too many calibration profiles.");
                foreach (var profile in camera.CalibrationProfiles) profile.Validate(); }
            _settings = loaded;
        }
        catch (Exception ex) { LoadWarning = "Analysis settings fallback: " + ex.Message; }
    }
    public CameraAnalysisSettings ForDevice(string deviceId) => _settings.Cameras.TryGetValue(deviceId, out var settings)
        ? settings : new(null, _settings.DefaultOptions);
    public void Save(string deviceId, CameraCalibration calibration)
    {
        calibration.Validate();
        if (calibration.DeviceId != deviceId) throw new ArgumentException("Calibration device identity mismatch.");
        var previous = ForDevice(deviceId);
        var profiles = previous.CalibrationProfiles.Concat(previous.Calibration is { } legacy ? [legacy] : [])
            .Where(c => c.DeviceId == deviceId && (c.FrameWidth != calibration.FrameWidth || c.FrameHeight != calibration.FrameHeight))
            .DistinctBy(c => (c.FrameWidth, c.FrameHeight)).Append(calibration).ToArray();
        if (profiles.Length > 16) throw new ArgumentException("At most 16 resolution calibrations per device.");
        var updated = new Dictionary<string, CameraAnalysisSettings>(_settings.Cameras)
            { [deviceId] = previous with { Calibration = calibration, CalibrationProfiles = profiles } };
        var merged = _settings with { Cameras = updated };
        Directory.CreateDirectory(AppPaths.RootDirectory);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(_path + ".tmp", _path, true);
        _settings = merged;
    }
}
