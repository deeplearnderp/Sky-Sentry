namespace SkySentry.Core.Cameras;

/// <summary>Resolve the current driver ordinal by stable identity, never by an old ordinal.</summary>
public static class CameraDeviceResolver
{
    public static int ResolveIndex(string deviceId, IReadOnlyList<CameraDeviceInfo> current)
    {
        var match = current.SingleOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        return match?.FallbackIndex ?? throw new InvalidOperationException("Selected camera is no longer present: " + deviceId);
    }
}
