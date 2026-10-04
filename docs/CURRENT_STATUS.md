# Current status

Updated October 4, 2026. Phase 3 is approved. The dual-physical-camera stabilization pass is complete, with the limitations recorded in [DUAL_CAMERA_VALIDATION.md](DUAL_CAMERA_VALIDATION.md).

Camera 1 is HD Pro Webcam C920 at 1920x1080@30; Camera 2 is EMEET PIXY at 1280x720@30. The accepted real event decoded both approximately 61.7/61.5-second clips with full 30-second pre/post coverage, correct device metadata and thumbnails; both recorders stayed healthy. Independent repeated hardware restart cycles and GUI Stop/Start passed. Both FOV calibrations remain Missing.

Stabilization fixes cover stable-identity resolution on every start, serialized native device lifecycle, MJPEG negotiation order, bounded event retention with early release, source identity snapshots, calibration profiles by device/resolution, timestamp/read-failure diagnostics, analysis FPS measurement, and async command availability refresh. Build: zero warnings/errors. Ordinary suite: 96 passed. Enabled dual-camera hardware test passed separately. No staging, commit or push.

Raw final measurements: [dual-camera-final.json](validation/dual-camera-final.json). See the validation report for exact device IDs, advertised modes, FPS/drops, CPU/memory/disk, timing skew, camera-specific constraints, GUI evidence and recommended next step.

## Earlier Phase 2/3 history
Updated October 4, 2026. Phase 2 implementation is complete for review; Phase 3
has not started. Nothing was staged, committed or pushed.

## State found at handoff

Worktree:
C:/Users/OokerDooker/RiderProjects/copilot-worktrees/SkySentry/deeplearnderp-scaling-waffle

Branch: deeplearnderp-phase1-dual-camera-foundation.

The existing Phase 1 capture/preview/discovery/logging/Rider foundation was present.
Phase 2 already contained segment/event recorders, Core recording models, startup
JSON settings, lifecycle/recent-event UI, window-close plumbing, Core recording
math/configuration tests and six native segment tests. The recording test project
was absent from SkySentry.sln. Documentation still described Phase 1.

All solution/source/docs/tests files were already untracked at handoff. This
prevents a meaningful tracked-file diff against Copilot's work; inspection
identified the existing pieces without resetting or staging them.

## Added or corrected

- Include the Recording test project in the full solution.
- Replace the 256-frame queue with eight work items plus a 32 MiB byte budget.
- Add a 256 MiB per-camera rolling disk budget and stop on disk pressure.
- Atomically pin retention at the current received-frame trigger timestamp.
- Flush current segments before finalization, including early shutdown.
- Preserve pinned footage after camera stop and reject restart until release.
- Restart capture and recording together; stop recording on capture failure.
- Restrict recovery/cleanup to reserved segment files; preserve arbitrary files.
- Prevent multiple instances from sharing/deleting live camera buffers.
- Retry stale-file deletion and retain failed deletions in the manifest.
- Isolate lifecycle observers and per-camera flush/output failures.
- Fail missing/unreadable/incompletely decoded footage explicitly.
- Add monotonic timing, playback frame rate and partial-output warnings to metadata.
- Match constant-FPS output duration to captured span.
- Publish metadata using temp file plus rename.
- Await shutdown without blocking WPF and avoid re-entering the Closing callback.
- Preserve Rider configuration and correct read-only recent-event Run bindings.
- Add end-to-end, isolation, overlap, cleanup, budget and hardware regression tests.
- Update all required documentation and add RECORDING_PIPELINE.md.

## Verification

Full solution build: **0 warnings, 0 errors**, eight projects.
Full suite: **60 passed, 0 failed** (Core 39, Capture 5, Recording 16).
The two opt-in hardware facts return without camera work in a normal run, so
58 tests exercise hardware-free paths. The original Phase 1 test files remain.

