using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Capture.Discovery;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Cameras;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
namespace SkySentry.Analysis.Tests;
public class DualModeProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProbeDual1080_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("SKYSENTRY_RUN_DUAL_PROBE") != "1") return;
        var devices = new DirectShowCameraDiscoveryService(NullLogger<DirectShowCameraDiscoveryService>.Instance).DiscoverDevices();
        var selected = new[] { devices.Single(d=>d.FriendlyName.Contains("C920")), devices.Single(d=>d.FriendlyName=="EMEET PIXY") };
        var reports = new List<object>();
        foreach(var size in new[]{ (1920,1080), (1280,720) })
        {
            var sessions=selected.Select((d,i)=>new OpenCvCameraCaptureSession($"Camera {i+1}",NullLogger<OpenCvCameraCaptureSession>.Instance)).ToArray();
            var counts=new long[2]; var dimensions=new string?[2];
            try
            {
                for(var i=0;i<2;i++) { var index=i; sessions[i].FrameCaptured+=(_,f)=>{Interlocked.Increment(ref counts[index]); dimensions[index]=$"{f.Width}x{f.Height}";}; sessions[i].Configure(selected[i],new(size.Item1,size.Item2,30)); await sessions[i].StartAsync(); }
                await Task.Delay(6000); var start=counts.ToArray(); var timer=Stopwatch.StartNew(); await Task.Delay(10000);
                var report=new {Requested=$"{size.Item1}x{size.Item2}@30",Actual=dimensions,Fps=counts.Select((n,i)=>(n-start[i])/timer.Elapsed.TotalSeconds).ToArray(),States=sessions.Select(s=>s.State.ToString()).ToArray()}; reports.Add(report); output.WriteLine(JsonSerializer.Serialize(report));
            }
            finally { foreach(var session in sessions) {await session.StopAsync(); session.Dispose();} }
        }
        Directory.CreateDirectory("hardware-evidence"); File.WriteAllText("hardware-evidence/mode-probe.json",JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
    }
}
