using System.Windows;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows;

namespace Belac.Dock.App.Views;

public partial class OnboardingWindow : Window
{
    private readonly AppHost _host;
    private readonly List<string> _pending = [];
    private readonly List<(string Name, string Path)> _installed = [];
    private int _step;

    public OnboardingWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        MonitorBox.ItemsSource = host.Monitors.GetMonitors();
        MonitorBox.SelectedItem = MonitorCatalog.Find(host.Monitors.GetMonitors(), host.Session.Current.AssignedMonitorDeviceName);
        ShowStep();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
            Multiselect = true,
            Title = "Add applications"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        foreach (var file in dialog.FileNames)
        {
            _pending.Add(file);
            AddedList.Items.Add(Path.GetFileName(file));
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
            return;
        _step--;
        ShowStep();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 3)
        {
            _step++;
            ShowStep();
            return;
        }

        var settings = _host.Session.Current;
        if (MonitorBox.SelectedItem is MonitorInfo monitor)
            settings.AssignedMonitorDeviceName = monitor.DeviceName;
        settings.OnboardingCompleted = true;
        _host.Session.Save();
        if (_pending.Count > 0)
            _host.Dock.AddFromPaths(_pending);
        DialogResult = true;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        EnsureInstalled();
        var query = SearchBox.Text.Trim();
        InstalledList.Items.Clear();
        foreach (var app in _installed)
        {
            if (query.Length > 0 && !app.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            InstalledList.Items.Add(app.Name);
            if (InstalledList.Items.Count >= 40)
                break;
        }
    }

    private void Installed_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (InstalledList.SelectedItem is not string name)
            return;
        var match = _installed.FirstOrDefault(app => app.Name == name);
        if (string.IsNullOrWhiteSpace(match.Path) || _pending.Contains(match.Path))
            return;
        _pending.Add(match.Path);
        AddedList.Items.Add(name);
    }

    private void EnsureInstalled()
    {
        if (_installed.Count > 0)
            return;

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                    continue;
                _installed.Add((name, file));
                if (_installed.Count >= 400)
                    return;
            }
        }

        _installed.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowStep()
    {
        Welcome.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        MonitorStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppsStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_step == 2 && InstalledList.Items.Count == 0)
            SearchBox_TextChanged(this, null!);
        SeparationStep.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == 3 ? "Finish" : "Next";
    }
}
