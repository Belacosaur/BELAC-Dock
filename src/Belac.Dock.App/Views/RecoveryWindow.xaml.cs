using System.Windows;

namespace Belac.Dock.App.Views;

public partial class RecoveryWindow : Window
{
    public RecoveryWindow(int valid, int stale)
    {
        InitializeComponent();
        Body.Text = valid == 0
            ? $"BELAC Dock found {stale} outdated recovery record(s). They do not match a running window, so nothing will be changed."
            : $"BELAC Dock did not exit cleanly. {valid} window style(s) can be restored. {stale} outdated record(s) will be ignored. Restoring puts the original taskbar button and Alt+Tab entry back when the same process is still running.";
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
