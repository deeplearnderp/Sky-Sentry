# Recording pipeline

Implemented by SegmentedCameraRecorder and SegmentedEventRecorder. The WPF app
uses this pipeline; InMemoryFrameBuffer remains a historical tested component.

## Why encoded segments

BGR24 raw storage at 1920 x 1080, 30 FPS, 30 seconds, two cameras:

~~~text
1920 x 1080 x 3 = 6,220,800 bytes/frame
6,220,800 x 30 x 30 x 2 = 11,197,440,000 bytes
= 11.20 decimal GB = approximately 10.43 GiB
~~~

This excludes array/object overhead, preview buffers and native encoder memory.
A raw 30-second history is unsuitable. The original 256-frame encode queue
alone could retain 1.48 GiB per camera; production now uses at most eight queued
work items and a 32 MiB queued-frame byte budget per camera. A 1080p camera can
queue at most five full frames under that default. One encoder frame and
preview/capture frames can additionally be alive, as can native codec buffers;
this budget is not a cap on total process working set.

## Segments and retention

Default segments span 2 seconds of camera-monotonic capture time. Segment files
are mp4v video in MP4 containers, encoded by the existing OpenCvSharp dependency.
No FFmpeg executable or new video dependency is required.

Files reside below:
<buffer-root>/.skysentry-segments/Camera_1/seg-000000000000.mp4
(and independently Camera_2). A .owner.lock is held exclusively for each camera.
Only seg-*.mp4 files in those reserved directories are cleaned. Arbitrary sibling
files/directories are preserved; cleanup never recursively deletes a configured
directory.

Normal pruning retains the configured pre-event window plus one segment margin.
Trigger capture and retention pinning share the same recorder lock as pruning,
so the pre-event floor cannot race deletion. The trigger point is the latest
received frame timestamp, including the open segment and dropped frames, rather
than the previous finalized segment. Post-event completion checks closed segment
coverage every 250 ms. Deadline uses Stopwatch, with a segment-duration allowance
and 15 seconds of safety grace. A stopped/failed camera does not block the other.

An eight-item channel uses nonblocking TryWrite. If either its item capacity or
byte budget is full, AddFrame drops the new frame and increments DroppedFrameCount.
Format mismatches or backward timestamps are rejected and surfaced as warnings.
Encoding errors stop recording and log a reason; preview can continue independently.

## Temporary disk budget

MaxBufferBytesPerCamera defaults to 268435456 (256 MiB), or 512 MiB for two
cameras. Before encoding each frame, the recorder measures segment files and
stops at budget minus an 8 MiB reserve for an encoded frame/container trailer.
Closed segments are checked against the full budget; a newest segment that
exceeds it is removed and recording fails. Native writer buffering means this
is an application storage bound with a closing check, not an OS filesystem quota.

This also bounds an event retention hold while finalization is slow. Deletion
failures retain manifest entries so later pruning retries them; disk pressure
eventually stops encoding instead of growing storage indefinitely. A failure to
remove files can leave a bounded stale buffer requiring filesystem repair.
Saved event folders are permanent user evidence and are not automatically pruned.

## Finalization, timing and output

The encoder accepts a flush command through its queue, closing the current file
after preceding frames. Event assembly reads selected closed segments in order
and decodes/re-encodes one frame at a time into Camera_1.mp4 / Camera_2.mp4.
Missing, unreadable or incompletely decoded selected segments fail that camera
rather than silently claiming complete coverage. Thumbnails are taken near the
trigger segment. Metadata is published using metadata.json.tmp plus a final rename.

Selection includes whole overlapping segments, so output may include up to roughly
one extra segment on either side. ActualPreEventDuration is capped at the requested
pre duration; ActualPostEventDuration reports the selected span after the trigger.
RecordingStart/EndMonotonic and UTC timestamps expose the actual selected boundaries.