Native recording tests use synthetic frames and actual OpenCV encoding/decoding.
They cover segment rollover/pruning, retention holds, startup cleanup, ownership,
byte limits, event transitions, serialization, durations, single-camera operation,
dual independent recorders, corrupt/inactive-camera isolation, overlap rejection,
second events, stop/restart protection, shutdown flush and readable output.

Direct GUI launch: SkySentry.App.exe showed a responsive window titled
Sky Sentry and closed with exit code 0 after the asynchronous close fix.
The Rider run configuration remains unchanged; Rider itself was not driven.

## Real hardware validation

Opt-in HardwareRecordingTests passed against the physical **HD Pro Webcam C920**
at 640x480@30. The final run verified:
- Fill the default pre-event buffer.
- Save a full pre/post event and decode all 1,841 output frames.
- Actual pre coverage: 30.00 seconds.
- Actual post coverage: 30.77 seconds.
- Playback length: 61.41 seconds, including whole-segment rounding.
- Metadata and successful per-camera result; zero dropped frames.
- Trigger a second event, request early shutdown after one second, preserve it
  with about 1.01 seconds of post-event coverage and a distinct event ID.
- Stop, restart capture/recording, observe a new segment, stop again.
- No segment files remain after normal shutdown.

Evidence and both self-contained event folders are under:
C:/Users/OokerDooker/AppData/Local/SkySentry/validation/7f440d890e344a158d2c022979da7c37/

validation.json records full timing/outcomes and resource observations:
- 3,841,192 rolling segment bytes at first trigger (about 3.66 MiB).
- 139.4 MiB peak working set for the testhost process.
- 9.67 CPU seconds over 74.54 elapsed seconds (about 13% of one logical core on
  average across capture, encoding, assembly, playback decoding and restart).

These are a short, one-camera VGA test, not a 1080p dual-camera soak benchmark.
Recorded output was decoded through OpenCV; an external Windows player was not
manually exercised. Two distinct physical cameras were unavailable for this test.

## Architecture/resource notes

Default 2-second mp4v/MP4 segments, 30-second pre/post targets, bounded encode
queues and per-camera temporary disk budgets. Final per-camera MP4 is assembled
by decode/re-encode; no new FFmpeg executable/dependency.

Raw BGR24 RAM at 1920x1080, 30 FPS, 30 seconds, two cameras would require
11,197,440,000 bytes (11.20 GB / 10.43 GiB), excluding overhead. Production
stores encoded history on disk instead. Full details and configuration:
[RECORDING_PIPELINE.md](RECORDING_PIPELINE.md).

## Known limitations and exact next step

Review both saved C920 clips in the intended Windows player, then run two distinct
physical cameras simultaneously at target resolution/FPS: warm 30 seconds,
save an event, confirm both clips and per-camera metadata, disconnect one camera,
save another event, and verify restart/shutdown cleanup. Measure CPU/RAM/disk
during a longer 1080p soak before unattended deployment. Then review Phase 2
changes before starting Phase 3.

Output has constant rather than per-frame variable timing; local capture jitter
is averaged even though total duration is preserved. Whole segments round the
requested window. A hard crash may lose the open segment and leave an incomplete
event folder; recovery clears stale rolling files on next camera start but does
not rebuild interrupted events. Native driver/codec calls cannot safely be
force-cancelled; a slow finalization timeout keeps the window and footage alive
for another close attempt. Permanent saved events require user storage management.
Recent events are session-local. No settings UI or automatic reconnect exists.

## Phase 3 implementation and validation (2026-10-04)

Implemented a resizable WPF monitoring dashboard with dual previews, recording
state, active tracks/detail, recent events and compact health. Five independent
preview overlays show boxes, IDs, bounded trails, angular speeds and diagnostics.
Track selection highlights matching preview IDs. Unknown fields display `--`.

Conventional MOG2/contour detection runs separately at bounded resolution/rate.
Prediction/distance/overlap association preserves IDs with missed-frame tolerance.
FOV is persisted by device and actual frame size; all motion uses monotonic timing.
Ignore rectangles apply before contour extraction. Manual events optionally record
track references. Phase 1/2 projects and tests remain; Rider configuration remains.

