# Apparent motion measurements

All frame-to-frame calculations use the capture monotonic elapsed timestamp.
Wall-clock UTC timestamps are display/event context only. Let dt > 0 be seconds
between matched observations and dx, dy be native centroid displacement in pixels.

- Pixel displacement: sqrt(dx^2 + dy^2).
- Pixel velocity vector: (dx/dt, dy/dt), px/s; speed is vector magnitude.
- Horizontal degrees/pixel: horizontal FOV / native frame width.
- Vertical degrees/pixel: vertical FOV / native frame height.
- Approximate angular displacement: sqrt((dx*H/width)^2 + (dy*V/height)^2), degrees.
- Angular speed: angular displacement / dt, degrees/s.
- Average angular speed: accumulated angular path / total matched-track duration.
  This includes elapsed missed intervals; it is not net endpoint displacement.
- Peak angular speed: maximum matched-step angular speed over the track lifetime.
- Angular acceleration: (current angular speed - previous angular speed) / dt,
  degrees/s^2. This is scalar speed change, not full vector acceleration.
- Image direction: atan2(dy, dx), normalized to [0,360), clockwise from image right
  because image Y grows downward. It is not compass bearing. Stationary means unknown.
- Direction change: shortest angle between consecutive nonzero pixel headings,
  [0,180] degrees. The current sample is shown, not an accumulated turning total.
- Apparent dimensions: native bounding-box width, height and area in pixels/pixels^2.
- Apparent area change: (new area - previous area) / previous area (fraction).

Missing FOV means angular displacement/speed/average/peak/acceleration are null
and displayed `--`; pixel speed and image direction remain available. Acceleration
requires two measured speeds; direction change requires two moving headings.
The first observation establishes position, not a fabricated velocity.

Lifetime distances/peaks are running counters; trajectory and measurement histories
are separately bounded. Observed box size is a foreground contour approximation,
so fragmentation/noise can change apparent size without a real change in distance.
Timing, association errors and noisy centroids can produce spikes; no smoothing or
uncertainty estimates are claimed. Linear FOV conversion approximates a rectilinear
view but is not its exact nonlinear perspective projection, particularly at edges.

No MPH/KPH, distance, physical acceleration, object dimensions or extraordinary
interpretation can be inferred from these single-camera apparent measurements.
