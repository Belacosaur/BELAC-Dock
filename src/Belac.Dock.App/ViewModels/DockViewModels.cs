using System.Windows;
using System.Windows.Media;
using Belac.Dock.App;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Belac.Dock.Core.Catalog;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Usage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Belac.Dock.App.ViewModels;

public abstract partial class DockRowViewModel : ObservableObject;

public partial class GroupStubViewModel : DockRowViewModel
{
    public GroupStubViewModel(DockGroup group)
    {
        GroupId = group.Id;
        Label = group.Label;
        Initial = string.IsNullOrWhiteSpace(group.Label) ? "?" : group.Label.Trim()[0].ToString().ToUpperInvariant();
    }

    public Guid GroupId { get; }
    public string Label { get; }
    public string Initial { get; }
}

public partial class WindowChoiceViewModel : ObservableObject
{
    public long Hwnd { get; init; }
    public string Title { get; init; } = "Untitled window";
    public string Detail { get; init; } = "";
}

public partial class AppItemViewModel : DockRowViewModel
{
    public Guid AppId { get; init; }
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private ImageSource? icon;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isFocused;
    [ObservableProperty] private bool isMinimized;
    [ObservableProperty] private bool isLaunching;
    [ObservableProperty] private bool isMissing;
    [ObservableProperty] private bool showDivider;
    [ObservableProperty] private int windowCount;
    [ObservableProperty] private bool hasMultiple;
    [ObservableProperty] private string taskbarText = "Mirror";
    [ObservableProperty] private string tooltip = "";
    [ObservableProperty] private bool hasWarning;
    [ObservableProperty] private string groupLabel = "";
    [ObservableProperty] private string monogram = "?";
    [ObservableProperty] private bool showGroupLabel;
    [ObservableProperty] private bool isSeparated;
    [ObservableProperty] private bool showPulse;
    [ObservableProperty] private double tileSize = 46;
    [ObservableProperty] private double iconBox = 32;
    [ObservableProperty] private double imageSize = 26;
    [ObservableProperty] private double iconOpacity = 1;
    [ObservableProperty] private Thickness itemMargin = new(0, 4, 0, 0);
    [ObservableProperty] private double pillHeight = 0;
    [ObservableProperty] private double pillOpacity = 0;
    public ObservableCollection<WindowChoiceViewModel> Windows { get; } = [];
}

public partial class ProviderRowViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string monogram = "?";
    [ObservableProperty] private string? plan;
    [ObservableProperty] private double outerPercent = double.NaN;
    [ObservableProperty] private double innerPercent = double.NaN;
    [ObservableProperty] private Brush outerBrush = Brushes.Gray;
    [ObservableProperty] private Brush innerBrush = Brushes.Gray;
    [ObservableProperty] private string tooltip = "";
    [ObservableProperty] private bool isStale;
    [ObservableProperty] private bool hasRing;
    [ObservableProperty] private bool hasChip;
    [ObservableProperty] private string chipText = "";
    [ObservableProperty] private string extraText = "";
    public ObservableCollection<string> Details { get; } = [];
}

public partial class UsageRailViewModel : ObservableObject
{
    public ObservableCollection<ProviderRowViewModel> Providers { get; } = [];
    [ObservableProperty] private bool isEnabled = true;
    [ObservableProperty] private bool hasData;
    [ObservableProperty] private bool isStale;
    [ObservableProperty] private bool isCollapsed;
    [ObservableProperty] private string message = "Checking AI usage…";
    [ObservableProperty] private string updatedText = "";

