# Tracking pipeline

## Detection

CameraAnalysisWorker consumes a capacity-one latest-frame mailbox on a dedicated
below-normal thread. Processing is paced at at most 10 frames/second by default.
Stale pending frames are discarded. Each frame is scaled to at most 640 pixels
wide, converted to grayscale, blurred (5x5), and fed to OpenCV MOG2 (150-frame
background history, variance threshold 25, no shadows). A 3x3 elliptical opening
and closing clean the mask. Ignore regions are zeroed before contour extraction.
Ten processed frames warm the background before detections are returned.

If foreground exceeds 35% of unmasked pixels, suppress the frame as probable global
motion/exposure change. This is a heuristic, not camera stabilization. External
contours must have at least 20 processing pixels and at most 15% of allowed area.
Boxes and centroids are mapped back to native frame coordinates. Confidence is
contour occupancy within its box, not object recognition confidence.

## Association and lifetime

TrackEngine predicts the centroid with last pixel velocity and elapsed monotonic
time. Candidate centers must be within 80 native pixels of that prediction.
Cost is distance / gate + 0.25 * (1 - box intersection-over-union).
Sorted greedy matching is one-to-one; unmatched valid detections create tracks
until the configured capacity. A session GUID prefix and counter identify each
track persistently within that worker's lifetime. Restart/resolution/calibration
changes reset the engine; IDs are not global object identities across cameras.

Five missed processed frames are tolerated; the sixth removes the track.
One second since the last detection also expires it. No input for that timeout
clears stale snapshots. Nonincreasing monotonic frame timestamps are ignored.
Each camera defaults to 64 tracks, 120 trajectory points and 60 measurement samples.
Lifetime distance/peak counters remain available after old samples are trimmed.
Configurable hard limits are 256 tracks, 1,000 trajectory/measurement points and
64 ignore rectangles. Snapshots are copied and bounded; no raw-frame history.

## Configuration and masks

Optional `%LocalAppData%/SkySentry/analysis-settings.json` is read at app startup.
The structure contains `DefaultOptions` and `Cameras` keyed by exact device ID.
Each camera entry contains `Calibration` (nullable) and `Options`. Omitted options
use record defaults. Invalid settings fall back with a diagnostics warning.
Restart the app after hand-editing detection parameters or masks. Calibration
Apply writes this same file atomically and preserves per-device options.

Example default masks, excluding the bottom quarter and left tenth:

```json
{
  "DefaultOptions": {
    "MaxFramesPerSecond": 10,
    "MaxProcessingWidth": 640,
    "MinimumBlobArea": 20,
    "AssociationDistancePixels": 80,
    "MissedFrameTolerance": 5,
    "TrackTimeout": "00:00:01",
    "IgnoreRegions": [
      { "X": 0, "Y": 0.75, "Width": 1, "Height": 0.25 },
      { "X": 0, "Y": 0, "Width": 0.1, "Height": 0.75 }
    ]
  },
  "Cameras": {}
}
```

Rectangle coordinates are normalized [0,1], relative to the full image; each must
fit completely within it. There is no graphical mask editor in this phase.
Masking affects analysis only, never recorded footage or the preview image.

## False positives and limits

Clouds, foliage, precipitation, exposure, shake, insects and compression noise can
create tracks. Large global changes are suppressed, but localized motion remains.
Slow/stationary objects become background. Downsampling/minimum area can miss tiny
or distant targets. Large targets may exceed maximum area. Fragmented silhouettes
can split one object into several tracks. Crossings, occlusion and rapid turns can
lose/exchange IDs. Tune thresholds/masks using representative footage; synthetic
success does not establish outdoor accuracy. No classification or automatic events.