OpenCV produces constant-FPS video. Final output FPS equals selected frame count
divided by the monotonic span plus one nominal frame interval. This preserves
overall captured duration when the delivered frame rate differs from the requested
rate. It does not preserve individual irregular frame presentation times: local
jitter/gaps are averaged across the clip. PlaybackFrameRate records that rate.
These timestamps describe capture coverage, not synchronized dual-camera frames.

## Lifecycle, overlap and shutdown

Idle -> Triggered -> CapturingPostEvent -> Finalizing -> Completed or Failed -> Idle.
One successful camera completes the event. Every configured camera gets an outcome,
including inactive cameras. A failed encoder can still supply a partial clip from
closed segments, with a Warning. A second simultaneous trigger throws
EventAlreadyInProgressException; it is not queued or merged.

Camera stop drains the encode queue and closes the active segment. Pinned segments
survive stop; restarting that camera is refused until retention releases. Normal
stop clears owned segment files. Window close awaits early event finalization and
then disposes cameras. A close attempt waits up to 15 seconds; if still finalizing,
the window stays available and the buffer remains pinned for a later close attempt.
Native driver/codec calls cannot be forcibly interrupted safely in-process.

On the next camera Start, exclusive ownership is acquired and stale reserved
segments from an abnormal termination are deleted. An interrupted final event
folder is retained for inspection; the app does not reconstruct it or resume
post-event capture after a crash. Recent events are session-local, not scanned
from disk on startup.

## Configuration

Optional %LocalAppData%/SkySentry/settings.json, read once at startup:

~~~json
{
  "PreEventDurationSeconds": 30,
  "PostEventDurationSeconds": 30,
  "SegmentDurationSeconds": 2,
  "RollingBufferDirectory": "C:\Users\YOUR_USER\AppData\Local\SkySentry\buffer",
  "EventsDirectory": "C:\Users\YOUR_USER\AppData\Local\SkySentry\Events",
  "MaxBufferBytesPerCamera": 268435456,
  "MaxQueuedFrameBytes": 33554432
}
~~~

Omitted fields use defaults. Invalid JSON/configuration logs a warning and falls
back to defaults. Durations must be positive; pre/post are limited to 10 minutes,
segments to 30 seconds and no longer than pre-event duration. Queue byte budget
must be 1 KiB to 128 MiB; disk budget at least 16 MiB. Buffer/event paths must be
separate, neither equal nor nested. Start validates frame size against the queue
budget. Paths should point to dedicated runtime directories.

## Validation

Core tests cover duration/window selection and configuration validation.
Recording tests exercise actual native encode/decode, retention/recovery, cleanup,
queue pressure, disk pressure, exclusive ownership, metadata, event lifecycle,
one-camera success, failed-camera isolation, overlap, repeated saves and shutdown.
See CURRENT_STATUS.md for actual hardware evidence and remaining limitations.

## Phase 3 recording priority

Analysis adds no work to the encoder or event assembly beyond taking an optional
bounded track-reference snapshot at manual trigger. Each capture callback enqueues
recording before handing the frame to independent latest-only analysis. Disabling
analysis, toggling overlays or changing FOV does not interrupt the rolling recorder.
Analysis failures do not stop capture or recording. Overlay graphics are preview
only and are not burned into evidence video. Recording byte/item/disk limits and
pre/post retention remain those of Phase 2. CPU saturation can still affect the
whole process: validate target resolution, both cameras and long runs on deployment
hardware rather than inferring unlimited capacity from a VGA test.

## Dual-camera stabilization pass

Full pre/post event and native decode validation are repeated for the two physical
cameras at 1080p. Source identity is persisted with each contribution. Each recorder
retains its bounded queue and disk budget; analysis remains latest-only and discardable.
Detailed resource and timing results are in DUAL_CAMERA_VALIDATION.md. The headless
hardware harness checks two stop/restart cycles per camera and verifies the other
continues delivering during each stop; GUI preview/lifecycle is checked separately.

Event retention now pins the bounded pre/post interval plus current rolling footage, rather than every later segment during finalization. Completed contributions release their holds promptly. The original 256 MiB per-camera limit remains; higher bitrates may still require an explicit budget adjustment or lower mode.
