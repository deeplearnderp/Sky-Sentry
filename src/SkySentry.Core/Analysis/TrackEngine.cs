namespace SkySentry.Core.Analysis;

/// <summary>Bounded, deterministic greedy association by predicted centroid distance and overlap.</summary>
public sealed class TrackEngine
{
    private sealed class Track
    {
        public required string Id;
        public required DateTimeOffset CreatedUtc;
        public required TimeSpan Created;
        public DateTimeOffset LastUtc;
        public TimeSpan Last;
        public PixelBox Box;
        public double Confidence;
        public int Missed;
        public MotionMeasurement? Motion;
        public double PixelPath;
        public double? AngularPath;
        public double? Peak;
        public Queue<TrajectorySample> Trail = new();
        public Queue<MotionMeasurement> History = new();
    }
    private readonly AnalysisOptions _options;
    private readonly MotionMeasurementService _measurements;
    private readonly string _cameraId, _label, _sessionPrefix;
    private readonly List<Track> _tracks = [];
    private CameraCalibration? _calibration;
    private long _nextId;
    private TimeSpan? _lastFrame;
    public TrackEngine(string cameraId, string label, AnalysisOptions options, CameraCalibration? calibration = null, MotionMeasurementService? measurements = null)
    {
        options.Validate();
        calibration?.Validate();
        _cameraId = cameraId; _label = label; _options = options;
        _calibration = calibration; _measurements = measurements ?? new();
        _sessionPrefix = Guid.NewGuid().ToString("N")[..8];
    }
    public IReadOnlyList<TrackSnapshot> Update(IReadOnlyList<MotionDetection> detections, TimeSpan monotonic, DateTimeOffset utc)
    {
        if (_lastFrame is { } last && monotonic <= last) return Snapshot();
        _lastFrame = monotonic;
        _tracks.RemoveAll(t => monotonic - t.Last > _options.TrackTimeout);
        var limited = detections.Where(d => d.Bounds.Width > 0 && d.Bounds.Height > 0 &&
            double.IsFinite(d.Bounds.X + d.Bounds.Y + d.Bounds.Area + d.Confidence)).OrderByDescending(d => d.Bounds.Area).Take(_options.MaxTracks).ToArray();
        var pairs = new List<(double Cost, int Track, int Detection)>();
        for (var t = 0; t < _tracks.Count; t++)
        {
            var track = _tracks[t];
            var dt = (monotonic - track.Last).TotalSeconds;
            var predicted = new PixelPoint(track.Box.Center.X + (track.Motion?.VelocityX ?? 0) * dt,
                track.Box.Center.Y + (track.Motion?.VelocityY ?? 0) * dt);
            for (var d = 0; d < limited.Length; d++)
            {
                var distance = predicted.DistanceTo(limited[d].Bounds.Center);
                if (distance <= _options.AssociationDistancePixels)
                    pairs.Add((distance / _options.AssociationDistancePixels + 0.25 * (1 - track.Box.IntersectionOverUnion(limited[d].Bounds)), t, d));
            }
        }
        var usedTracks = new HashSet<int>();
        var usedDetections = new HashSet<int>();
        foreach (var pair in pairs.OrderBy(p => p.Cost).ThenBy(p => p.Track).ThenBy(p => p.Detection))
        {
            if (usedTracks.Contains(pair.Track) || usedDetections.Contains(pair.Detection)) continue;
            usedTracks.Add(pair.Track); usedDetections.Add(pair.Detection);
            var track = _tracks[pair.Track];
            var detection = limited[pair.Detection];
            var motion = _measurements.Measure(track.Box, detection.Bounds, monotonic - track.Last, _calibration, track.Motion);
            track.Motion = motion;
            track.PixelPath += motion.PixelDisplacement;
            if (motion.AngularDisplacement is { } angular) track.AngularPath = (track.AngularPath ?? 0) + angular;
            if (motion.AngularSpeed is { } speed) track.Peak = Math.Max(track.Peak ?? 0, speed);
            track.Box = detection.Bounds; track.Confidence = Math.Clamp(detection.Confidence, 0, 1);
            track.Last = monotonic; track.LastUtc = utc; track.Missed = 0;
            track.Trail.Enqueue(new(detection.Bounds.Center, monotonic));
            track.History.Enqueue(motion);
            while (track.Trail.Count > _options.MaxTrajectoryPoints) track.Trail.Dequeue();
            while (track.History.Count > _options.MaxMeasurementSamples) track.History.Dequeue();
        }
        for (var t = 0; t < _tracks.Count; t++) if (!usedTracks.Contains(t)) _tracks[t].Missed++;
        _tracks.RemoveAll(t => t.Missed > _options.MissedFrameTolerance);
        for (var d = 0; d < limited.Length && _tracks.Count < _options.MaxTracks; d++)
        {
            if (usedDetections.Contains(d)) continue;
            var detection = limited[d];
            var track = new Track { Id = $"{_sessionPrefix}-{++_nextId}", CreatedUtc = utc, Created = monotonic,
                Last = monotonic, LastUtc = utc, Box = detection.Bounds, Confidence = Math.Clamp(detection.Confidence, 0, 1) };
            track.Trail.Enqueue(new(detection.Bounds.Center, monotonic));
            _tracks.Add(track);
        }
        return Snapshot();
    }
    public IReadOnlyList<TrackSnapshot> Snapshot() => _tracks.Select(t => new TrackSnapshot(t.Id, _cameraId, _label, t.CreatedUtc,
        t.LastUtc, t.Created, t.Last, t.Box.Center, t.Box, t.Confidence, t.Missed, t.Motion,
        t.AngularPath is { } path && t.Last > t.Created ? path / (t.Last - t.Created).TotalSeconds : null,
        t.Peak, t.PixelPath, t.AngularPath, Array.AsReadOnly(t.Trail.ToArray()), Array.AsReadOnly(t.History.ToArray()))).ToArray();
}
