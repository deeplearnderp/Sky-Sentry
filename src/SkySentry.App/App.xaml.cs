using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SkySentry.App.Services;
using SkySentry.App.ViewModels;
using SkySentry.Capture.Discovery;

namespace SkySentry.App;

/// <summary>
/// Composition root. Deliberately simple (no DI container) given the project's
/// current scope: wires up logging, discovery, recording configuration, and the
/// main window. Startup failures are logged and shown to the user rather than
/// crashing silently or swallowing the error.
/// </summary>
public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            AppPaths.EnsureDirectoriesExist();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppPaths.LogsDirectory, "skysentry-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();

            _loggerFactory = new SerilogLoggerFactory(Log.Logger);
            var startupLogger = _loggerFactory.CreateLogger("Startup");

            var recordingOptions = RecordingOptionsLoader.Load(_loggerFactory.CreateLogger("Settings"));
            recordingOptions.Validate();

            startupLogger.LogInformation(
                "Sky Sentry starting. Events root: {EventsRoot}; buffer root: {BufferRoot}; pre={Pre}s post={Post}s segment={Segment}s",
                recordingOptions.EventsDirectory, recordingOptions.RollingBufferDirectory,
                recordingOptions.PreEventDuration.TotalSeconds, recordingOptions.PostEventDuration.TotalSeconds,
                recordingOptions.SegmentDuration.TotalSeconds);

            var discoveryService = new DirectShowCameraDiscoveryService(
                _loggerFactory.CreateLogger<DirectShowCameraDiscoveryService>());

            var viewModel = new MainWindowViewModel(
                discoveryService, recordingOptions, _loggerFactory, Dispatcher.CurrentDispatcher);

            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();

            startupLogger.LogInformation("Sky Sentry main window shown.");
        }
        catch (Exception ex)
        {
            Log.Logger?.Error(ex, "Sky Sentry failed to start");
            MessageBox.Show(
                $"Sky Sentry failed to start:\n\n{ex}",
                "Sky Sentry — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Logger?.Error(e.Exception, "Unhandled UI-thread exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nSky Sentry will try to continue running.",
            "Sky Sentry — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Logger?.Fatal(ex, "Unhandled non-UI-thread exception");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Logger?.Information("Sky Sentry exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
