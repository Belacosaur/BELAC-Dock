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
    private UsageSettings _selection = new();
    private int _step;
    private bool _checking;
    private UsageSnapshot? _snapshot;
    public ConnectUsageWindow(AppHost host, IUsageProviderSource? source = null)
    {
        _host = host;
        _source = source ?? host.NativeUsage;
        InitializeComponent();
        ShowStep();
        CodexBox.IsChecked = (host.Session.Current.Usage.NativeProviders ?? []).Contains("codex");
        ClaudeBox.IsChecked = (host.Session.Current.Usage.NativeProviders ?? []).Contains("claude");
        CursorBox.IsChecked = (host.Session.Current.Usage.NativeProviders ?? []).Contains("cursor");
        WslBox.IsChecked = host.Session.Current.Usage.IncludeWslCodex;
        FallbackBox.IsChecked = host.Session.Current.Usage.CompanionFallback;
        Closed += (_, _) => { _closed.Cancel();  };
    }
    private void ShowStep()
    {
        StepLabel.Text = $"Step {_step + 1} of 3 · {new[] { "Choose providers", "Sign in once", "Connection check" }[_step]}";
        InstallStep.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountsStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        CheckStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        BackButton.IsEnabled = !_checking;
        NextButton.Content = _step == 2 ? "Show readings on dock" : "Next";
        NextButton.IsEnabled = _step != 2 || (!_checking && _snapshot is not null && UsageSetupStatus.From(_snapshot, true, true).CanFinish);
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        _selection.NativeProviders = new[] { ("codex", CodexBox.IsChecked), ("claude", ClaudeBox.IsChecked), ("cursor", CursorBox.IsChecked) }.Where(p => p.Item2 == true).Select(p => p.Item1).ToList();
        _selection.IncludeWslCodex = WslBox.IsChecked == true;
        _selection.CompanionFallback = FallbackBox.IsChecked == true;
        if (_step < 2) { _step++; ShowStep(); if (_step == 2) await CheckAsync(); return; }
        if (_snapshot is null || !UsageSetupStatus.From(_snapshot, true, true).CanFinish) return;
        try
        {
            _host.Session.Current.Usage.NativeProviders = _selection.NativeProviders;
            _host.Session.Current.Usage.IncludeWslCodex = _selection.IncludeWslCodex;
            _host.Session.Current.Usage.CompanionFallback = _selection.CompanionFallback;
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
    private void SaveChoices_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _host.Session.Current.Usage.NativeProviders = new[] { ("codex", CodexBox.IsChecked), ("claude", ClaudeBox.IsChecked), ("cursor", CursorBox.IsChecked) }.Where(p => p.Item2 == true).Select(p => p.Item1).ToList();
            _host.Session.Current.Usage.IncludeWslCodex = WslBox.IsChecked == true;
            _host.Session.Current.Usage.CompanionFallback = FallbackBox.IsChecked == true;
            _host.Session.Save();
            _host.RestartUsage();
            Close();
        }
        catch (Exception) { ActionError.Text = "Could not save your choices. Check that the settings folder is writable."; }
    }
    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();
    private async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        BackButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        ConnectionTitle.Text = "Checking your provider accounts…";
        ConnectionDetail.Text = "Existing Windows sign-ins are read locally. Usage is fetched directly from the selected providers.";
        Readings.ItemsSource = null;
        try
        {
            var snapshot = _source is NativeUsageSource native ? await native.GetSnapshotAsync(_closed.Token, _selection) : await _source.GetSnapshotAsync(_closed.Token);
            if (_closed.IsCancellationRequested) return;
            _snapshot = snapshot;
            var state = UsageSetupStatus.From(snapshot, true, true);
            ConnectionTitle.Text = state.Title;
            ConnectionDetail.Text = state.Detail;
            Readings.ItemsSource = snapshot.Providers.Select(p => new {
                Name = p.DisplayName,
                Detail = (UsageSetupStatus.HasReading(p) ? "Reading available" : p.Error ?? "No supported usage reading yet") +
                    (p.FetchedAt is null ? " · update time unavailable" : $" · updated {p.FetchedAt.Value.ToLocalTime():g}")
            }).ToList();
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception) { _snapshot = null; ConnectionTitle.Text = "Couldn't finish the check"; ConnectionDetail.Text = "Open your provider app, sign in, and try again. Your app shortcuts are unaffected."; }
        finally
        {
            _checking = false;
            if (!_closed.IsCancellationRequested) { CheckButton.IsEnabled = true; ShowStep(); }
        }
    }
    private void Browse(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); ActionError.Text = ""; }
        catch (Exception) { ActionError.Text = "Couldn't open your browser. Use the setup guide in the repository for provider help."; }
    }
    private void Guide_Click(object sender, RoutedEventArgs e) => Browse("https://github.com/Belacosaur/BELAC-Dock/blob/main/USAGE_INTEGRATION.md");
    private void ProviderHelp_Click(object sender, RoutedEventArgs e)
    {
        var provider = (ProviderHelp.SelectedItem as ComboBoxItem)?.Tag as string;
        Browse(provider switch { "codex" => "https://chatgpt.com/codex", "claude" => "https://claude.ai/download", "cursor" => "https://cursor.com/downloads", _ => "https://github.com/Belacosaur/BELAC-Dock/blob/main/USAGE_INTEGRATION.md" });
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try { _host.OpenUsageApp(); ActionError.Text = "If no window appears, check the notification area beside the Windows clock."; }
        catch (Exception) { ActionError.Text = "Couldn't open OpenTokenUsage. Try opening it from Windows Start."; }
    }
}
