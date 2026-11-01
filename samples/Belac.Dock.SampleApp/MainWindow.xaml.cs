using System.Windows;
using Belac.Dock.Integration;

namespace Belac.Dock.SampleApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PathText.Text = Environment.ProcessPath ?? "";
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => CooperativeDock.HideFromTaskbar(this);

    private void Show_Click(object sender, RoutedEventArgs e) => CooperativeDock.ShowInTaskbar(this);
}
