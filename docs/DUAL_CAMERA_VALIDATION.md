# Dual-camera validation

Physical cameras enumerated through DirectShow on 2026-10-04. Stream start re-resolves stable identity; numeric ordinals are temporary.

## Camera 1: HD Pro Webcam C920

Stable identifier:

```text
@device:pnp:\\?\usb#vid_046d&pid_082d&mi_00#a&2aa7f292&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global
```

| Advertised resolution | Advertised FPS |
|---|---|
| 160×90 | 30 |
| 160×120 | 30 |
| 176×144 | 30 |
| 320×180 | 30 |
| 320×240 | 30 |
| 352×288 | 30 |
| 432×240 | 30 |
| 640×360 | 30 |
| 640×480 | 30 |
| 800×448 | 30 |
| 800×600 | 24, 30 |
| 864×480 | 24, 30 |
| 960×720 | 15, 30 |
| 1024×576 | 15, 30 |
| 1280×720 | 10, 30 |
| 1600×896 | 7.5, 30 |
| 1920×1080 | 5, 30 |
| 2304×1296 | 2 |
| 2304×1536 | 2 |

## Camera 2: EMEET PIXY

Stable identifier:

```text
@device:pnp:\\?\usb#vid_328f&pid_00c0&mi_00#9&59a0e33&1&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global
```

| Advertised resolution | Advertised FPS |
|---|---|
| 640×360 | 30 |
| 640×480 | 30 |
| 800×600 | 30 |
| 960×720 | 30 |
| 1024×576 | 30 |
| 1280×720 | 30 |
| 1280×960 | 30 |
| 1920×1080 | 30 |
| 2560×1440 | 30 |
| 3840×2160 | 30 |

Advertised formats are driver capability snapshots, not guarantees. EMEET STUDIO Virtual Camera is a separate software device; the physical model is exactly **EMEET PIXY**.

## Tested modes and fixes

| Simultaneous request | Initial C920 / PIXY FPS | Corrected negotiation |
|---|---|---|
| 1920×1080 @ 30 | 5.00 / 29.89 | 30.00 / 29.70 |
| 1280×720 @ 30 | 10.30 / 30.00 | 30.00 / 29.70 |

Final selected modes: **C920 1920×1080 @ 30 FPS; EMEET PIXY 1280×720 @ 30 FPS**.
Requesting MJPEG after dimensions and FPS restored the C920's advertised high-rate
mode. Initial rates matched its uncompressed modes; no measured USB-controller
topology or bandwidth saturation diagnosis is claimed. No USB settings changed.

A native simultaneous-start test crashed once. Device opening/configuration/release
now share a lifecycle gate because OpenCV DSHOW has shared backend state. Reads,
recording and analysis remain independent. Shared-state contention is a plausible
cause, not a crash-dump-confirmed diagnosis. Repeated restart results follow below.

Test segment paths initially reached 260 characters and native writers failed.
Hardware footage now uses an ignored short `.validation-dual/<run-id>` directory
inside this worktree. Writer errors include path and length for diagnosis. The app's
normal LocalAppData paths are shorter. Long custom video paths remain a limitation.

The first successful full event decoded both clips but the PIXY exhausted its
256 MiB rolling budget during 47.58-second finalization: retention incorrectly pinned
all later footage. Retention now preserves only the requested event interval plus
the latest rolling interval, releases each completed camera contribution promptly,
and retains the original byte cap. A synthetic regression tests deletion of the
unneeded interval while preserving event and live segments. Final hardware acceptance
also requires both recorders still running and no metadata warning after saving.

Calibration is **Missing** for both cameras because FOV is not independently known.
No manufacturer FOV was invented. Separate device/resolution profiles and a
Provisional indicator are implemented; synthetic tests use different FOVs to verify
isolation. Configured means operator-marked measured FOV, not distortion calibration.

No autofocus/exposure/white-balance/system/privacy settings or room-specific detector
thresholds were changed. Detection options remain independently configurable per
device. False-positive rates require controlled outdoor footage before tuning.

## Reproduce

```powershell
$env:SKYSENTRY_RUN_DUAL_HARDWARE_TESTS = '1'
dotnet test tests/SkySentry.Analysis.Tests --filter FullyQualifiedName~BothPhysicalCameras --logger 'console;verbosity=detailed'
Remove-Item Env:SKYSENTRY_RUN_DUAL_HARDWARE_TESTS
```

Use `SKYSENTRY_RUN_DUAL_PROBE=1` and filter `ProbeDual1080` for 1080p/720p delivery
checks. Hardware facts are opt-in and do not exercise cameras in ordinary test runs.

The bounded-retention 1080p repeat still exhausted the PIXY's 256 MiB budget in this
scene during finalization. Its saved clip was 156.9 MiB, compared with 63.7 MiB for
the C920. This is an encoded-bitrate/retention constraint, not proof of USB overload.
The selected PIXY mode is therefore 720p30; C920 stays 1080p30. These are independent
operator selections, not automatic model-name-specific defaults. Higher-resolution
PIXY recording requires an explicitly larger budget and another soak measurement.

## Accepted hardware run

