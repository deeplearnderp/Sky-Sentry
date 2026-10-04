using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SkySentry.Analysis;
using SkySentry.Capture.Discovery;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Analysis;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Frames;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;
using Xunit.Abstractions;
namespace SkySentry.Analysis.Tests;
public class DualRecordingHardwareTests(ITestOutputHelper output)
{
    private sealed class Observation
    {
        public long Count;
        public CapturedFrame? Latest;
        public DateTimeOffset? First;
        public long MaxSequenceGap;
    }
    [Fact]
    public async Task BothPhysicalCameras_RecordAnalyzeRestartAndSave_WhenEnabled()
    {
        if(Environment.GetEnvironmentVariable("SKYSENTRY_RUN_DUAL_HARDWARE_TESTS")!="1") return;
        var all=new DirectShowCameraDiscoveryService(NullLogger<DirectShowCameraDiscoveryService>.Instance).DiscoverDevices();
        var devices=new[]{all.Single(d=>d.FriendlyName.Contains("C920")),all.Single(d=>d.FriendlyName=="EMEET PIXY" && d.DeviceId.Contains("usb#"))};
        CameraCaptureFormat[] formats = [new(1920,1080,30), new(1280,720,30)];
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SkySentry.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        // Native video I/O may still require a path shorter than MAX_PATH.
        var root=Path.Combine(directory!.FullName,".validation-dual",Guid.NewGuid().ToString("N")[..8]);
        var options=RecordingOptions.CreateDefault(Path.Combine(root,"buffer"),Path.Combine(root,"events"));
        var recordings=Enumerable.Range(0,2).Select(i=>new SegmentedCameraRecorder($"Camera {i+1}",options,NullLogger.Instance)).ToArray();
        var sessions=Enumerable.Range(0,2).Select(i=>new OpenCvCameraCaptureSession($"Camera {i+1}",NullLogger<OpenCvCameraCaptureSession>.Instance)).ToArray();
        var observations=Enumerable.Range(0,2).Select(_=>new Observation()).ToArray();
        var workers=new CameraAnalysisWorker?[2]; var cycleReports=new List<object>();
        var stopTimer=Stopwatch.StartNew();
        try
        {
            for(var i=0;i<2;i++)
            {
                var index=i;
                sessions[i].FrameCaptured+=(_,frame)=>
                {
                    recordings[index].AddFrame(frame);
                    var prior=Volatile.Read(ref observations[index].Latest);
                    observations[index].First??=frame.Timestamp.WallClockUtc;
                    if(prior is not null && frame.Timestamp.SequenceNumber>prior.Timestamp.SequenceNumber)
                        observations[index].MaxSequenceGap=Math.Max(observations[index].MaxSequenceGap,frame.Timestamp.SequenceNumber-prior.Timestamp.SequenceNumber-1);
                    Volatile.Write(ref observations[index].Latest,frame);
                    Interlocked.Increment(ref observations[index].Count);
                    Volatile.Read(ref workers[index])?.Submit(frame);
                };
                sessions[i].Configure(devices[i],formats[i]); recordings[i].Start(formats[i],devices[i]); await sessions[i].StartAsync();
            }
            await Task.Delay(6000);
            for(var round=0;round<2;round++)
                for(var index=0;index<2;index++)
                {
                    var other=1-index; var prior=Interlocked.Read(ref observations[other].Count); var timer=Stopwatch.StartNew();
                    await sessions[index].StopAsync(); recordings[index].Stop();
                    await Task.Delay(1500);
                    var otherAdvance=Interlocked.Read(ref observations[other].Count)-prior;
                    Assert.True(otherAdvance>10,"Other camera stopped delivering during independent stop.");
                    recordings[index].Start(formats[index],devices[index]); await sessions[index].StartAsync();
                    var starting=Interlocked.Read(ref observations[index].Count); await Task.Delay(5000);
                    Assert.Equal(CameraSessionState.Running,sessions[index].State);
                    Assert.True(Interlocked.Read(ref observations[index].Count)>starting+30);
                    cycleReports.Add(new{Round=round+1,Restarted=$"Camera {index+1}",OtherFrames=otherAdvance,ElapsedSeconds=timer.Elapsed.TotalSeconds});
                }
            var baselineStart=observations.Select(o=>Interlocked.Read(ref o.Count)).ToArray(); var baselineTimer=Stopwatch.StartNew();
            await Task.Delay(5000); var baselineEnd=observations.Select(o=>Interlocked.Read(ref o.Count)).ToArray();
            var baselineFps=baselineEnd.Select((n,i)=>(n-baselineStart[i])/baselineTimer.Elapsed.TotalSeconds).ToArray();
            for(var i=0;i<2;i++) Volatile.Write(ref workers[i],new CameraAnalysisWorker(devices[i].DeviceId,$"Camera {i+1}",new(),null,NullLogger.Instance));
            using var process=Process.GetCurrentProcess(); var cpuStart=process.TotalProcessorTime; var timerActive=Stopwatch.StartNew();
            var maxTracks=new int[2]; var ids=new[]{new HashSet<string>(),new HashSet<string>()}; var skews=new List<double>();
            long maxDisk=0; long maxRam=0;
            for(var second=0;second<35;second++)
            {
                await Task.Delay(1000);
                for(var camera=0;camera<2;camera++) Assert.True(recordings[camera].IsRunning, $"Recorder {camera+1}: {recordings[camera].FailureReason}");
                process.Refresh(); maxRam=Math.Max(maxRam,process.WorkingSet64);
                maxDisk=Math.Max(maxDisk,Directory.GetFiles(options.RollingBufferDirectory,"*.mp4",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length));
                var frames=observations.Select(o=>Volatile.Read(ref o.Latest)).ToArray();
                if(frames.All(f=>f is not null)) skews.Add(Math.Abs((frames[0]!.Timestamp.WallClockUtc-frames[1]!.Timestamp.WallClockUtc).TotalMilliseconds));
                for(var i=0;i<2;i++) if(workers[i]!.Latest is { } snapshot)
                {
                    maxTracks[i]=Math.Max(maxTracks[i],snapshot.Tracks.Count);
                    Assert.All(snapshot.Tracks,t=>Assert.Equal(devices[i].DeviceId,t.CameraId));
                    Assert.All(snapshot.Tracks,t=>Assert.Null(t.Measurement?.AngularSpeed));
                    foreach(var track in snapshot.Tracks) ids[i].Add(track.TrackId);
                }
            }
            Assert.Empty(ids[0].Intersect(ids[1]));
            var atTrigger=observations.Select(o=>Volatile.Read(ref o.Latest)!.Timestamp).ToArray();
            var eventRecorder=new SegmentedEventRecorder(recordings,options,NullLogger.Instance,
                ()=>workers.SelectMany(w=>w!.Latest?.Tracks??[]).Select(t=>new TrackEventReference(t.TrackId,t.CameraId,t.CameraLabel,"Manual Save Event",t.Duration,t.PeakAngularSpeed,t.Measurement?.DirectionChangeDegrees)).ToArray());
            TimeSpan? finalizing=null; eventRecorder.StateChanged+=(_,state)=>{if(state==EventLifecycleState.Finalizing) finalizing=timerActive.Elapsed;};
            var saved=await eventRecorder.TriggerEventAsync();
            Assert.All(recordings,r=>Assert.True(r.IsRunning,r.FailureReason));
            Assert.All(saved.Cameras,c=>Assert.Null(c.Warning)); var finalizationSeconds=finalizing is { } f ? (timerActive.Elapsed-f).TotalSeconds : 0;
            var elapsed=timerActive.Elapsed.TotalSeconds; var cpu=(process.TotalProcessorTime-cpuStart).TotalSeconds;
            var analysisFps=workers.Select(w=>w!.Latest!.AnalysisFps).ToArray();
            var analysisDrops=workers.Select(w=>w!.DroppedFrames).ToArray();
            var captureFps=observations.Select((o,i)=>(Interlocked.Read(ref o.Count)-baselineEnd[i])/elapsed).ToArray();
            var readFailures=sessions.Select(s=>s.ReadFailureCount).ToArray();
            Assert.All(saved.Cameras,c=>Assert.True(c.Success,c.FailureReason));
            Assert.All(saved.Cameras,c=>Assert.True(c.ActualPreEventDuration.TotalSeconds>=29));
            Assert.All(saved.Cameras,c=>Assert.True(c.ActualPostEventDuration.TotalSeconds>=30));
            var metadataPath=Directory.GetFiles(options.EventsDirectory,"metadata.json",SearchOption.AllDirectories).Single();
            var clips=new List<object>();
            foreach(var camera in saved.Cameras)
            {
                var path=Path.Combine(Path.GetDirectoryName(metadataPath)!,camera.VideoRelativePath!);
                using var video=new VideoCapture(path); using var mat=new Mat(); Assert.True(video.IsOpened());
                var fps=video.Get(VideoCaptureProperties.Fps); var count=0; while(video.Read(mat)&&!mat.Empty()) count++;
                Assert.True(count>1500); Assert.Equal(formats[camera.CameraLabel=="Camera 1"?0:1].Width,video.Get(VideoCaptureProperties.FrameWidth));
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(metadataPath)!,camera.ThumbnailRelativePath!)));
                Assert.Equal(devices[camera.CameraLabel=="Camera 1"?0:1].DeviceId,camera.DeviceId);
                clips.Add(new{camera.CameraLabel,Frames=count,PlaybackFps=fps,DurationSeconds=count/fps,Bytes=new FileInfo(path).Length});
            }
            var eventBytes=Directory.GetFiles(options.EventsDirectory,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
            for(var i=0;i<2;i++){await sessions[i].StopAsync(); recordings[i].Stop();}
            Assert.Empty(Directory.GetFiles(options.RollingBufferDirectory,"seg-*.mp4",SearchOption.AllDirectories));
            var report=new {Devices=devices,RequestedModes=formats.Select(f=>f.ToString()).ToArray(),ActualModes=observations.Select(o=>$"{o.Latest!.Width}x{o.Latest.Height}").ToArray(),
                FirstFramesUtc=observations.Select(o=>o.First).ToArray(),AtTriggerFrames=atTrigger,FirstFrameSkewMilliseconds=Math.Abs((observations[0].First!.Value-observations[1].First!.Value).TotalMilliseconds),
                SampledLatestSkewMilliseconds=new{Minimum=skews.Min(),Median=skews.Order().ElementAt(skews.Count/2),Maximum=skews.Max()},
                BaselineCaptureFps=baselineFps,CaptureFps=captureFps,AnalysisFps=analysisFps,AnalysisDrops=analysisDrops,ReadFailures=readFailures,
                SequenceGaps=observations.Select(o=>o.MaxSequenceGap).ToArray(),CpuSeconds=cpu,MeasuredSeconds=elapsed,CpuMachinePercent=100*cpu/elapsed/Environment.ProcessorCount,LogicalProcessors=Environment.ProcessorCount,
                PeakSampledWorkingSetBytes=maxRam,PeakProcessWorkingSetBytes=process.PeakWorkingSet64,MaximumRollingMp4Bytes=maxDisk,EventBytes=eventBytes,
                MaximumTracks=maxTracks,DistinctTrackIds=ids.Select(set=>set.Count).ToArray(),Cycles=cycleReports,Event=saved,Clips=clips,FinalizationSeconds=finalizationSeconds,
                CaptureHardwareDropCount="Unavailable: API exposes delivered frames/read failures, not device-side losses",Calibration="Missing for both; no guessed FOV",HardwareSynchronized=false};
            var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(Path.Combine(root,"validation.json"),json); output.WriteLine(json); output.WriteLine("Evidence: "+root);
        }
        finally
        {
            for(var i=0;i<2;i++){await sessions[i].StopAsync(); sessions[i].Dispose(); Interlocked.Exchange(ref workers[i],null)?.Dispose(); recordings[i].Dispose();}
        }
    }
}