    public void Apply(UsageSnapshot snapshot, UsageSettings settings)
    {
        IsEnabled = settings.Enabled;
        if (!settings.Enabled)
        {
            HasData = false;
            Message = "";
            Providers.Clear();
            return;
        }

        IsStale = snapshot.SourceError is not null && snapshot.Providers.Count > 0;
        UpdatedText = snapshot.Providers.Count == 0 ? "" : snapshot.ObservedAt.ToLocalTime().ToString("HH:mm");
        if (snapshot.Providers.Count == 0)
        {
            HasData = false;
            Providers.Clear();
            Message = snapshot.SourceError is null
                ? "Connect your AI accounts to see usage."
                : snapshot.SourceError + " Open Connect AI usage to check your accounts.";
            return;
        }

        HasData = true;
        Message = IsStale ? "Stale. " + snapshot.SourceError : "";
        var ordered = ProviderOrdering.Order(snapshot.Providers, settings);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in ordered)
        {
            keep.Add(provider.Id);
            var row = Providers.FirstOrDefault(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new ProviderRowViewModel { Id = provider.Id };
                Providers.Add(row);
            }

            Fill(row, provider, settings);
        }

        for (var index = Providers.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(Providers[index].Id))
                Providers.RemoveAt(index);
        }

        var rank = ordered.Select((provider, index) => (provider.Id, index)).ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
        var sorted = Providers.OrderBy(row => rank.TryGetValue(row.Id, out var index) ? index : int.MaxValue).ToList();
        for (var index = 0; index < sorted.Count; index++)
        {
            var current = Providers.IndexOf(sorted[index]);
            if (current != index)
                Providers.Move(current, index);
        }
    }

    private static void Fill(ProviderRowViewModel row, ProviderUsage provider, UsageSettings settings)
    {
        row.DisplayName = provider.DisplayName;
        row.Plan = provider.Plan;
        row.Monogram = Monogram(provider.DisplayName);
        row.IsStale = provider.IsStale;
        var (outer, inner, extra) = RingLayout.Select(provider.Metrics);
        row.HasRing = outer?.UsedPercent is not null;
        row.OuterPercent = outer?.UsedPercent ?? double.NaN;
        row.InnerPercent = inner?.UsedPercent ?? double.NaN;
        row.OuterBrush = Palette.BandBrush(RingMath.Band(outer?.UsedPercent, settings.CautionThreshold, settings.CriticalThreshold), inner: false);
        row.InnerBrush = Palette.BandBrush(RingMath.Band(inner?.UsedPercent, settings.CautionThreshold, settings.CriticalThreshold), inner: true);
        row.ExtraText = extra > 0 ? "+" + extra : "";
        var chip = provider.Metrics.FirstOrDefault(metric => !string.IsNullOrWhiteSpace(metric.TextValue));
        row.HasChip = !row.HasRing && chip is not null;
        row.ChipText = chip?.TextValue ?? "";
        row.Tooltip = BuildTooltip(provider, settings);
        row.Details.Clear();
        foreach (var line in Describe(provider, settings))
            row.Details.Add(line);
    }

    private static IEnumerable<string> Describe(ProviderUsage provider, UsageSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(provider.Plan))
            yield return provider.Plan;
        foreach (var metric in provider.Metrics)
        {
            if (metric.UsedPercent is not null)
            {
                var used = metric.UsedPercent.Value;
                var shown = settings.ShowRemaining ? Math.Max(0, 100 - used) : used;
                var word = settings.ShowRemaining ? "left" : "used";
                var reset = ResetClock.Describe(metric.ResetsAt, DateTimeOffset.Now);
                yield return $"{metric.Label}: {shown:0.#}% {word}. Resets {reset}.";
            }
            else if (!string.IsNullOrWhiteSpace(metric.TextValue))
            {
                yield return $"{metric.Label}: {metric.TextValue}";
            }
        }

        if (provider.FetchedAt is DateTimeOffset fetched)
            yield return "Fetched " + fetched.ToLocalTime().ToString("g");
        if (provider.IsStale)
            yield return "This reading is stale.";
        if (!string.IsNullOrWhiteSpace(provider.Error))
            yield return provider.Error;
    }

    private static string BuildTooltip(ProviderUsage provider, UsageSettings settings)
    {
        var (outer, inner, _) = RingLayout.Select(provider.Metrics);
        var parts = new List<string> { provider.DisplayName };
        if (outer is not null)
            parts.Add(PercentLabel(outer, settings));
        if (inner is not null)
            parts.Add(PercentLabel(inner, settings));
        if (provider.IsStale)
            parts.Add("Stale");
        return string.Join("\n", parts);
    }

    private static string PercentLabel(UsageMetric metric, UsageSettings settings)
    {
        var used = metric.UsedPercent ?? 0;
        var shown = settings.ShowRemaining ? Math.Max(0, 100 - used) : used;
        return $"{metric.Label} {(settings.ShowRemaining ? "left" : "used")} {shown:0.#}%";
    }

    private static string Monogram(string name)
    {
        var letter = name.Trim().FirstOrDefault(char.IsLetterOrDigit);
        return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
    }
}

