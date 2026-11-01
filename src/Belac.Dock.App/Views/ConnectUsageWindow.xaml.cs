using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Usage;
using Belac.Dock.Infrastructure;
using Belac.Dock.Core.Abstractions;

namespace Belac.Dock.App.Views;

public partial class ConnectUsageWindow : Window
{
    private readonly AppHost _host;
    private readonly CancellationTokenSource _closed = new();
    private readonly IUsageProviderSource _source;
    private readonly bool _ownsSource;
    private int _step;
    private bool _checking;
    private UsageSnapshot? _snapshot;
    public ConnectUsageWindow(AppHost host, IUsageProviderSource? source = null)
    {
        _host = host;
        _source = source ?? new OpenTokenUsageSource();
        _ownsSource = source is null;
        InitializeComponent();
        ShowStep();
        Loaded += async (_, _) => await InspectInstallationAsync();
        Closed += (_, _) => { _closed.Cancel(); if (_ownsSource && _source is IDisposable disposable) disposable.Dispose(); };
    }
    private async Task InspectInstallationAsync()
    {
        try
        {
            var location = await Task.Run(() => _host.UsageApps.Locate());
            if (_closed.IsCancellationRequested) return;
            InstallStatus.Text = location.IsRunning ? "OpenTokenUsage is running. You can continue." :
                location.ExecutablePath is not null ? "OpenTokenUsage is installed. Open it to continue." :
                "OpenTokenUsage was not detected. Install it, or open your existing copy from Windows Start.";
        }
        catch (Exception) { if (!_closed.IsCancellationRequested) InstallStatus.Text = "Could not detect the companion. You can still open it from Windows Start."; }
    }
    private void ShowStep()
    {
        StepLabel.Text = $"Step {_step + 1} of 3 · {new[] { "Companion app", "Your providers", "Connection check" }[_step]}";
        InstallStep.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountsStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        CheckStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _step == 2 ? "Show readings on dock" : "Next";
        NextButton.IsEnabled = _step != 2 || (!_checking && _snapshot is not null && UsageSetupStatus.From(_snapshot, true, true).CanFinish);
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 2) { _step++; ShowStep(); if (_step == 2) await CheckAsync(); return; }
        if (_snapshot is null || !UsageSetupStatus.From(_snapshot, true, true).CanFinish) return;
        try
        {
            _host.Session.Current.Usage.Enabled = true;
            var ids = _snapshot.Providers.Where(UsageSetupStatus.HasReading).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _host.Session.Current.Usage.HiddenProviderIds.RemoveAll(ids.Contains);
            _host.Session.Save();
            _host.RestartUsage();
            Close();
        }
        catch (Exception) { ActionError.Text = "Could not save the preference. Check that the dock's settings folder is writable, then try again."; }
    }
    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) _step--; ShowStep(); }
    private void Later_Click(object sender, RoutedEventArgs e) => Close();
    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();
    private async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        CheckButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        ConnectionTitle.Text = "Checking the local connection…";
        ConnectionDetail.Text = "This usually takes only a few seconds.";
        Readings.ItemsSource = null;
        try
        {
            var snapshot = await _source.GetSnapshotAsync(_closed.Token);
            var location = await Task.Run(() => _host.UsageApps.Locate());
            if (_closed.IsCancellationRequested) return;
            _snapshot = snapshot;
            var state = UsageSetupStatus.From(snapshot, location.IsRunning, location.ExecutablePath is not null);
            ConnectionTitle.Text = state.Title;
            ConnectionDetail.Text = state.Detail;
            Readings.ItemsSource = snapshot.Providers.Select(p => new {
                Name = p.DisplayName,
                Detail = (UsageSetupStatus.HasReading(p) ? "Reading available" : "Needs attention — refresh this provider in OpenTokenUsage") +
                    (p.FetchedAt is null ? " · update time unavailable" : $" · updated {p.FetchedAt.Value.ToLocalTime():g}")
            }).ToList();
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception) { _snapshot = null; ConnectionTitle.Text = "Couldn't finish the check"; ConnectionDetail.Text = "Open OpenTokenUsage and try again. Your app shortcuts are unaffected."; }
        finally
        {
            _checking = false;
            if (!_closed.IsCancellationRequested) { CheckButton.IsEnabled = true; ShowStep(); }
        }
    }
    private void Browse(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); ActionError.Text = ""; }
        catch (Exception) { ActionError.Text = "Couldn't open your browser. Open github.com/PowerUserZ/OpenTokenUsage for installation and provider help."; }
    }
    private void Download_Click(object sender, RoutedEventArgs e) => Browse("https://github.com/PowerUserZ/OpenTokenUsage/releases/latest");
    private void Guide_Click(object sender, RoutedEventArgs e) => Browse("https://github.com/Belacosaur/BELAC-Dock/blob/main/USAGE_INTEGRATION.md");
    private void ProviderHelp_Click(object sender, RoutedEventArgs e)
    {
        var provider = (ProviderHelp.SelectedItem as ComboBoxItem)?.Tag as string;
        Browse(provider is "codex" or "claude" or "cursor" or "copilot" ? $"https://github.com/PowerUserZ/OpenTokenUsage/blob/main/docs/providers/{provider}.md" : "https://github.com/PowerUserZ/OpenTokenUsage#supported-providers");
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        try { _host.OpenUsageApp(); ActionError.Text = "If no window appears, check the notification area beside the Windows clock."; }
        catch (Exception) { ActionError.Text = "Couldn't open OpenTokenUsage. Try opening it from Windows Start."; }
        await InspectInstallationAsync();
    }
}
