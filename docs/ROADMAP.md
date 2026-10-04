# Roadmap

## Phase 1 — Complete

Camera discovery, independent dual-camera sessions/preview, duplicate-device
prevention, resolution/FPS selection, bounded preview delivery, raw rolling-buffer
proof of concept, manual saving, timestamps, logs, shutdown, Rider configuration.
Original Core/Capture tests remain in the solution.

## Phase 2 — Reviewed and approved

Encoded rolling segments, configurable pre/post/segment durations and paths,
byte/item queue bounds, temporary disk budget, retention holds, Save Event,
per-camera MP4/thumbnail/outcome metadata, explicit lifecycle, overlap rejection,
recovery cleanup, asynchronous active-event shutdown and recent-event status.
Automated recording tests and opt-in C920 pre/post hardware validation exist.
See CURRENT_STATUS.md for verified results and practical limitations.

Before signing off deployment, review saved clips in the intended Windows media
player and exercise two distinct physical cameras simultaneously at target formats.
Long-duration high-resolution resource measurements remain a deployment check.

## Phase 3 — Implemented

Monitoring dashboard; motion detection; persistent track IDs; bounded trajectory
trails; camera field-of-view calibration; pixel speed; angular speed and
acceleration; direction changes; active-track panel and overlays.

AI classification, ADS-B, satellite lookup, triangulation, and physical MPH/KPH
are not part of current work. Single-camera footage does not provide distance
or physical speed. Use neutral measurement terminology without UFO/UAP claims.

## Phase 3 implemented

Dashboard, bounded conventional motion analysis, persistent IDs, trajectories,
FOV calibration, apparent/angular measurements, ignore regions, overlays and
manual-event references are implemented. See CURRENT_STATUS.md for measured
validation and remaining hardware checks.

## Recommended Phase 4: evidence review and measurement validation

Add saved-event playback with synchronized metadata/track inspection, a graphical
ignore-region editor, and an explicit camera calibration workflow (measured FOV,
then distortion/projection calibration). Tune false-positive handling with outdoor
recordings and repeatable crossing/occlusion scenes. Add long-duration dual-physical-
camera recording/analysis soak validation at target resolutions. Keep automatic
triggering disabled until noise rates and bounded retention are demonstrated.
AI classification, ADS-B, satellite matching and triangulation require separate
approval after this evidence/measurement baseline; do not infer physical speed.

Dual-camera stabilization is a validation pass between Phases 3 and 4. The
recommended next feature phase remains evidence playback/metadata inspection,
measured calibration, graphical masks and controlled outdoor noise evaluation,
followed by a sustained two-camera soak at the chosen capture modes.
