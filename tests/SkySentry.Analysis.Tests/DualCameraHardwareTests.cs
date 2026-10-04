using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Capture.Discovery;
using Xunit.Abstractions;

namespace SkySentry.Analysis.Tests;
public class DualCameraHardwareTests(ITestOutputHelper output)
{
    [Fact]
    public void EnumeratePhysicalDevices_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("SKYSENTRY_RUN_DUAL_HARDWARE_TESTS") != "1") return;
        var devices = new DirectShowCameraDiscoveryService(NullLogger<DirectShowCameraDiscoveryService>.Instance).DiscoverDevices();
        var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        Directory.CreateDirectory("hardware-evidence");
        File.WriteAllText("hardware-evidence/devices.json", json);
        Assert.Contains(devices, d => d.FriendlyName.Contains("C920", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(devices, d => d.FriendlyName.Contains("EMEET", StringComparison.OrdinalIgnoreCase) && d.DeviceId.StartsWith("@device:pnp:"));
    }
}
