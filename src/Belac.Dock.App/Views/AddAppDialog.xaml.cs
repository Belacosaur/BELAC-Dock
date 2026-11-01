using System.Windows;
using Belac.Dock.Core.Catalog;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;

namespace Belac.Dock.App.Views;

public partial class AddAppDialog : Window
{
    private readonly AppHost _host;
    private readonly DockApp? _existing;

    public AddAppDialog(AppHost host, DockApp? existing)
    {
        _host = host;
        _existing = existing;
        InitializeComponent();
        GroupBox.ItemsSource = host.Session.Current.Groups.OrderBy(group => group.Order).ToList();
        if (existing is null)
        {
            GroupBox.SelectedItem = host.Session.Current.Groups.FirstOrDefault();
            return;
        }

        Heading.Text = "Edit application";
        Title = "Edit application";
        PathBox.Text = existing.ExePath;
        NameBox.Text = existing.DisplayName;
        ArgsBox.Text = existing.LaunchArgs ?? "";
        WorkBox.Text = existing.WorkingDirectory ?? "";
        GroupBox.SelectedItem = host.Session.Current.Groups.FirstOrDefault(group => group.Id == existing.GroupId) ?? host.Session.Current.Groups.FirstOrDefault();
    }

    public DockApp? Result { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
            Title = "Choose an application"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (dialog.FileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _host.Shortcuts.Resolve(dialog.FileName);
            if (resolved?.TargetPath is null)
            {
                ErrorText.Text = "That shortcut could not be resolved.";
                return;
            }

            PathBox.Text = resolved.TargetPath;
            ArgsBox.Text = resolved.Arguments ?? "";
            WorkBox.Text = resolved.WorkingDirectory ?? "";
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(resolved.TargetPath);
            return;
        }

        PathBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var path = PathBox.Text.Trim();
        if (!File.Exists(path))
        {
            ErrorText.Text = "That file was not found.";
            return;
        }

        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ErrorText.Text = "Choose an executable. Shortcuts are resolved to their target.";
            return;
        }

        var group = GroupBox.SelectedItem as DockGroup ?? _host.Session.Current.Groups.First();
        var full = Path.GetFullPath(path);
        var app = _existing ?? DockAppFactory.FromExecutable(full, group.Id, ArgsBox.Text, WorkBox.Text, 0);
        app.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? Path.GetFileNameWithoutExtension(full) : NameBox.Text.Trim();
        app.ExePath = full;
        app.LaunchArgs = string.IsNullOrWhiteSpace(ArgsBox.Text) ? null : ArgsBox.Text;
        app.WorkingDirectory = string.IsNullOrWhiteSpace(WorkBox.Text) ? Path.GetDirectoryName(full) : WorkBox.Text.Trim();
        app.GroupId = group.Id;
        app.MatchRules = [new MatchRule { Kind = MatchKind.ExecutablePath, Pattern = full, Priority = 100 }];
        if (_existing is null)
            app.Order = _host.Session.Current.Apps.Where(item => item.GroupId == group.Id).Select(item => item.Order).DefaultIfEmpty(-1).Max() + 1;
        Result = app;
        DialogResult = true;
    }
}
