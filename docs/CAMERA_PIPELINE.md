# Camera pipeline

Discovery and capture retain the Phase 1 architecture. DirectShow enumeration
provides friendly names, device monikers, fallback indices and best-effort formats.
Unsupported driver capability enumeration falls back in the UI to 1280x720@30
or 640x480@30. Device identity uses monikers; opening still uses the enumerated
index through OpenCV's DirectShow backend. Advanced camera controls are absent.

Each OpenCvCameraCaptureSession runs its own long-running read task. States are
Stopped, Starting, Running, Error and Disconnected. Ten consecutive read failures
declare a disconnect. Device errors are isolated and logged.

Each frame carries an owned BGR24 byte array, sequence number, monotonic elapsed
time since this capture session started, and approximate UTC reference plus elapsed
time. Independent USB cameras are not assumed to be synchronized. UTC is for
human-readable correlation; segment/event calculations use camera monotonic time.

CameraPanelViewModel fans frames out on the capture thread:
1. Interlocked.Exchange into a single latest-frame preview mailbox.
2. Nonblocking AddFrame into that camera's byte/item-bounded recording queue.

A 50 ms dispatcher timer renders at most one latest frame into a WriteableBitmap.
Recording consumes frames on a separate encode thread. There is no production
raw-frame history. See [Recording pipeline](RECORDING_PIPELINE.md).

Start stops any previous session/recorder, configures the device, starts the
recorder, and then starts capture. Restart stops and recreates both capture and
recording so a reset clock is never mixed with the old recording timeline.
Capture Error/Disconnected stops recording while preserving event-pinned footage.
A restart during retention is refused until the event completes. The duplicate
device guard remains in place, with the existing explicit override.

Capture allocations/native work depend on the negotiated hardware format.
Unexpected delivered dimensions or backward timestamps are dropped by recording
and recorded as a warning. There is no automatic reconnect policy. Measured
recording FPS and dropped-frame counts remain observable through the recorder.

## Phase 3 fan-out

The capture callback publishes latest preview, submits to the existing recording
queue, then submits the same immutable frame to capacity-one analysis delivery.
Analysis never awaits the capture callback. Its older pending frame is replaced
when necessary. Nonincreasing monotonic timestamps are rejected. Real elapsed
pacing caps processing, with native image dimensions retained for measurements.

Starting/restarting or applying calibration resets analysis IDs/history only as
appropriate; applying FOV leaves recording running. Stopping/disconnecting clears
tracks and disposes analysis. No incoming frames for the configured timeout also
clears the last overlay. Device lists are populated independently for both panels;
refresh preserves selected devices and avoids disturbing a running panel.

## Real dual-camera validation

Physical Camera 1: HD Pro Webcam C920. Physical Camera 2: EMEET PIXY.
The EMEET STUDIO Virtual Camera is a separate software device and is not the PIXY.
Stable IDs, advertised formats and actual results are recorded in DUAL_CAMERA_VALIDATION.md.

Initial C920 delivery was 5 FPS at 1080p and 10 FPS at 720p despite requesting 30.
MJPEG selected after dimensions/FPS restored about 30 FPS; the PIXY remained near
29.7 FPS. Reordering is deliberate: OpenCV DSHOW format/FPS setters can rebuild
the graph with the default subtype. See the upstream
[DSHOW implementation](https://github.com/opencv/opencv/blob/4.x/modules/videoio/src/cap_dshow.cpp).
Request acceptance alone is not a throughput guarantee; verify actual delivered FPS.

A native dual-start test crashed before lifecycle serialization was added. Shared
backend lifecycle state is a plausible cause, not a proven crash-dump diagnosis.
Opening/configuring/releasing devices is now serialized; capture reads are not.
Each session reports read failures. Hardware-side missing frames cannot be counted
from the delivered-frame API, and software sequence numbers do not prove no sensor
loss. Latest UTC frame timestamps are exposed as observed skew, explicitly
unsynchronized; they are software delivery timestamps, not shared shutter times.
