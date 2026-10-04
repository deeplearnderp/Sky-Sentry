# Architecture

The existing five production projects remain intact. Core has no WPF/OpenCV
dependency; Capture owns device I/O; Recording owns encoded storage and export;
App composes these services and presents two independent camera panels.
Buffering retains the Phase 1 proof of concept for compatibility and tests.

## Production frame flow

~~~text
DirectShow discovery -> camera selection
OpenCvCameraCaptureSession (one capture loop/clock per camera)
  -> immutable BGR24 CapturedFrame with sequence, monotonic and UTC timestamps
     -> single-slot preview mailbox -> 50 ms WPF render timer
     -> bounded encode channel -> SegmentedCameraRecorder -> rolling MP4 files
Save Event -> SegmentedEventRecorder
  -> atomically snapshot trigger/pin pre-event retention for each active camera
  -> await post-event coverage -> flush open segments
  -> decode/re-encode selected segments -> per-camera MP4 and thumbnail
  -> atomically publish metadata.json -> release retention
~~~

ISegmentedCameraRecorder replaces IFrameBuffer in the production composition
root. CameraPanelViewModel owns capture/preview and one independent recorder.
MainWindowViewModel owns event orchestration and a session-local list capped at
25 recent events. Diagnostics are capped at 200 messages.

The event service admits one event at a time. Each camera finalizes separately;
one camera's failed output does not prevent another from succeeding. State
observers are isolated from orchestration failures. Background awaits do not
depend on the WPF synchronization context.

Camera stop drains recording but retains pinned footage until the event ends.
Restart is refused during that camera's retention hold, avoiding mixing reset
session clocks. Asynchronous window close requests early finalization before
disposing capture and recording. A 15-second timeout keeps the window alive
with its footage preserved, allowing another close attempt.

## Boundaries and future work

Recording configuration is loaded from an optional JSON file at startup.
Runtime data lives outside the source tree. Exclusive ownership locks prevent
another instance from deleting a running recorder's files. See
[Recording pipeline](RECORDING_PIPELINE.md) for resource budgets and recovery.

Phase 3 may insert motion detection, tracking, overlays and angular measurement
after capture. Independent monotonic clocks and approximate UTC timestamps
support later correlation without assuming synchronization. No tracking,
classification, ADS-B, satellite lookup, network station synchronization, or
triangulation is implemented. AnalysisResults is an unused metadata extension.

## Phase 3 architecture

Capture fans out to latest-only preview, segmented recording, then a nonblocking
capacity-one analysis mailbox. CameraAnalysisWorker owns MOG2, TrackEngine and
MotionMeasurementService on a below-normal dedicated thread. Immutable snapshots
cross into WPF; the UI never runs OpenCV or speed formulas. Analysis exceptions
publish an error and leave capture/recording running. Frame dimensions and device
identity must match calibration before angular values are produced.

The UI refreshes preview at 50 ms, bounded track rows at 200 ms, and process/disk
metrics once per second. TrackOverlay maps native coordinates to the preview's
Uniform letterboxed rectangle and clips drawing there. A selected track is gold;
ordinary tracks are aquamarine. Overlay options are independent per camera.

## Dual-camera stabilization

Capture resolves the selected stable DirectShow moniker against current enumeration
at every start. Numeric indexes are transient backend ordinals only. OpenCV DSHOW
has shared native device-registry state; open/configure/release are serialized across
sessions, while frame reads and recording/analysis queues remain independent.
A high-resolution USB stream requests MJPEG after dimensions and FPS negotiation.
No autofocus, exposure, white-balance, USB, privacy or system settings are changed.

Recorder Start optionally accepts source device identity. Event camera results now
include DeviceId and DeviceFriendlyName, including failed contributions. Calibration
profiles are isolated by exact device identity and resolution. GUI headers expose
friendly name, actual dimensions and Missing/Provisional/Configured calibration.
