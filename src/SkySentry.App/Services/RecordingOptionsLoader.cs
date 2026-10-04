using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkySentry.Core.Recording;

namespace SkySentry.App.Services;

/// <summary>
/// Loads <see cref="RecordingOptions"/> from an optional
/// <see cref="AppPaths.SettingsFilePath"/> JSON override file, falling back to
/// <see cref="RecordingOptions.CreateDefault"/> if the file is missing, invalid,
/// or fails validation. Deliberately minimal — Phase 2 does not include a
/// settings UI, only a hand-editable file (see docs/RECORDING_PIPELINE.md).
/// </summary>
public static class RecordingOptionsLoader
{
    private sealed record OverridesDto(
        double? PreEventDurationSeconds,
        double? PostEventDurationSeconds,
        double? SegmentDurationSeconds,
        string? RollingBufferDirectory,
        string? EventsDirectory,
        long? MaxBufferBytesPerCamera,
        long? MaxQueuedFrameBytes);

    public static RecordingOptions Load(ILogger logger)
    {
        var defaults = RecordingOptions.CreateDefault(AppPaths.BufferDirectory, AppPaths.EventsDirectory);

        if (!File.Exists(AppPaths.SettingsFilePath))
        {
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            var overrides = JsonSerializer.Deserialize<OverridesDto>(json);
            if (overrides is null)
            {
                return defaults;
            }

            var merged = new RecordingOptions(
                PreEventDuration: overrides.PreEventDurationSeconds is { } pre ? TimeSpan.FromSeconds(pre) : defaults.PreEventDuration,
                PostEventDuration: overrides.PostEventDurationSeconds is { } post ? TimeSpan.FromSeconds(post) : defaults.PostEventDuration,
                SegmentDuration: overrides.SegmentDurationSeconds is { } seg ? TimeSpan.FromSeconds(seg) : defaults.SegmentDuration,
                RollingBufferDirectory: overrides.RollingBufferDirectory ?? defaults.RollingBufferDirectory,
                EventsDirectory: overrides.EventsDirectory ?? defaults.EventsDirectory)
            {
                MaxBufferBytesPerCamera = overrides.MaxBufferBytesPerCamera ?? defaults.MaxBufferBytesPerCamera,
                MaxQueuedFrameBytes = overrides.MaxQueuedFrameBytes ?? defaults.MaxQueuedFrameBytes
            };

            merged.Validate();
            logger.LogInformation("Loaded recording settings override from {Path}", AppPaths.SettingsFilePath);
            return merged;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load {Path}; falling back to default recording settings", AppPaths.SettingsFilePath);
            return defaults;
        }
    }
}
