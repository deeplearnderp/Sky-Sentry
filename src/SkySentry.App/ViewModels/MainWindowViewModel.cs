using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using SkySentry.App.Services;
using SkySentry.Core.Analysis;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SkySentry.Capture.Sessions;
using SkySentry.Core.Cameras;
using SkySentry.Core.Events;
using SkySentry.Core.Recording;
using SkySentry.Recording.Segments;

namespace SkySentry.App.ViewModels;

/// <summary>
/// Top-level view model: owns device discovery, both camera panels (each
/// backed by its own segmented rolling-buffer recorder), the shared diagnostics
/// log, the recent-events list, and the "Save Event" control that triggers the
/// pre/post-event capture + finalize pipeline (see docs/RECORDING_PIPELINE.md).
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int MaxRecentEvents = 25;
    private readonly Stopwatch _healthClock = Stopwatch.StartNew();
    private TimeSpan _healthElapsed, _lastCpu;
    private string _systemHealth = "Health waiting";
    private TrackRowViewModel? _selectedTrack;
    private readonly DispatcherTimer _trackTimer;

    private readonly ICameraDiscoveryService _discoveryService;
    private readonly RecordingOptions _recordingOptions;
    private readonly IEventRecorder _eventRecorder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly DispatcherTimer _healthTimer;
    private readonly Dispatcher _dispatcher;
    private string _saveEventStatus = string.Empty;
    private string _eventLifecycleLabel = "Buffering";

    public MainWindowViewModel(
        ICameraDiscoveryService discoveryService,
        RecordingOptions recordingOptions,
        ILoggerFactory loggerFactory,
        Dispatcher dispatcher)
    {
        _discoveryService = discoveryService;
        _recordingOptions = recordingOptions;
        _loggerFactory = loggerFactory;
        _dispatcher = dispatcher;

        var analysisSettings = new AnalysisSettingsStore();
        if (analysisSettings.LoadWarning is { } warning) AppendLog(warning);
        Camera1 = new CameraPanelViewModel(
            "Camera 1",
            new OpenCvCameraCaptureSession("Camera 1", loggerFactory.CreateLogger<OpenCvCameraCaptureSession>()),
            new SegmentedCameraRecorder("Camera 1", recordingOptions, loggerFactory.CreateLogger("Camera 1 Recorder")),
            loggerFactory.CreateLogger("Camera 1"),
            dispatcher,
            IsDeviceSelectedElsewhere, analysisSettings);

        Camera2 = new CameraPanelViewModel(
            "Camera 2",
            new OpenCvCameraCaptureSession("Camera 2", loggerFactory.CreateLogger<OpenCvCameraCaptureSession>()),
            new SegmentedCameraRecorder("Camera 2", recordingOptions, loggerFactory.CreateLogger("Camera 2 Recorder")),
            loggerFactory.CreateLogger("Camera 2"),
            dispatcher,
            IsDeviceSelectedElsewhere, analysisSettings);

        _eventRecorder = new SegmentedEventRecorder(
            [Camera1.Recorder, Camera2.Recorder], recordingOptions, loggerFactory.CreateLogger("EventRecorder"), () => Camera1.GetTrackReferences().Concat(Camera2.GetTrackReferences()).ToArray());
        _eventRecorder.StateChanged += OnEventStateChanged;

        SaveEventCommand = new AsyncRelayCommand(SaveEventAsync);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        RefreshDevices();

        _healthTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _healthTimer.Tick += OnHealthTick;
        _healthTimer.Start();
        _trackTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
        _trackTimer.Tick += (_, _) => RefreshTracks();
        _trackTimer.Start();
    }

    public ObservableCollection<TrackRowViewModel> ActiveTracks { get; } = [];
    public int ActiveTrackCount => ActiveTracks.Count;
    public string MonitoringState => Camera1.IsConnected || Camera2.IsConnected ? "MONITORING" : "READY · SELECT A CAMERA";
    public string SystemHealth { get => _systemHealth; private set => SetField(ref _systemHealth, value); }
    public TrackRowViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetField(ref _selectedTrack, value))
            {
                Camera1.HighlightTrackId = value?.Track.TrackId;
                Camera2.HighlightTrackId = value?.Track.TrackId;
                OnPropertyChanged(nameof(TrackDetail));
            }
        }
    }
    public string TrackDetail => SelectedTrack?.Detail ?? "Select an active track to inspect its motion.\nAngular measurements require camera FOV calibration.";
    private void RefreshTracks()
    {
        var snapshots = (Camera1.AnalysisSnapshot?.Tracks ?? []).Concat(Camera2.AnalysisSnapshot?.Tracks ?? []).ToArray();
        var selectedId = SelectedTrack?.Track.TrackId;
        // Update a bounded list while preserving selection by identity.
        for (var i = 0; i < snapshots.Length; i++)
        {
            var row = new TrackRowViewModel(snapshots[i]);
            if (i < ActiveTracks.Count) ActiveTracks[i] = row; else ActiveTracks.Add(row);
        }
        while (ActiveTracks.Count > snapshots.Length) ActiveTracks.RemoveAt(ActiveTracks.Count - 1);
        SelectedTrack = ActiveTracks.FirstOrDefault(r => r.Track.TrackId == selectedId);
        OnPropertyChanged(nameof(ActiveTrackCount));
        OnPropertyChanged(nameof(MonitoringState));
    }
    public CameraPanelViewModel Camera1 { get; }

    public CameraPanelViewModel Camera2 { get; }

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = [];

    public ObservableCollection<string> DiagnosticsLog { get; } = [];

    public ObservableCollection<RecentEventSummary> RecentEvents { get; } = [];

    public AsyncRelayCommand SaveEventCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    public string SaveEventStatus
    {
        get => _saveEventStatus;
        private set => SetField(ref _saveEventStatus, value);
    }

    /// <summary>Live progress label for the in-flight event: Buffering / Recording Event / Finalizing.</summary>
    public string EventLifecycleLabel
    {
        get => _eventLifecycleLabel;
        private set => SetField(ref _eventLifecycleLabel, value);
    }

    private bool IsDeviceSelectedElsewhere(CameraDeviceInfo device, CameraPanelViewModel requestingPanel)
    {
        var other = ReferenceEquals(requestingPanel, Camera1) ? Camera2 : Camera1;
        return other.SelectedDevice?.DeviceId == device.DeviceId;
    }

    private void RefreshDevices()
    {
        AvailableDevices.Clear();
        foreach (var device in _discoveryService.DiscoverDevices())
        {
            AvailableDevices.Add(device);
        }

        foreach (var panel in new[] { Camera1, Camera2 })
        {
            // Keep a running camera's selected device/format untouched.
            if (panel.CanStop) continue;
            var selectedId = panel.SelectedDevice?.DeviceId;
            panel.AvailableDevices.Clear();
            foreach (var device in AvailableDevices) panel.AvailableDevices.Add(device);
            panel.SelectedDevice = panel.AvailableDevices.FirstOrDefault(d => d.DeviceId == selectedId);
        }
        AppendLog($"Discovered {AvailableDevices.Count} camera device(s).");
    }

    private async Task SaveEventAsync()
    {
        try
        {
            var metadata = await _eventRecorder.TriggerEventAsync();

            var camera1 = metadata.Cameras.FirstOrDefault(c => c.CameraLabel == Camera1.SessionLabel);
            var camera2 = metadata.Cameras.FirstOrDefault(c => c.CameraLabel == Camera2.SessionLabel);
            var anySuccess = metadata.Cameras.Any(c => c.Success);

            var eventFolder = BuildEventFolderPath(metadata.EventId, metadata.TriggeredAtUtc);

            SaveEventStatus = anySuccess
                ? $"Saved event {metadata.EventId} ({metadata.Cameras.Count(c => c.Success)}/{metadata.Cameras.Count} camera(s))."
                : $"Save Event failed for all cameras ({metadata.EventId}).";
            AppendLog(SaveEventStatus);

            AddRecentEvent(new RecentEventSummary(
                metadata.TriggeredAtUtc,
                metadata.EventId,
                anySuccess ? "Completed" : "Failed",
                camera1?.Success ?? false,
                camera2?.Success ?? false,
                eventFolder));
        }
        catch (EventAlreadyInProgressException)
        {
            SaveEventStatus = "An event is already being saved — please wait for it to finish.";
            AppendLog(SaveEventStatus);
        }
        catch (Exception ex)
        {
            SaveEventStatus = $"Save Event failed: {ex.Message}";
            AppendLog(SaveEventStatus);
            _loggerFactory.CreateLogger("SaveEvent").LogError(ex, "Save Event failed");
        }
    }

    private string BuildEventFolderPath(string eventId, DateTimeOffset triggeredAtUtc) =>
        Path.Combine(
            _recordingOptions.EventsDirectory,
            triggeredAtUtc.Year.ToString("D4"),
            triggeredAtUtc.Month.ToString("D2"),
            triggeredAtUtc.Day.ToString("D2"),
            eventId);

    private void AddRecentEvent(RecentEventSummary summary)
    {
        RecentEvents.Insert(0, summary);
        while (RecentEvents.Count > MaxRecentEvents)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }
    }

    private void OnEventStateChanged(object? sender, EventLifecycleState state)
    {
        _dispatcher.BeginInvoke(() =>
        {
            EventLifecycleLabel = state switch
            {
                EventLifecycleState.Idle => "Buffering",
                EventLifecycleState.Triggered => "Recording Event",
                EventLifecycleState.CapturingPostEvent => "Recording Event",
                EventLifecycleState.Finalizing => "Finalizing",
                EventLifecycleState.Completed => "Saved",
                EventLifecycleState.Failed => "Failed",
                _ => EventLifecycleLabel,
            };
        });
    }

    private void OnHealthTick(object? sender, EventArgs e)
    {
        var now = _healthClock.Elapsed;
        var elapsed = now - _healthElapsed;
        _healthElapsed = now;

        Camera1.TickFpsWindow(elapsed);
        Camera2.TickFpsWindow(elapsed);
        try
        {
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var percent = elapsed.TotalSeconds > 0 ? 100 * (cpu - _lastCpu).TotalSeconds / elapsed.TotalSeconds / Environment.ProcessorCount : 0;
            _lastCpu = cpu;
            var diskRoot = Path.GetPathRoot(Path.GetFullPath(_recordingOptions.EventsDirectory))!;
            var drive = new DriveInfo(diskRoot);
            var free = drive.IsReady ? $"{drive.AvailableFreeSpace / 1073741824.0:F1} GB free" : "disk --";
            SystemHealth = $"CPU {Math.Clamp(percent, 0, 100):F1}% · RAM {process.WorkingSet64 / 1048576.0:F0} MB · {free}\n" +
                $"Capture {Camera1.CaptureFps:F1} / {Camera2.CaptureFps:F1} FPS · Analysis {Camera1.AnalysisSnapshot?.AnalysisFps ?? 0:F1} / {Camera2.AnalysisSnapshot?.AnalysisFps ?? 0:F1} FPS · " +
                $"Analysis drops {Camera1.AnalysisSnapshot?.DroppedFrames ?? 0} / {Camera2.AnalysisSnapshot?.DroppedFrames ?? 0} · Tracks {ActiveTrackCount} · " +
                (Camera1.IsConnected && Camera2.IsConnected && Camera1.LatestFrameUtc is { } first && Camera2.LatestFrameUtc is { } second
                    ? $"Latest timestamp skew {Math.Abs((first - second).TotalMilliseconds):F1} ms (unsynchronized)" : "Timestamp skew --");
        }
        catch (Exception ex) { SystemHealth = "Health unavailable: " + ex.Message; }
    }

    private void AppendLog(string message)
    {
        DiagnosticsLog.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        while (DiagnosticsLog.Count > 200)
        {
            DiagnosticsLog.RemoveAt(DiagnosticsLog.Count - 1);
        }
    }

    /// <summary>
    /// Bounded shutdown: if an event is mid-flight, asks it to finalize
    /// immediately with whatever footage is already available rather than
    /// hanging the application close indefinitely (see docs/DECISIONS.md).
    /// </summary>
    public Task ShutdownAsync(TimeSpan timeout) => _eventRecorder.ShutdownAsync(timeout);

    public void Dispose()
    {
        _trackTimer.Stop();
        _healthTimer.Stop();
        _eventRecorder.StateChanged -= OnEventStateChanged;
        Camera1.Dispose();
        Camera2.Dispose();
        Camera1.Recorder.Dispose();
        Camera2.Recorder.Dispose();
    }
}
