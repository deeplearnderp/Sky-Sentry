using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Capture.Discovery;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Cameras;

namespace SkySentry.Capture.Tests.Sessions;

/// <summary>
/// Hardware integration test — NOT part of normal automated runs. Requires a
/// real camera connected to the machine and is opt-in via the
/// <c>SKYSENTRY_RUN_HARDWARE_TESTS=1</c> environment variable, since CI/dev
/// machines without a camera attached cannot exercise it. See
/// docs/CURRENT_STATUS.md for the full manual hardware test checklist.
///
/// When enabled, this proves the real end-to-end path: DirectShow enumeration
/// finds an actual device, OpenCvCameraCaptureSession opens it, and frames
/// flow through <see cref="ICameraCaptureSession.FrameCaptured"/> with
/// monotonically increasing timestamps.
/// </summary>
public class HardwareSmokeTests
{
    private static bool HardwareTestsEnabled =>
        Environment.GetEnvironmentVariable("SKYSENTRY_RUN_HARDWARE_TESTS") == "1";

    [Fact]
    public async Task RealCamera_DeliversFrames_WhenHardwareTestsEnabled()
    {
        if (!HardwareTestsEnabled)
        {
            // Opt-in only: no camera hardware is assumed to be present by default.
            return;
        }

        var discovery = new DirectShowCameraDiscoveryService(NullLogger<DirectShowCameraDiscoveryService>.Instance);
        var devices = discovery.DiscoverDevices();
        Assert.NotEmpty(devices);

        var device = devices.First(d => d.SupportedFormats.Count > 0);
        var format = device.SupportedFormats.FirstOrDefault(f => f.Width <= 1280) is { Width: > 0 } f
            ? f
            : new CameraCaptureFormat(640, 480, 30);

        using var session = new OpenCvCameraCaptureSession(
            "Hardware Smoke Test", NullLogger<OpenCvCameraCaptureSession>.Instance);

        var framesReceived = new List<long>();
        var tcs = new TaskCompletionSource();
        session.FrameCaptured += (_, frame) =>
        {
            lock (framesReceived)
            {
                framesReceived.Add(frame.Timestamp.SequenceNumber);
                if (framesReceived.Count >= 5)
                {
                    tcs.TrySetResult();
                }
            }
        };

        session.Configure(device, format);
        await session.StartAsync();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        await session.StopAsync();

        Assert.Same(tcs.Task, completed);
        Assert.True(framesReceived.Count >= 5, $"Expected >= 5 frames, got {framesReceived.Count}");

        // Sequence numbers must be strictly increasing — proves timestamping/
        // ordering works against a real device, not just synthetic data.
        for (var i = 1; i < framesReceived.Count; i++)
        {
            Assert.True(framesReceived[i] > framesReceived[i - 1]);
        }
    }
}
