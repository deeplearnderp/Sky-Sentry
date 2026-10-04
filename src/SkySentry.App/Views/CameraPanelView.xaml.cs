using System.Windows.Controls;

namespace SkySentry.App.Views;

/// <summary>
/// Interaction logic for CameraPanelView.xaml. Pure view — all behavior lives in
/// <see cref="ViewModels.CameraPanelViewModel"/>, bound via the DataContext set by
/// MainWindow.
/// </summary>
public partial class CameraPanelView : UserControl
{
    public CameraPanelView()
    {
        InitializeComponent();
    }
}
