using System.Runtime.InteropServices;
using DirectShowLib;
using Microsoft.Extensions.Logging;
using SkySentry.Core.Cameras;

namespace SkySentry.Capture.Discovery;

/// <summary>
/// Enumerates Windows video capture devices via DirectShow. DirectShow is used
/// (rather than relying solely on OpenCvSharp's device-open-by-index) because it
/// exposes human-friendly names, a reasonably stable per-device moniker path, and
/// best-effort access to each device's supported capture formats — none of which
/// OpenCvSharp's VideoCapture alone provides. See docs/CAMERA_PIPELINE.md.
/// </summary>
public sealed class DirectShowCameraDiscoveryService(ILogger<DirectShowCameraDiscoveryService> logger)
    : ICameraDiscoveryService
{
    public IReadOnlyList<CameraDeviceInfo> DiscoverDevices()
    {
        var results = new List<CameraDeviceInfo>();
        DsDevice[] devices;
        try
        {
            devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Camera enumeration failed (DirectShow video input category query)");
            return results;
        }

        for (var i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            try
            {
                var deviceId = !string.IsNullOrWhiteSpace(device.DevicePath)
                    ? device.DevicePath
                    : $"{device.Name}#{i}";
                var formats = TryGetSupportedFormats(device);

                results.Add(new CameraDeviceInfo(
                    DeviceId: deviceId,
                    FriendlyName: device.Name ?? $"Camera {i}",
                    FallbackIndex: i,
                    SupportedFormats: formats));

                logger.LogInformation(
                    "Discovered camera device: {FriendlyName} ({DeviceId}), {FormatCount} format(s)",
                    device.Name, deviceId, formats.Count);
            }
            catch (Exception ex)
            {
                // One misbehaving device must not prevent discovering the others.
                logger.LogWarning(ex, "Failed to inspect camera device at index {Index}", i);
            }
            finally
            {
                device.Dispose();
            }
        }

        return results;
    }

    /// <summary>
    /// Best-effort enumeration of a device's supported capture formats by building
    /// a minimal filter graph and querying its capture pin's <see cref="IAMStreamConfig"/>.
    /// Returns an empty list (rather than throwing) if the driver doesn't expose
    /// this information or the graph can't be built — format selection then falls
    /// back to letting the capture session request a format directly and see what
    /// the driver negotiates. See docs/CAMERA_PIPELINE.md for the documented limitation.
    /// </summary>
    private List<CameraCaptureFormat> TryGetSupportedFormats(DsDevice device)
    {
        var formats = new List<CameraCaptureFormat>();
        IFilterGraph2? graph = null;
        IBaseFilter? sourceFilter = null;

        try
        {
            graph = (IFilterGraph2)new FilterGraph();
            var hr = graph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out sourceFilter);
            if (hr != 0 || sourceFilter is null)
            {
                return formats;
            }

            var pin = DsFindPin.ByCategory(sourceFilter, PinCategory.Capture, 0);
            if (pin is not IAMStreamConfig streamConfig)
            {
                return formats;
            }

            streamConfig!.GetNumberOfCapabilities(out var count, out var size);
            if (count <= 0)
            {
                return formats;
            }

            var capabilitiesPtr = Marshal.AllocCoTaskMem(size);
            try
            {
                var seen = new HashSet<(int, int, int)>();
                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        streamConfig.GetStreamCaps(i, out var mediaType, capabilitiesPtr);

                        if (mediaType is not null && mediaType.formatType == FormatType.VideoInfo && mediaType.formatPtr != IntPtr.Zero)
                        {
                            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr) ?? throw new InvalidOperationException("Invalid video capability header.");
                            var width = videoInfo.BmiHeader.Width;
                            var height = videoInfo.BmiHeader.Height;
                            var avgTimePerFrame = videoInfo.AvgTimePerFrame; // 100ns units
                            if (width > 0 && height > 0 && avgTimePerFrame > 0)
                            {
                                const long ticksPerSecond = 10_000_000L;
                                var approxFps = (int)Math.Round(ticksPerSecond / (double)avgTimePerFrame);
                                var key = (width, height, approxFps);
                                if (seen.Add(key))
                                {
                                    formats.Add(new CameraCaptureFormat(
                                        width,
                                        height,
                                        (int)ticksPerSecond,
                                        (int)avgTimePerFrame));
                                }
                            }
                        }

                        DsUtils.FreeAMMediaType(mediaType);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to read capability {Index} for device {Name}", i, device.Name);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(capabilitiesPtr);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Capability enumeration not available for device {Name}", device.Name);
        }
        finally
        {
            if (sourceFilter is not null)
            {
                Marshal.ReleaseComObject(sourceFilter);
            }

            if (graph is not null)
            {
                Marshal.ReleaseComObject(graph);
            }
        }

        return formats;
    }
}
