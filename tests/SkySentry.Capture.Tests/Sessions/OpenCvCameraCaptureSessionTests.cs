using Microsoft.Extensions.Logging.Abstractions;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Cameras;

namespace SkySentry.Capture.Tests.Sessions;

/// <summary>
/// Automated (no-hardware) coverage of <see cref="OpenCvCameraCaptureSession"/>'s
/// error-isolation contract: a missing/invalid device must surface as an
/// <see cref="CameraSessionState.Error"/>/<see cref="CameraSessionState.Disconnected"/>
/// state transition, never an unhandled exception. This is what guarantees one
/// camera's failure can never crash the app or the other camera's session.
///
/// Tests that require an actual physical camera to validate real frame delivery
/// are intentionally NOT here — see docs/CURRENT_STATUS.md for the manual
/// hardware test checklist.
/// </summary>
public class OpenCvCameraCaptureSessionTests
{
    private static readonly CameraDeviceInfo NonExistentDevice = new(
        DeviceId: "test-nonexistent",
        FriendlyName: "Nonexistent Test Device",
        FallbackIndex: 9999,
        SupportedFormats: Array.Empty<CameraCaptureFormat>());

    private static readonly CameraCaptureFormat DefaultFormat = new(640, 480, 30);

    [Fact]
    public async Task StartAsync_ThrowsWhenNoDeviceConfigured()
    {
        using var session = new OpenCvCameraCaptureSession(
            "Test Camera", NullLogger<OpenCvCameraCaptureSession>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
    }

    [Fact]
    public async Task StartAsync_WithNonExistentDevice_TransitionsToErrorOrDisconnected_NeverThrows()
    {
        using var session = new OpenCvCameraCaptureSession(
            "Test Camera", NullLogger<OpenCvCameraCaptureSession>.Instance);

        var terminalStateReached = new TaskCompletionSource();
        session.StateChanged += (_, e) =>
        {
            if (e.NewState is CameraSessionState.Error or CameraSessionState.Disconnected)
            {
                terminalStateReached.TrySetResult();
            }
        };

        session.Configure(NonExistentDevice, DefaultFormat);

        // Must not throw: device-level failures are reported via StateChanged, not exceptions.
        await session.StartAsync();

        var completed = await Task.WhenAny(terminalStateReached.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(terminalStateReached.Task, completed);

        await session.StopAsync();
        Assert.Equal(CameraSessionState.Stopped, session.State);
    }

    [Fact]
    public async Task Configure_ThrowsWhileSessionIsNotStopped()
    {
        using var session = new OpenCvCameraCaptureSession(
            "Test Camera", NullLogger<OpenCvCameraCaptureSession>.Instance);

        var terminalStateReached = new TaskCompletionSource();
        session.StateChanged += (_, e) =>
        {
            if (e.NewState is CameraSessionState.Error or CameraSessionState.Disconnected)
            {
                terminalStateReached.TrySetResult();
            }
        };

        session.Configure(NonExistentDevice, DefaultFormat);
        await session.StartAsync();
        await Task.WhenAny(terminalStateReached.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        // Session is now in a terminal (non-Stopped) state — reconfiguring
        // without an explicit Stop first must be rejected, per the documented
        // "must be called while Stopped" contract.
        Assert.Throws<InvalidOperationException>(() => session.Configure(NonExistentDevice, DefaultFormat));

        await session.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutHavingStarted_DoesNotThrow()
    {
        using var session = new OpenCvCameraCaptureSession(
            "Test Camera", NullLogger<OpenCvCameraCaptureSession>.Instance);

        await session.StopAsync();

        Assert.Equal(CameraSessionState.Stopped, session.State);
    }
}
