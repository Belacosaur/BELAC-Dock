using System.Windows;
using System.Windows.Interop;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.App;

public static class DialogChrome
{
    public static readonly DependencyProperty DarkCaptionProperty = DependencyProperty.RegisterAttached(
        "DarkCaption", typeof(bool), typeof(DialogChrome), new PropertyMetadata(false, Changed));
    public static void SetDarkCaption(DependencyObject window, bool value) => window.SetValue(DarkCaptionProperty, value);
    public static bool GetDarkCaption(DependencyObject window) => (bool)window.GetValue(DarkCaptionProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window || !(bool)e.NewValue) return;
        window.SourceInitialized += (_, _) =>
        {
            var dark = SystemParameters.HighContrast ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,
                NativeMethods.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        };
    }
}
