# Camera calibration

CameraCalibration stores DeviceId, FrameWidth, FrameHeight, HorizontalFovDegrees,
VerticalFovDegrees and ProjectionModel (`LinearFovApproximation`). Both FOVs must
be finite, greater than zero and less than 180 degrees; dimensions must be positive.
The calibration must match the exact capture device and actual frame dimensions.
A mismatch leaves angular values unavailable while pixel motion remains measurable.

Select the desired capture format, expand Camera calibration & options, enter H/V
FOV and Apply. While running, actual preview dimensions are used. Values persist
per device in `%LocalAppData%/SkySentry/analysis-settings.json`. Applying calibration
resets current analysis tracks/metrics to avoid mixing projection units and leaves
recording running. Changing capture resolution requires a matching calibration;
up to 16 resolution profiles per device are stored after the dual-camera stabilization pass.

Manufacturer FOV is a starting estimate, not measured truth. Measure the installed
camera/optics and selected crop/zoom mode with known angular references. Report
angular values as approximate until independently checked. FOV does not provide
object distance, physical dimensions or physical speed.

IAngularProjection separates conversion from tracking and WPF. A future projection
can replace the linear model with intrinsic calibration, undistortion and angular
rays. No distortion coefficients, perspective correction, calibration-target wizard
or uncertainty intervals are implemented. Wide lenses and edge movement are
particularly inaccurate with uniform degrees-per-pixel scaling.

## Separate resolution profiles and provenance

Each per-device settings entry now has CalibrationProfiles, up to 16 distinct
resolution profiles. The old Calibration field is retained as a backwards-compatible
fallback and the most recently saved profile. Lookup always checks stable device ID
and width/height; a profile from another camera or resolution is never borrowed.
Saving one resolution preserves others and the device's own detection options.

Each calibration has IsProvisional (default true). The GUI checkbox `FOV independently
measured` sets it false only when the operator explicitly applies measured values.
Headers display Missing, Provisional, or Configured. Configured means operator-marked
FOV, not a distortion calibration or independent accuracy certification. With no
verified FOV for either attached camera, hardware tests leave both Missing and angles
unknown. Synthetic tests use intentionally different FOVs to verify isolation.
