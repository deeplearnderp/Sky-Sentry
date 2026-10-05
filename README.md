# Sky Sentry

Windows desktop sky-camera capture and evidence recording with .NET 8, WPF,
OpenCvSharp4, DirectShow device discovery, and Serilog.

**Phase 3: monitoring and apparent motion implemented.** Camera discovery,
independent dual preview, duplicate-device prevention, format selection, and
manual Save Event remain available. Motion analysis runs independently of recording.

## Build and run

Requires the .NET 8 SDK on Windows.

~~~powershell
dotnet build SkySentry.sln
dotnet test SkySentry.sln
dotnet run --project src/SkySentry.App/SkySentry.App.csproj
~~~

Open SkySentry.sln in Rider and select the existing **Sky Sentry** Run/Debug
configuration in .run/Sky Sentry.run.xml.

## Recording

Each camera encodes short MP4 segments on disk. Save Event preserves approximately
30 seconds before the latest captured frame and captures 30 seconds afterward.
A newly started camera can provide less pre-event footage. One working camera
is sufficient. Repeated triggers during an active event are rejected.

Runtime logs, settings, buffers, and saved events default to
%LocalAppData%/SkySentry. Optional settings.json configures durations, paths,
and byte budgets; see [Recording pipeline](docs/RECORDING_PIPELINE.md).
The production application does not use the Phase 1 raw-frame history buffer.

## Solution

- Core: camera/frame/timestamp models, recording configuration and interfaces.
- Capture: DirectShow discovery and independent OpenCV capture sessions.
- Buffering: retained Phase 1 in-memory proof of concept and its tests.
- Recording: bounded segment encoding and event assembly.
- App: WPF composition root, camera controls, lifecycle and recent events.
- Analysis: bounded conventional detection, tracking workers and native ROI processing.
- Tests: Core, Capture, Recording, and Analysis projects, all included in the solution.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Current status and validation](docs/CURRENT_STATUS.md)
- [Decisions](docs/DECISIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Camera pipeline](docs/CAMERA_PIPELINE.md)
- [Event format](docs/EVENT_FORMAT.md)
- [Recording pipeline](docs/RECORDING_PIPELINE.md)

Normal automated tests require no camera; opt-in hardware facts return without
exercising devices unless enabled. The Phase 2 C920 test can be enabled with:

~~~powershell
$env:SKYSENTRY_RUN_RECORDING_HARDWARE_TESTS = "1"
dotnet test tests/SkySentry.Recording.Tests --filter FullyQualifiedName~HardwareRecordingTests --logger "console;verbosity=detailed"
Remove-Item Env:SKYSENTRY_RUN_RECORDING_HARDWARE_TESTS
~~~

Sky Sentry does not infer physical speed or distance from a single camera.
Classification, ADS-B, satellite correlation, and triangulation are not implemented.

## Phase 3 monitoring

Phase 3 adds a dark, resizable dual-camera monitoring dashboard, conventional
motion detection, persistent tracks, bounded trails, selected-track detail,
independent overlay switches, and one-second process/disk health updates.
Manual recording remains the only event trigger. No AI classification or external
object matching is implemented; unavailable measurements display `--`.

Analysis is a separate below-normal worker per camera with one pending frame,
640-pixel processing width and a default 10 FPS ceiling. Recording receives each
frame before analysis. Analysis may drop stale frames; it cannot accumulate a
raw-frame history. Device-specific FOV settings are entered under each camera's
calibration expander. They are approximate until measured for the actual optics.

- [Tracking pipeline](docs/TRACKING_PIPELINE.md)
- [Camera calibration](docs/CAMERA_CALIBRATION.md)
- [Measurement model](docs/MEASUREMENT_MODEL.md)

The solution now includes SkySentry.Analysis and SkySentry.Analysis.Tests.
Enable the optional C920 analysis/recording test with
`SKYSENTRY_RUN_ANALYSIS_HARDWARE_TESTS=1`; filter `HardwareAnalysisTests`.
Ordinary tests use synthetic motion and require no camera.

Dual-camera hardware results: [Dual camera validation](docs/DUAL_CAMERA_VALIDATION.md).