Real C920 GUI validation: discovery, VGA start, live preview, motion overlays,
settled-track expiration, health and recording were observed. Initial exposure
changes produced small transient false tracks (up to 37); these settled to zero.
Capture remained about 30 FPS with zero reported recording drops. UI actions
responded; no formal latency benchmark was performed. GUI RAM was about 311 MB,
CPU about 9% after settling; startup/noisy scenes reached roughly 16% and 317 MB.
Before pacing refinement analysis averaged 7.4 FPS; stale analysis drops are expected.

First headless C920 run: capture with analysis 30.00 FPS, analysis 6.97 FPS,
1,399 analysis drops, zero recording drops, peak test-host RAM 159 MiB, CPU 7.81
seconds over 62.33 seconds (about 12.5% of one logical core). Decoded saved video
contained 1,841 frames with at least 29 seconds pre-event and 30 seconds post-event.
The 17.2 FPS baseline included device startup and is not a fair throughput comparison.
A revised test excludes startup and a second rate filter has been removed.

Controlled human movement, calibrated real-world angular accuracy, two distinct
physical cameras, outdoor weather and long-duration high-resolution operation were
not verified. Synthetic frames/trajectories cover identity, measurement, ROI and
expiry independently of hardware. Native recording decode validates integrity,
not compatibility with every external player. Final build/test and revised hardware
results are recorded below when completed.

### Final verification

Full Debug solution build: **0 warnings, 0 errors**. Full test suite: **89 passed**
(Core 61, Capture 5, Recording 17, Analysis 6), preserving all prior tests. Three
opt-in hardware facts return without device exercise in the ordinary suite; the
analysis hardware fact was additionally run with its flag and passed.

Revised C920 run, VGA 30 FPS, warm-up excluded from capture comparison:

| Observation | Result |
|---|---:|
| Capture, analysis disabled | 30.00 FPS |
| Capture, analysis enabled | 29.93 FPS |
| Reported analysis average | 8.64 FPS |
| CPU time / analysis-enabled interval | 11.17 s / 63.81 s |
| Peak test-host working set | 159 MiB |
| Stale analysis frames discarded | 1,271 |
| Recording frames dropped | 0 |
| Maximum simultaneous tracks | 34 |
| Distinct IDs observed | 189 |
| Decoded saved-event frames | 1,900 |
| Actual pre/post coverage | 30.00 s / 31.66 s |

Analysis average includes the initial ten-second no-input baseline because its
worker existed before submissions were enabled; active processing is paced at
10 FPS. CPU is about 17.5% of one logical core, not whole-machine CPU percentage.
The headless run has no WPF rendering. GUI observations are reported separately.
Capture losses are not independently measured against a hardware frame counter;
near-30 delivered FPS and zero recording drops are the measured evidence.

The revised scene generated many small/fragmented tracks and one optional track
reference was included at manual trigger with null angular fields. This verifies
metadata integration, not accuracy of every ID. Synthetic tests cap two tracks,
four trajectory points and three measurement samples to verify resource bounds.
GUI overlays and expiration were observed; controlled selection/highlighting,
varied human movement and actual calibrated speeds were not fully verified.

GUI manual Save Event completed (event 352dd8337cb74bb59e5718d7b229fcca), with
Camera 1 successful and Camera 2 unavailable. A virtual-camera attempt reported an
error while C920 capture continued. Closing the running dashboard completed and
no SkySentry.App process remained. Original Rider Run/Debug configuration remains.

Machine-readable hardware evidence: [phase3-c920.json](validation/phase3-c920.json).
All requested docs updated; tracking, calibration and measurement docs added.
Git status/diff-stat inspected: solution files were already untracked at Phase 2
handoff, so diff-stat is empty and cannot represent this phase's changes. Files
were reviewed directly. Nothing staged, committed or pushed; other repositories
and parent Git state were not changed.
