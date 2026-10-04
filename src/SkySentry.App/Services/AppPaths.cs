using System.IO;

namespace SkySentry.App.Services;

/// <summary>
/// Centralizes where Sky Sentry writes runtime data (logs, saved events). Kept
/// out of the repository/working tree deliberately — this is machine-local state,
/// not source. Defaults under the user's LocalApplicationData folder.
/// </summary>
public static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkySentry");

    public static string EventsDirectory { get; } = Path.Combine(RootDirectory, "Events");

    public static string LogsDirectory { get; } = Path.Combine(RootDirectory, "logs");

    /// <summary>
    /// Transient, Sky Sentry-owned rolling segment buffer directory (see
    /// docs/RECORDING_PIPELINE.md). Contents are continuously created/deleted by
    /// the recording pipeline and are never source data — never point this at a
    /// shared/system/parent directory.
    /// </summary>
    public static string BufferDirectory { get; } = Path.Combine(RootDirectory, "buffer");

    /// <summary>Optional configuration override file (see docs/RECORDING_PIPELINE.md "configuration").</summary>
    public static string SettingsFilePath { get; } = Path.Combine(RootDirectory, "settings.json");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(EventsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BufferDirectory);
    }
}
