using System.Windows;
using SkySentry.App.ViewModels;

namespace SkySentry.App;

public partial class MainWindow : Window
{
    private bool _shutdownHandled;
    private bool _closing;
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownHandled) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        // Let this Closing callback unwind before calling Close again.
        await Task.Yield();
        try
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.ShutdownAsync(TimeSpan.FromSeconds(15));
                viewModel.Dispose();
            }
            _shutdownHandled = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sky Sentry — Shutdown", MessageBoxButton.OK, MessageBoxImage.Warning);
            _closing = false;
            IsEnabled = true;
        }
    }
}
