using System.Windows.Media;
using System.Windows;
using System.Windows.Data;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;

namespace Belac.Dock.App;

public static class Palette
{
    private static DockTheme _theme = DockThemes.Find(null);

    public static void Select(string? themeId)
    {
        _theme = DockThemes.Find(themeId);
        Apply(false);
    }

    public static void Apply(bool highContrast)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Apply(highContrast));
            return;
        }
        if (highContrast || System.Windows.SystemParameters.HighContrast)
            ApplySet(
                System.Windows.SystemColors.WindowColor,
                System.Windows.SystemColors.ControlColor,
                System.Windows.SystemColors.HighlightColor,
                System.Windows.SystemColors.WindowFrameColor,
                System.Windows.SystemColors.WindowFrameColor,
                System.Windows.SystemColors.WindowTextColor,
                System.Windows.SystemColors.GrayTextColor,
                System.Windows.SystemColors.HotTrackColor,
                System.Windows.SystemColors.HotTrackColor,
                System.Windows.SystemColors.GrayTextColor,
                System.Windows.SystemColors.ActiveCaptionTextColor,
                System.Windows.SystemColors.GrayTextColor,
                System.Windows.SystemColors.ControlColor);
        else
        {
            Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
            ApplySet(
                Parse(_theme.Background),
                Parse(_theme.Surface),
                Parse(_theme.Hover),
                Parse(_theme.Hover),
                Parse(_theme.Border),
                Color.FromRgb(0xF0, 0xF3, 0xEE),
                Color.FromRgb(0x97, 0xA4, 0xA4),
                Parse(_theme.Accent),
                Parse(_theme.Accent),
                Color.FromRgb(0xE9, 0xBB, 0x77),
                Color.FromRgb(0xE7, 0x80, 0x80),
                Parse(_theme.Border),
                Parse(_theme.Surface));
        }
    }

    public static SolidColorBrush BandBrush(RingBand band, bool inner)
    {
        var key = band switch
        {
            RingBand.Caution => "Brush.Warn",
            RingBand.Critical => "Brush.Danger",
            RingBand.Unknown => "Brush.Track",
            _ => inner ? "Brush.Good" : "Brush.Accent"
        };
        return (SolidColorBrush)System.Windows.Application.Current.Resources[key];
    }

    private static void ApplySet(Color background, Color surface, Color hover, Color border, Color borderStrong, Color foreground, Color muted, Color accent, Color good, Color warn, Color danger, Color track, Color flyout)
    {
        var resources = System.Windows.Application.Current.Resources;
        Set(resources, "Brush.Background", background);
        Set(resources, "Brush.Surface", surface);
        Set(resources, "Brush.Hover", hover);
        Set(resources, "Brush.Border", border);
        Set(resources, "Brush.BorderStrong", borderStrong);
        Set(resources, "Brush.Foreground", foreground);
        Set(resources, "Brush.Muted", muted);
        Set(resources, "Brush.Accent", accent);
        Set(resources, "Brush.Good", good);
        Set(resources, "Brush.Warn", warn);
        Set(resources, "Brush.Danger", danger);
        Set(resources, "Brush.Track", track);
        Set(resources, "Brush.Flyout", flyout);
    }

    private static void Set(System.Windows.ResourceDictionary resources, string key, Color color)
    {
        // WPF can freeze brushes consumed by templates. A bound colour keeps the
        // shared brush live, including brushes already used by custom ring drawings.
        var sourceKey = key + ".ColorSource";
        if (resources[sourceKey] is PaletteColor existing)
        {
            existing.Color = color;
            return;
        }
        var source = new PaletteColor { Color = color };
        var brush = new SolidColorBrush();
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Color)) { Source = source });
        resources[sourceKey] = source;
        resources[key] = brush;
    }

    private sealed class PaletteColor : DependencyObject
    {
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(PaletteColor));
        public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    }
}
