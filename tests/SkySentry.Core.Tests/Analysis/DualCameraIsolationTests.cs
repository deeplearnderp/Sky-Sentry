using SkySentry.Core.Analysis;
using SkySentry.Core.Cameras;
namespace SkySentry.Core.Tests.Analysis;
public class DualCameraIsolationTests
{
    [Fact]
    public void StableIdentityResolvesReorderedDevicesAndNeverUsesOldOrdinal()
    {
        CameraDeviceInfo[] devices = [new("emeet", "EMEET PIXY", 0, []), new("logitech", "C920", 3, [])];
        Assert.Equal(3, CameraDeviceResolver.ResolveIndex("LOGITECH", devices));
        Assert.Throws<InvalidOperationException>(() => CameraDeviceResolver.ResolveIndex("removed", devices));
    }
    [Fact]
    public void CalibrationProfilesAreIsolatedByDeviceAndResolution()
    {
        var settings = new CameraAnalysisSettings(new("a",640,480,80,60),new())
        { CalibrationProfiles = [new("a",1920,1080,90,55), new("a",1280,720,85,50)] };
        Assert.Equal(90, settings.ForResolution("a",1920,1080)!.HorizontalFovDegrees);
        Assert.Equal(80, settings.ForResolution("a",640,480)!.HorizontalFovDegrees);
        Assert.Null(settings.ForResolution("b",1920,1080));
        Assert.Null(settings.ForResolution("a",3840,2160));
        Assert.True(settings.ForResolution("a",1920,1080)!.IsProvisional);
    }
    [Fact]
    public void TrackHistoriesAndCalibratedAnglesRemainCameraSpecific()
    {
        var first = new TrackEngine("c920","Camera 1",new(),new("c920",100,100,100,100));
        var second = new TrackEngine("pixy","Camera 2",new(),new("pixy",100,100,50,50));
        MotionDetection Detection(double x) => new(new(x,0,10,10),1);
        first.Update([Detection(0)],TimeSpan.Zero,DateTimeOffset.UnixEpoch);
        second.Update([Detection(0)],TimeSpan.Zero,DateTimeOffset.UnixEpoch);
        var a=Assert.Single(first.Update([Detection(10)],TimeSpan.FromSeconds(1),DateTimeOffset.UnixEpoch));
        var b=Assert.Single(second.Update([Detection(20)],TimeSpan.FromSeconds(1),DateTimeOffset.UnixEpoch));
        Assert.NotEqual(a.TrackId,b.TrackId); Assert.Equal("c920",a.CameraId); Assert.Equal("pixy",b.CameraId);
        Assert.Equal(10,a.Measurement!.AngularSpeed); Assert.Equal(10,b.Measurement!.AngularSpeed);
        Assert.NotEqual(a.Trajectory.Last().Centroid,b.Trajectory.Last().Centroid);
    }
}
