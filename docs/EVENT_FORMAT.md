# Event format

SegmentedEventRecorder writes self-contained packages:

~~~text
<events-root>/YYYY/MM/DD/<full-guid-event-id>/
  Camera_1.mp4
  Camera_1-thumbnail.jpg
  Camera_2.mp4                 if this camera succeeded
  Camera_2-thumbnail.jpg       if this camera succeeded
  metadata.json
~~~

The root defaults to %LocalAppData%/SkySentry/Events; date directories use trigger
UTC. Video is MPEG-4 Part 2 (mp4v) inside MP4, assembled by OpenCV. This retains
the Phase 1 codec dependency. OpenCV decode validation is covered; compatibility
with a particular external Windows player must still be checked on that machine.
Temporary rolling segments are not referenced by the completed event.

Metadata is System.Text.Json output with PascalCase fields and TimeSpan strings.
The Phase 1 metadata shape is superseded by the Cameras array; old event folders
are not migrated. The current schema includes:
- EventId, TriggeredAtUtc, CreatedAtUtc, ApplicationVersion.
- RequestedPreEventDuration, RequestedPostEventDuration.
- Cameras: one CameraRecordingResult for each configured recorder.
- AnalysisResults: empty; no analysis runs in Phase 2.

Per-camera fields:
- CameraLabel, Success, FailureReason (null on success).
- VideoRelativePath, ThumbnailRelativePath (null on failure).
- Width, Height, RequestedFps, AchievedFps, PlaybackFrameRate.
- ActualPreEventDuration, ActualPostEventDuration.
- RecordingStartUtc, RecordingEndUtc.
- TriggerMonotonic, RecordingStartMonotonic, RecordingEndMonotonic.
- DroppedFrameCount (cumulative for this recording session).
- Warning (optional encoder/format issue when partial footage was still saved).

Actual durations describe the selected captured span, with segment-boundary
rounding and potentially short warmup/post-event shutdown coverage. Pre coverage
is capped at the requested pre duration; post coverage may extend beyond target
by a segment. Monotonic values belong only to that camera's capture session.
The cameras are not synchronized.

One successful camera makes the overall event Completed; other camera failures
are retained in metadata. No active camera produces failure metadata. Segment
read failures fail that camera; an incomplete/unusable output may remain for
inspection but is not referenced as successful footage. A filesystem failure
writing metadata raises an event error. Complete metadata is published by rename
from metadata.json.tmp; a crash can leave an incomplete folder/tmp file.

Output uses constant FPS derived from frame count and the captured span. Thus
overall playback duration matches the recorded interval, while irregular frame
timings are averaged. See [Recording pipeline](RECORDING_PIPELINE.md).

## Phase 3 optional motion references

Metadata adds `TriggerReason` (currently `Manual Save Event`) and `AssociatedTracks`.
Each reference contains TrackId, CameraId, CameraLabel, TriggerReason, Duration,
PeakAngularVelocity and DirectionChangeDegrees. Angular fields are nullable when
uncalibrated. These are bounded snapshots at trigger time, not a complete event
trajectory or guaranteed identification of every object in the clip.
`AnalysisResults` remains the legacy empty field. Video and thumbnail assembly,
per-camera failure results and segment retention are unchanged. Track provider
errors produce an empty reference array and a warning; saving proceeds.

## Dual physical camera identity

Every CameraRecordingResult now optionally includes DeviceId and DeviceFriendlyName.
They identify the source supplied when its recorder started, rather than inferring a
physical camera from Camera 1/2 or a numeric OpenCV index. Legacy/synthetic callers
that provide no identity serialize null. A failed contribution retains its identity
and failure reason while successful contributions are preserved.

Per-session monotonic epochs are independent; do not subtract cross-camera monotonic
values as synchronization evidence. Compare approximate UTC delivery timestamps for
observed skew only. Clips retain separate playback frame rates and capture timestamps;
no synchronized frame pairing, shutter metadata or stereo triangulation is claimed.
