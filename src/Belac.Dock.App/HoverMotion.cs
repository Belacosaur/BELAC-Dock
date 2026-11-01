using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Belac.Dock.App;

// Animate transforms, never layout: hover cannot push neighbouring targets away.
public static class HoverMotion
{
    public static bool AllowAnimation { get; set; } = true;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(HoverMotion), new PropertyMetadata(false, OnEnabledChanged));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void OnEnabledChanged(DependencyObject value, DependencyPropertyChangedEventArgs e)
    {
        if (value is not FrameworkElement element) return;
        element.MouseEnter -= Enter;
        element.MouseLeave -= Leave;
        element.PreviewMouseLeftButtonDown -= Press;
        element.PreviewMouseLeftButtonUp -= Release;
        if ((bool)e.NewValue)
        {
            element.RenderTransformOrigin = new Point(.5, .5);
            element.RenderTransform = new ScaleTransform();
            element.MouseEnter += Enter;
            element.MouseLeave += Leave;
            element.PreviewMouseLeftButtonDown += Press;
            element.PreviewMouseLeftButtonUp += Release;
        }
    }
    private static void Enter(object sender, MouseEventArgs e) => Scale(sender, 1.045);
    private static void Leave(object sender, MouseEventArgs e) => Scale(sender, 1);
    private static void Press(object sender, MouseButtonEventArgs e) => Scale(sender, .97);
    private static void Release(object sender, MouseButtonEventArgs e) => Scale(sender, 1.045);
    private static void Scale(object sender, double target)
    {
        if (sender is not FrameworkElement { RenderTransform: ScaleTransform transform }) return;
        var animate = AllowAnimation && SystemParameters.ClientAreaAnimation;
        var animation = new DoubleAnimation(animate ? target : 1, TimeSpan.FromMilliseconds(animate ? 150 : 0))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
