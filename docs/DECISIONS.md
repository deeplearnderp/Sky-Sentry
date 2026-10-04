# Decisions

## Preserved Phase 1 foundation

.NET 8/WPF, OpenCvSharp4, DirectShow enumeration and Serilog remain. Core stays
free of UI/native video dependencies. Two independent capture sessions retain
device selection, format selection, dual preview, duplicate-device prevention,
structured logs, and independent monotonic plus approximate UTC timestamps.
The existing Rider Run/Debug configuration remains unchanged. Composition is
manual; no dependency-injection container is added.

## Phase 2 recording

Use short encoded disk segments instead of raw RAM history. At 1080p30, a
30-second BGR24 history for two cameras costs 11,197,440,000 bytes (10.43 GiB)
before overhead. Retain the old buffer project/tests as a proof of concept;
production uses ISegmentedCameraRecorder.

Bound queued frames by both bytes (32 MiB per camera default) and count (eight).
Use nonblocking writes with explicit drop counting. Encoding stays off capture
and UI threads. Bound temporary segment storage by retention time and a byte
budget, including during active retention holds. Stop recording on budget
exhaustion instead of sacrificing pinned event footage without reporting it.

Use reserved subdirectories and exclusive ownership locks. Recovery/cleanup
deletes only owned segment filenames, preserving unrelated content. Retry failed
pruning rather than forgetting files that remain on disk. Reject overlapping
buffer/event paths. Saved user events do not undergo automatic deletion.

Keep mp4v/MP4 and OpenCV. Assemble by streaming decode/re-encode rather than
introducing FFmpeg deployment/container concatenation dependencies. This costs
an extra encode pass and generation loss but gives single playable camera clips.
Use capture-span-derived output FPS to preserve total duration; per-frame jitter
is not represented exactly by the constant-FPS output.

Reject overlapping event triggers. One active event is easy to bound and test;
there is no merge, queue or indefinite post-event extension. An inactive/failed
camera cannot prevent a successful camera from saving. Atomic trigger/pinning
protects segments from pruning races. Flush open files before finalizing.

Camera stop preserves event pins; restart during a pin is refused. Reset clocks
must not share old segment manifests. Window closing is asynchronous and requests
early finalization before disposal. A timeout preserves the active window and
buffers for a later close attempt. Unsafe forced termination of native calls is
not attempted.

Record actual camera outcomes and timing, not a promised 30 seconds after short
warmup/shutdown. Publish metadata via a temp file and rename. Crash recovery
clears stale rolling files on the next camera start; it does not reconstruct
incomplete saved events. The existing recent-event UI is limited to 25 session
entries and does not require a database.

Configuration is a startup JSON override, validated with safe fallback and logs.
A settings editor, persistent camera selection and analysis remain future work.

## Phase 3 decisions

- Choose adaptive MOG2 background subtraction over plain consecutive-frame
  differencing: it provides a simple maintained background and avoids treating
  every changed pixel as a new object. Grayscale blur, morphology and external
  contours remove some noise. Shadows are disabled. This is motion evidence,
  not semantic object detection.
- Use predicted centroid distance plus overlap for deterministic greedy one-to-one
  association; no training/model service dependency. Crossing objects and abrupt
  turns can still exchange or lose IDs.
- Bound pending analysis, tracks, histories and UI snapshots. Recording submission
  precedes analysis and workers run below normal. This reduces contention without
  promising hard real-time scheduling of native OpenCV threads.
- Keep calibration explicit and resolution-specific; missing/mismatched FOV means
  unknown angular values, never guessed physical speed. Projection is replaceable.
- Snapshot optional track references at manual event trigger. Provider failures
  are logged and cannot prevent evidence recording. No automatic triggers.

## Dual-camera stability acceptance

The stabilization pass exercises HD Pro Webcam C920 and EMEET PIXY together at
1920×1080 @ 30 FPS. Verify continuous rolling recording through event finalization,
not merely playable saved clips. Keep distinct physical source IDs in metadata,
repeat independent stop/restart, and report delivered timestamps as unsynchronized.
See DUAL_CAMERA_VALIDATION.md for measurements and remaining deployment checks.
