using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Belac.Dock.App.ViewModels;
using Belac.Dock.Core.Models;

namespace Belac.Dock.App.Views;

public partial class DockWindow
{
    internal ContextMenu? BuildAppContextMenu(AppItemViewModel item)
    {
        var app = _host.Session.Current.Apps.FirstOrDefault(candidate => candidate.Id == item.AppId);
        if (app is null) return null;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = item.DisplayName,
            IsEnabled = false,
            Style = (Style)FindResource("ContextMenuHeading")
        });
        if (item.IsMissing)
            menu.Items.Add(Action("Locate app...", () => _viewModel.Repair(item), "\uE721"));
        else if (item.WindowCount > 1)
            menu.Items.Add(Action("Choose window...", () => _viewModel.ShowWindows(item), "\uE737"));
        else if (item.IsRunning && item.Windows.FirstOrDefault() is { } window)
            menu.Items.Add(Action("Switch to app", () => _viewModel.ChooseWindowCommand.Execute(window), "\uE8A7"));
        else
            menu.Items.Add(Action("Open", () => _viewModel.Launch(item), "\uE8A7"));

        if (item.IsRunning)
        {
            menu.Items.Add(Action(item.WindowCount > 1 ? "Minimize all windows" : "Minimize", () => _viewModel.Minimize(item), "\uE921"));
            if (item.WindowCount == 1)
                menu.Items.Add(Action("Close window", () => _viewModel.CloseWindow(item), "\uE8BB"));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("Edit app...", () => _viewModel.RequestEdit(item), "\uE70F"));
        var more = new MenuItem { Header = "More", Icon = Glyph("\uE712") };
        more.Items.Add(Action("Open file location", () => _viewModel.OpenFolder(item), "\uE8B7"));
        more.Items.Add(new Separator());
        var ordered = Belac.Dock.Core.Catalog.AppOrdering.InDisplayOrder(_host.Session.Current.Groups, _host.Session.Current.Apps);
        var index = ordered.FindIndex(candidate => candidate.Id == app.Id);
        var up = Action("Move up", () => _viewModel.Move(item, -1), "\uE74A");
        up.IsEnabled = index > 0;
        var down = Action("Move down", () => _viewModel.Move(item, 1), "\uE74B");
        down.IsEnabled = index >= 0 && index < ordered.Count - 1;
        more.Items.Add(up);
        more.Items.Add(down);
        if (_host.Session.Current.Groups.Count > 1)
        {
            var groups = new MenuItem { Header = "Move to group" };
            foreach (var group in _host.Session.Current.Groups.OrderBy(candidate => candidate.Order))
            {
                var groupId = group.Id;
                var entry = Action(group.Label, () => _viewModel.MoveToGroup(item, groupId));
                entry.IsChecked = groupId == app.GroupId;
                groups.Items.Add(entry);
            }
            more.Items.Add(groups);
        }
        more.Items.Add(new Separator());
        var taskbar = new MenuItem { Header = "Taskbar options" };
        var mirror = Action("Show on both taskbars", () => _viewModel.SetMode(item, TaskbarMode.Mirror));
        mirror.IsChecked = app.TaskbarMode == TaskbarMode.Mirror;
        var dockOnly = Action("Dock only (experimental)", () => _viewModel.SetMode(item, TaskbarMode.DockOnly));
        dockOnly.IsChecked = app.TaskbarMode == TaskbarMode.DockOnly;
        taskbar.Items.Add(mirror);
        taskbar.Items.Add(dockOnly);
        taskbar.Items.Add(new Separator());
        var compat = new MenuItem
        {
            Header = "Compatibility fallback",
            IsCheckable = true,
            IsChecked = app.AllowStyleCompatibility,
            ToolTip = "If normal hiding fails, allow a window-style change for this app. This also hides it from Alt+Tab. Switching to Mirror restores it."
        };
        compat.Click += (_, _) =>
        {
            if (!_viewModel.SetCompatibility(item, compat.IsChecked)) compat.IsChecked = !compat.IsChecked;
        };
        taskbar.Items.Add(compat);
        more.Items.Add(taskbar);
        menu.Items.Add(more);
        menu.Items.Add(new Separator());
        var remove = Action("Remove from dock", () => _viewModel.Remove(item), "\uE738");
        remove.SetResourceReference(ForegroundProperty, "Brush.Danger");
        menu.Items.Add(remove);
        return menu;
    }

    private static MenuItem Action(string label, System.Action action, string? glyph = null)
    {
        var item = new MenuItem { Header = label, Icon = glyph is null ? null : Glyph(glyph) };
        item.Click += (_, _) => action();
        return item;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
}