public partial class DockViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, AppItemViewModel> _items = [];
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _iconRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Guid AppId, int Pid, DateTime Utc)> _launches = [];
    private bool _flyoutGuard;

    public DockViewModel(AppHost host)
    {
        _host = host;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Usage = new UsageRailViewModel { IsEnabled = host.Session.Current.Usage.Enabled };
        SeparationPaused = host.Session.Current.SeparationPaused;
        LoadWarning = host.Session.Warning;
        _host.Tracker.Changed += (_, _) => _dispatcher.BeginInvoke(Rebuild);
        _host.UsagePoll.Updated += (_, _) => _dispatcher.BeginInvoke(PullUsage);
        _host.Session.Changed += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            SeparationPaused = _host.Session.Current.SeparationPaused;
            Usage.IsEnabled = _host.Session.Current.Usage.Enabled;
            Rebuild();
            PullUsage();
            LayoutRequested?.Invoke(this, EventArgs.Empty);
        });
        Rebuild();
        PullUsage();
    }

    public UsageRailViewModel Usage { get; }
    public ObservableCollection<DockRowViewModel> Rows { get; } = [];
    public ObservableCollection<AppItemViewModel> SearchResults { get; } = [];
    public ObservableCollection<WindowChoiceViewModel> ChooserWindows { get; } = [];

    [ObservableProperty] private bool hasApps;
    [ObservableProperty] private bool isCollapsed;
    [ObservableProperty] private bool separationPaused;
    [ObservableProperty] private string pauseGlyph = "\uE769";
    [ObservableProperty] private string pauseToolTip = "Pause taskbar separation";
    [ObservableProperty] private string collapseGlyph = "\uE76C";
    [ObservableProperty] private string expandGlyph = "\uE76B";
    [ObservableProperty] private string? loadWarning;
    [ObservableProperty] private bool flyoutOpen;
    [ObservableProperty] private string flyout = "None";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string chooserTitle = "Windows";
    [ObservableProperty] private ProviderRowViewModel? usageDetail;
    [ObservableProperty] private string statusText = "";

    public event EventHandler? AddRequested;
    public event EventHandler<Guid>? EditRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? LayoutRequested;

    partial void OnSeparationPausedChanged(bool value)
    {
        PauseGlyph = value ? "\uE768" : "\uE769";
        PauseToolTip = value ? "Resume taskbar separation" : "Pause taskbar separation";
    }

    partial void OnSearchTextChanged(string value) => RefreshSearch();

    partial void OnFlyoutOpenChanged(bool value)
    {
        if (_flyoutGuard)
            return;
        if (!value)
            SetFlyout("None", open: false);
    }

    partial void OnIsCollapsedChanged(bool value) => LayoutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ActivateItem(AppItemViewModel? item)
    {
        if (item is null)
            return;

        if (!item.IsRunning)
        {
            Launch(item);
            return;
        }

        if (item.WindowCount > 1)
        {
            ShowWindows(item);
            return;
        }

        var window = item.Windows.FirstOrDefault();
        if (window is null)
        {
            Launch(item);
            return;
        }

        if (item.IsFocused && _host.Session.Current.MinimizeIfFocused)
            _host.Controller.TryMinimize(window.Hwnd);
        else if (!_host.Controller.TryActivate(window.Hwnd))
            item.TaskbarText = _host.Controller.LastFailure ?? "Windows did not move focus.";
    }

    [RelayCommand] private void OpenAdd() => AddRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ToggleCollapse() => IsCollapsed = !IsCollapsed;
    [RelayCommand] private void ToggleUsage() => Usage.IsCollapsed = !Usage.IsCollapsed;
    [RelayCommand] private void ShowSearch() => SetFlyout("Search", open: true);

    [RelayCommand]
    private void RefreshUsage() => _ = _host.UsagePoll.RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private void OpenUsageApp() => _host.ConnectUsage();

    [RelayCommand]
    private void ConnectUsage() => _host.ConnectUsage();

    [RelayCommand]
    private void ShowUsage(ProviderRowViewModel? row)
    {
        if (row is null)
            return;
        UsageDetail = row;
        SetFlyout("Usage", open: true);
    }

    [RelayCommand]
    private void ChooseWindow(WindowChoiceViewModel? choice)
    {
        if (choice is null)
            return;
        if (!_host.Controller.TryActivate(choice.Hwnd))
            StatusText = _host.Controller.LastFailure ?? "Windows did not move focus.";
        SetFlyout("None", open: false);
    }

    [RelayCommand]
    private void ToggleGroup(GroupStubViewModel? stub)
    {
        var group = _host.Session.Current.Groups.FirstOrDefault(item => item.Id == stub?.GroupId);
        if (group is null)
            return;
        group.Collapsed = !group.Collapsed;
        _host.Session.Save();
    }

    [RelayCommand]
    private void DismissWarning()
    {
        _host.Session.DismissWarning();
        LoadWarning = null;
    }

    [RelayCommand]
    private void TogglePause()
    {
        var settings = _host.Session.Current;
        settings.SeparationPaused = !settings.SeparationPaused;
        _host.Separation.SetPaused(settings.SeparationPaused);
        _host.Separation.Reconcile(settings.Apps);
        if (settings.SeparationPaused)
            _host.Separation.RestoreAll(Probe);
        _host.Session.Save();
        StatusText = settings.SeparationPaused ? "Taskbar separation is paused." : "Taskbar separation is on for apps that opted in.";
    }

    public void Launch(AppItemViewModel item)
    {
        var app = Find(item.AppId);
        if (app is null)
            return;
        if (!File.Exists(app.ExePath))
        {
            Repair(item);
            return;
        }

        try
        {
            var process = Process.Start(LaunchRequest.Create(app.ExePath, app.LaunchArgs, app.WorkingDirectory));
            if (process is not null)
            {
                try
                {
                    _launches.Add((app.Id, process.Id, DateTime.UtcNow));
                }
                catch (InvalidOperationException)
                {
                }
            }

            SetLaunching(item, true);
            var id = item.AppId;
            _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => _dispatcher.BeginInvoke(() =>
            {
                if (_items.TryGetValue(id, out var current))
                    SetLaunching(current, false);
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            SetLaunching(item, false);
            item.HasWarning = true;
            item.TaskbarText = "Launch failed.";
            _host.Logger.LogWarning("Launch failed for {File}: {Error}", Path.GetFileName(app.ExePath), ex.GetType().Name);
        }
    }

    public void ShowWindows(AppItemViewModel item)
    {
        ChooserWindows.Clear();
        foreach (var window in item.Windows)
            ChooserWindows.Add(window);
        ChooserTitle = item.DisplayName;
        SetFlyout("Windows", open: true);
    }

    public void Minimize(AppItemViewModel item)
    {
        foreach (var window in item.Windows)
            _host.Controller.TryMinimize(window.Hwnd);
    }

    public void CloseWindow(AppItemViewModel item)
    {
        if (item.WindowCount > 1 && !item.IsFocused)
        {
            ShowWindows(item);
            StatusText = "Choose the window to close.";
            return;
        }

        var target = item.Windows.FirstOrDefault();
        if (target is null)
            return;
        if (!_host.Controller.TryClose(target.Hwnd))
            StatusText = _host.Controller.LastFailure ?? "Close was not accepted.";
    }

    public void OpenFolder(AppItemViewModel item)
    {
        var app = Find(item.AppId);
        if (app is null || !File.Exists(app.ExePath))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + app.ExePath + "\"") { UseShellExecute = true });
    }

    public void Remove(AppItemViewModel item)
    {
        var app = Find(item.AppId);
        if (app is null)
            return;
        if (MessageBox.Show($"Remove {app.DisplayName} from the dock? The program itself is not uninstalled.", "BELAC Dock", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _host.Session.Current.Apps.RemoveAll(candidate => candidate.Id == app.Id);
        _host.Session.Save();
    }

    public void SetMode(AppItemViewModel item, TaskbarMode mode)
    {
        var app = Find(item.AppId);
        if (app is null)
            return;
        if (mode == TaskbarMode.DockOnly)
        {
            var warning = "Try to hide this app's bottom taskbar button. If Windows cannot verify it, the taskbar button is restored and the dock reports the reason.";
            if (app.AllowStyleCompatibility)
                warning += "\n\nStyle compatibility is on. That can remove the window from Alt+Tab. Restore All puts it back.";
            if (MessageBox.Show(warning, "Dock-only is experimental", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        app.TaskbarMode = mode;
        _host.Session.Save();
    }

    public bool SetCompatibility(AppItemViewModel item, bool enabled)
    {
        var app = Find(item.AppId);
        if (app is null)
            return false;
        if (enabled && MessageBox.Show("This can change the window style and remove it from Alt+Tab. Turn it on only if DeleteTab is not enough.", "Style compatibility", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return false;
        foreach (var choice in item.Windows)
            if (_host.Inspector.TryInspect(choice.Hwnd, out var window)) _host.Separation.RestoreOne(window);
        app.AllowStyleCompatibility = enabled;
        _host.Session.Save();
        return true;
    }

    public void Move(AppItemViewModel item, int delta)
    {
        var settings = _host.Session.Current;
        AppOrdering.MoveBy(settings.Apps, settings.Groups, item.AppId, delta);
        _host.Session.Save();
    }

    public void MoveBefore(Guid sourceId, Guid targetId)
    {
        var settings = _host.Session.Current;
        var ordered = AppOrdering.InDisplayOrder(settings.Groups, settings.Apps);
        var from = ordered.FindIndex(app => app.Id == sourceId);
        var to = ordered.FindIndex(app => app.Id == targetId);
        if (from < 0 || to < 0 || from == to)
            return;
        AppOrdering.MoveBy(settings.Apps, settings.Groups, sourceId, to - from);
        _host.Session.Save();
    }

    public void MoveToGroup(AppItemViewModel item, Guid groupId)
    {
        AppOrdering.MoveToGroup(_host.Session.Current.Apps, item.AppId, groupId);
        _host.Session.Save();
    }

    public void RequestEdit(AppItemViewModel item) => EditRequested?.Invoke(this, item.AppId);

    public void Repair(AppItemViewModel item)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk",
            Title = "Repair application path"
        };
        if (dialog.ShowDialog() != true)
            return;
        AddFromPaths([dialog.FileName], replaceId: item.AppId);
    }

    public void AddFromPaths(IEnumerable<string> paths, Guid? replaceId = null)
    {
        var settings = _host.Session.Current;
        var groupId = settings.Groups.FirstOrDefault()?.Id ?? WellKnownGroups.Services;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string? target = raw;
            string? args = null;
            string? work = null;
            if (raw.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = _host.Shortcuts.Resolve(raw);
                if (resolved?.TargetPath is null)
                {
                    StatusText = "That shortcut could not be resolved.";
                    continue;
                }

                target = resolved.TargetPath;
                args = resolved.Arguments;
                work = resolved.WorkingDirectory;
            }

            if (string.IsNullOrWhiteSpace(target))
                continue;
            try
            {
                target = Path.GetFullPath(target);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (replaceId is Guid replacing)
            {
                var existing = Find(replacing);
                if (existing is null)
                    continue;
                existing.ExePath = target;
                existing.LaunchArgs = args;
                existing.WorkingDirectory = work;
                existing.DisplayName = Path.GetFileNameWithoutExtension(target);
                existing.MatchRules = [new MatchRule { Kind = MatchKind.ExecutablePath, Pattern = target, Priority = 100 }];
                continue;
            }

            if (settings.Apps.Any(app => AppMatcher.PathsEqual(app.ExePath, target)))
            {
                StatusText = Path.GetFileName(target) + " is already on the dock.";
                continue;
            }

            var order = settings.Apps.Where(app => app.GroupId == groupId).Select(app => app.Order).DefaultIfEmpty(-1).Max() + 1;
            settings.Apps.Add(DockAppFactory.FromExecutable(target, groupId, args, work, order));
        }

        _host.Session.Save();
    }

    public void ReplaceApp(DockApp updated)
    {
        var apps = _host.Session.Current.Apps;
        var index = apps.FindIndex(app => app.Id == updated.Id);
        if (index < 0)
            apps.Add(updated);
        else
            apps[index] = updated;
        _host.Session.Save();
    }

    public int RestoreAll() => _host.Separation.RestoreAll(Probe);

    public void Rebuild()
    {
        var settings = _host.Session.Current;
        var windows = _host.Tracker.Windows;
        var foreground = _host.Tracker.ForegroundHwnd;
        var matched = MatchWindows(settings, windows);
        var rows = new List<DockRowViewModel>();
        var seen = new HashSet<Guid>();
        foreach (var group in settings.Groups.OrderBy(item => item.Order))
        {
            var apps = settings.Apps.Where(app => app.Enabled && app.GroupId == group.Id).OrderBy(app => app.Order).ThenBy(app => app.DisplayName).ToList();
            if (apps.Count == 0)
                continue;
            if (group.Collapsed)
            {
                rows.Add(Rows.OfType<GroupStubViewModel>().FirstOrDefault(row => row.GroupId == group.Id && row.Label == group.Label) ?? new GroupStubViewModel(group));
                continue;
            }

            var first = true;
            foreach (var app in apps)
            {
                seen.Add(app.Id);
                var item = Get(app);
                matched.TryGetValue(app.Id, out var appWindows);
                UpdateItem(item, app, appWindows ?? [], foreground, group.Label, first, first && rows.Count > 0);
                first = false;
                rows.Add(item);
            }
        }

        foreach (var stale in _items.Keys.Where(id => !seen.Contains(id)).ToList())
            _items.Remove(stale);

        // Preserve item containers, hover state and popup anchors across tracker updates.
        for (var index = Rows.Count - 1; index >= 0; index--)
            if (!rows.Contains(Rows[index])) Rows.RemoveAt(index);
        for (var index = 0; index < rows.Count; index++)
        {
            if (index < Rows.Count && ReferenceEquals(Rows[index], rows[index])) continue;
            var existing = Rows.IndexOf(rows[index]);
            if (existing >= 0) Rows.Move(existing, index);
            else Rows.Insert(index, rows[index]);
        }
        HasApps = settings.Apps.Count > 0;
        CollapseGlyph = settings.RightEdge ? "\uE76C" : "\uE76B";
        ExpandGlyph = settings.RightEdge ? "\uE76B" : "\uE76C";
        RefreshSearch();
        ApplySeparation(settings, matched);
        if (_host.Tracker.TaskbarHwnd != _lastTaskbar && _lastTaskbar != 0)
            LayoutRequested?.Invoke(this, EventArgs.Empty);
        _lastTaskbar = _host.Tracker.TaskbarHwnd;
    }

    public string BuildDiagnostics()
    {
        var settings = _host.Session.Current;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("BELAC Dock diagnostics");
        builder.AppendLine("Appbar registered: " + _host.Appbar.IsRegistered);
        builder.AppendLine("Dock bounds: " + (_host.Appbar.DockBounds?.ToString() ?? "none"));
        builder.AppendLine("Window hook: " + _host.Tracker.IsHooked);
        builder.AppendLine("Tracked windows: " + _host.Tracker.Windows.Count);
        builder.AppendLine("Matched apps running: " + Rows.OfType<AppItemViewModel>().Count(item => item.IsRunning));
        builder.AppendLine("Separation paused: " + settings.SeparationPaused);
        builder.AppendLine("Last separation note: " + _host.Separation.LastDetail);
        builder.AppendLine("Usage: " + (Usage.HasData ? Usage.Providers.Count + " providers" : Usage.Message));
        builder.AppendLine("Shortcut: " + (_host.ShortcutRegistered ? settings.KeyboardShortcut : _host.ShortcutWarning));
        foreach (var note in _host.Separation.RecentEvents.TakeLast(8))
            builder.AppendLine(note);
        return builder.ToString();
    }

    private long _lastTaskbar;

    private void ApplySeparation(DockSettings settings, Dictionary<Guid, List<WindowObservation>> matched)
    {
        _host.Separation.SetPaused(settings.SeparationPaused);
        _host.Separation.Reconcile(settings.Apps);
        if (settings.SeparationPaused)
            return;

        foreach (var app in settings.Apps)
        {
            if (!matched.TryGetValue(app.Id, out var windows))
                continue;
            if (!_items.TryGetValue(app.Id, out var item))
                continue;

            var candidates = windows.Where(window => (!window.HasOwner && !window.IsToolWindow) || window.IsAppWindow
                || _host.Separation.Snapshots.Any(entry => JournalValidator.IsSameWindow(entry, window))).ToArray();
            var results = _host.Separation.ApplyBatch(app, candidates);
            // One successful child must not conceal another window's failure or pending verification.
            var last = results.FirstOrDefault(result => result.Status is TaskbarStatus.Error or TaskbarStatus.Unsupported)
                ?? results.FirstOrDefault(result => result.Status == TaskbarStatus.Unknown)
                ?? results.FirstOrDefault();
            if (last is null) continue;
            item.TaskbarText = last.Detail;
            item.IsSeparated = last.Status == TaskbarStatus.DockOnly;
            item.HasWarning = last.Status is TaskbarStatus.Unsupported or TaskbarStatus.Error;
            item.Tooltip = $"{app.DisplayName}\n{app.ExePath}\n{(item.IsRunning ? item.WindowCount + " running" : "Not running")}\n{last.Detail}";
        }
    }

    private Dictionary<Guid, List<WindowObservation>> MatchWindows(DockSettings settings, IReadOnlyList<WindowObservation> windows)
    {
        var matched = new Dictionary<Guid, List<WindowObservation>>();
        _launches.RemoveAll(launch => DateTime.UtcNow - launch.Utc > TimeSpan.FromMinutes(2));
        foreach (var window in windows)
        {
            var outcome = AppMatcher.Match(settings.Apps, window);
            Guid? id = outcome.App?.Id;
            if (id is null)
            {
                var launch = _launches.LastOrDefault(item => item.Pid == window.ProcessId);
                if (launch.Pid == window.ProcessId)
                {
                    var app = settings.Apps.FirstOrDefault(candidate => candidate.Id == launch.AppId);
                    if (app is not null && (string.IsNullOrWhiteSpace(window.ExecutablePath) || AppMatcher.PathsEqual(app.ExePath, window.ExecutablePath)))
                        id = app.Id;
                }
            }

            if (id is null)
                continue;
            if (!matched.TryGetValue(id.Value, out var list))
                matched[id.Value] = list = [];
            list.Add(window);
        }

        return matched;
    }

    private void UpdateItem(AppItemViewModel item, DockApp app, List<WindowObservation> windows, long foreground, string groupLabel, bool showLabel, bool divider)
    {
        var settings = _host.Session.Current;
        var icon = Math.Clamp(settings.IconSizeDip, 32, 64);
        item.DisplayName = app.DisplayName;
        item.Monogram = Letter(app.DisplayName);
        item.GroupLabel = groupLabel;
        item.ShowGroupLabel = showLabel;
        item.ShowDivider = divider;
        item.TileSize = icon + 4;
        item.IconBox = Math.Max(24, icon - 8);
        item.ImageSize = Math.Max(16, icon - 16);
        item.ItemMargin = new Thickness(0, Math.Clamp(settings.GapDip, 4, 24) / 2, 0, 0);
        item.IconOpacity = File.Exists(app.ExePath) ? 1 : 0.4;
        item.IsMissing = !File.Exists(app.ExePath);
        item.IsSeparated = false;
        item.WindowCount = windows.Count;
        item.HasMultiple = windows.Count > 1;
        item.IsRunning = windows.Count > 0;
        item.IsFocused = windows.Any(window => window.Hwnd == foreground);
        item.IsMinimized = item.IsRunning && windows.All(window => window.IsMinimized);
        if (item.IsRunning)
            SetLaunching(item, false);
        item.PillHeight = item.IsFocused ? 22 : item.IsRunning ? item.IsMinimized ? 8 : 14 : 0;
        item.PillOpacity = item.IsFocused ? 1 : 0.85;
        item.HasWarning = false;
        item.TaskbarText = app.TaskbarMode == TaskbarMode.DockOnly ? "Dock-only" : "Mirror";
        item.Windows.Clear();
        foreach (var window in windows.OrderByDescending(candidate => candidate.Hwnd == foreground))
        {
            item.Windows.Add(new WindowChoiceViewModel
            {
                Hwnd = window.Hwnd,
                Title = string.IsNullOrWhiteSpace(window.Title) ? app.DisplayName : window.Title,
                Detail = window.IsMinimized ? "Minimized" : window.Hwnd == foreground ? "Active" : "Open"
            });
        }

        item.Tooltip = $"{app.DisplayName}\n{app.ExePath}\n{(item.IsRunning ? item.WindowCount + " running" : "Not running")}\n{item.TaskbarText}";
        if (item.Icon is null)
            RequestIcon(item, app);
    }

    private void RequestIcon(AppItemViewModel item, DockApp app)
    {
        var path = string.IsNullOrWhiteSpace(app.IconPath) ? app.ExePath : app.IconPath;
        if (_icons.TryGetValue(path, out var cached))
        {
            item.Icon = cached;
            return;
        }

        if (!_iconRequests.Add(path))
            return;

        _ = Task.Run(() =>
        {
            var bytes = _host.Icons.GetPng(path, 64);
            if (bytes is null)
                return;
            _dispatcher.BeginInvoke(() =>
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _icons[path] = image;
                item.Icon = image;
            });
        });
    }

    private AppItemViewModel Get(DockApp app)
    {
        if (_items.TryGetValue(app.Id, out var existing))
            return existing;
        var created = new AppItemViewModel { AppId = app.Id, DisplayName = app.DisplayName };
        _items[app.Id] = created;
        return created;
    }

    private DockApp? Find(Guid id) => _host.Session.Current.Apps.FirstOrDefault(app => app.Id == id);

    private WindowObservation? Probe(long hwnd) => _host.Inspector.TryInspect(hwnd, out var observation) ? observation : null;

    private void PullUsage() => Usage.Apply(_host.UsagePoll.Current, _host.Session.Current.Usage);

    private void SetLaunching(AppItemViewModel item, bool launching)
    {
        item.IsLaunching = launching;
        item.ShowPulse = launching && _host.Session.Current.Animation && SystemParameters.ClientAreaAnimation;
    }

    private static string Letter(string name)
    {
        var letter = name.Trim().FirstOrDefault(char.IsLetterOrDigit);
        return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
    }

    private void RefreshSearch()
    {
        SearchResults.Clear();
        foreach (var item in _items.Values.OrderBy(row => row.DisplayName))
        {
            if (string.IsNullOrWhiteSpace(SearchText) ||
                item.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Tooltip.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                SearchResults.Add(item);
        }
    }

    private void SetFlyout(string kind, bool open)
    {
        _flyoutGuard = true;
        Flyout = kind;
        FlyoutOpen = open && kind != "None";
        _flyoutGuard = false;
    }
}