Evidence: [dual-camera-final.json](validation/dual-camera-final.json); original decoded footage and thumbnails remain in ignored `.validation-dual/782702c4`. Earlier reports are diagnostic attempts, not acceptance results.

| Measurement | Camera 1: C920 | Camera 2: PIXY |
|---|---:|---:|
| Selected mode | 1920x1080 @ 30 | 1280x720 @ 30 |
| Baseline delivered capture FPS | 29.92 | 29.72 |
| Analysis-enabled capture FPS | 28.91 | 29.69 |
| Analysis FPS | 9.35 | 9.34 |
| Stale analysis frames dropped, since start | 1,870 | 1,945 |
| Capture Read failures / observed sequence gaps | 0 / 0 | 0 / 0 |
| Recorder queue frame drops, since start | 1 | 0 |
| Maximum simultaneous tracks | 30 | 64 |
| Decoded event frames | 1,745 | 1,825 |
| Actual clip duration | 61.70 s | 61.52 s |
| Pre / post-trigger coverage | 30.00 / 31.47 s | 30.00 / 30.48 s |
| Clip bytes | 66,790,336 | 67,320,169 |

Device-side capture losses are unavailable; zero Read failures and sequence gaps do not establish zero camera/driver losses. Recording drops are separate from intentional stale-analysis discards. Both recorders remained healthy after event finalization; metadata had no warning. Both clips were fully decoded, thumbnails checked, and stable source identities verified. Existing synthetic failure tests verify that one failed camera does not invalidate the other.

Average CPU was 18.98% of eight logical processors across 95.57 s. Peak sampled working set was 379.38 MiB; process peak working set 395.46 MiB. Maximum sampled rolling MP4 usage during the pre-trigger observation was 77.41 MiB (not a measurement of maximum pinned usage during finalization). Total event output including metadata/thumbnails was 128.41 MiB. Finalization took 28.66 s. Analysis queues remain bounded and discard stale work first.

Each camera completed two hardware stop/restart cycles, taking 6.77-6.85 s per cycle including the observation interval. The other camera delivered 53-55 frames during each stop interval. Rolling files were cleared on normal stop. No analysis worker error was reported.

## Observed timing (UTC)

| Timestamp | C920 | PIXY |
|---|---|---|
| First frame | 23:15:40.7531557 | 23:15:42.3397837 |
| Latest frame at trigger | 23:16:51.0106521 | 23:16:51.0006102 |
| Recording start | 23:16:20.8214247 | 23:16:19.9921347 |
| Recording end | 23:17:22.4834444 | 23:17:21.4805954 |

Trigger: 2026-10-04 23:16:51.0247074 UTC. First-frame startup difference was 1,586.63 ms. Sampled latest-frame timestamp skew was 4.15 / 10.62 / 35.99 ms minimum / median / maximum; about 10.04 ms at trigger. Clip start/end differences were about 829.29 / 1,002.85 ms because independent segment boundaries include extra footage. These are host receipt timestamps and segment observations, not shutter synchronization or hardware timestamps. Cameras remain unsynchronized.

## GUI and tracking review

Both distinct previews visibly updated with correct friendly names, actual dimensions, Missing calibration, rolling state and near-30 FPS. UI interactions remained responsive. GUI CPU observations ranged roughly 12-27%, working set roughly 435-518 MB. After correcting async command invalidation, Stop enabled Start and disabled Stop/Restart correctly for each camera; Start recovered each while the other preview/recorder continued. Closing with both running completed cleanly. Reopening after an earlier active-camera close and starting both again demonstrated device release. Final close left no SkySentry.App process.

Real analysis produced separate camera-associated trajectories and globally unique IDs with no sampled collision. Synthetic tests explicitly cover independent histories and different per-camera FOV profiles. The accepted run left angular fields empty because calibration is Missing. Both settled GUI views eventually showed zero active tracks. The uncontrolled hardware scene also produced many fragmented/short tracks (151 / 581 distinct IDs), especially on PIXY. Exposure, focus, flicker and genuine movement were not isolated, so their individual false-positive contribution is unmeasured. No room-specific thresholds were changed.

Selected-track camera mapping and highlight routing were inspected in source; an active-track detail selection was not exercised in the settled GUI. Overlay motion/angle accuracy and false-positive rates still need controlled moving-target footage and measured FOV. No calibrated real-world angular-accuracy claim is made.

## Final verification and next step

Solution build succeeded with zero warnings/errors; all 96 ordinary tests passed (hardware facts are opt-in and return without camera access when disabled). The enabled dual hardware acceptance test passed separately in about 2m19s, and 1080p/720p mode probes ran separately. Added coverage includes identity reorder/missing-device behavior, calibration and track isolation, dual metadata, and bounded event retention.

Source and documentation were reviewed directly. Git status still shows the solution/source/docs/tests as untracked from the handoff; tracked diff --stat is empty. Nothing was staged, committed or pushed.

Next: a sustained dual-camera outdoor soak, measured FOV per selected resolution, and controlled exposure/focus/motion samples before detector tuning or the next playback/mask feature phase. Native long-path writer support, larger-budget PIXY 1080p recording and USB-controller topology remain unverified.
