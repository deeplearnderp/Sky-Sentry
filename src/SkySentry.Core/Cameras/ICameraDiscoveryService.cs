namespace SkySentry.Core.Cameras;

/// <summary>
/// Discovers video capture devices connected to this machine.
/// Implemented in SkySentry.Capture using DirectShow on Windows.
/// </summary>
public interface ICameraDiscoveryService
{
    /// <summary>
    /// Enumerates currently connected video capture devices. Safe to call
    /// repeatedly (e.g. to refresh a device list); does not open any device.
    /// </summary>
    IReadOnlyList<CameraDeviceInfo> DiscoverDevices();
}
