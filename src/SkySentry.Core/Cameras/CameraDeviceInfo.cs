namespace SkySentry.Core.Cameras;

/// <summary>
/// Describes a connected video capture device as discovered by the platform's
/// device enumeration (DirectShow on Windows). This is a discovery-time snapshot,
/// not a live handle — opening the device is a separate step (see
/// <c>ICameraCaptureSession</c> in SkySentry.Capture).
/// </summary>
/// <param name="DeviceId">
/// A stable identifier for the physical device where the platform provides one
/// (DirectShow device path / moniker display name). This is preferred over a
/// numeric index because Windows does not guarantee index stability across
/// reboots, reconnects, or when other capture devices are added/removed.
/// Falls back to a synthesized value (friendly name + ordinal) if the platform
/// does not expose a persistent identifier for a given device.
/// </param>
/// <param name="FriendlyName">Human-readable device name, e.g. "Logitech BRIO".</param>
/// <param name="FallbackIndex">
/// The OpenCvSharp/driver-order index at discovery time. Used only as a last-resort
/// way to open the device (e.g. via VideoCapture(index)); must not be relied on as a
/// persistent identity across process restarts.
/// </param>
/// <param name="SupportedFormats">Capture formats the device reports supporting.</param>
public sealed record CameraDeviceInfo(
    string DeviceId,
    string FriendlyName,
    int FallbackIndex,
    IReadOnlyList<CameraCaptureFormat> SupportedFormats)
{
    public override string ToString() => $"{FriendlyName} ({DeviceId})";
}
