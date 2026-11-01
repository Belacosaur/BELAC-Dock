using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Belac.Dock.App.Views;

public sealed class ProviderRingControl : FrameworkElement
{
    public static readonly DependencyProperty OuterPercentProperty = DependencyProperty.Register(
        nameof(OuterPercent), typeof(double), typeof(ProviderRingControl), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, PercentChanged));

    public static readonly DependencyProperty InnerPercentProperty = DependencyProperty.Register(
        nameof(InnerPercent), typeof(double), typeof(ProviderRingControl), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, PercentChanged));

    public static readonly DependencyProperty OuterBrushProperty = DependencyProperty.Register(
        nameof(OuterBrush), typeof(Brush), typeof(ProviderRingControl), new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InnerBrushProperty = DependencyProperty.Register(
        nameof(InnerBrush), typeof(Brush), typeof(ProviderRingControl), new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MonogramProperty = DependencyProperty.Register(
        nameof(Monogram), typeof(string), typeof(ProviderRingControl), new FrameworkPropertyMetadata("?", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsStaleProperty = DependencyProperty.Register(
        nameof(IsStale), typeof(bool), typeof(ProviderRingControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayOuterProperty = DependencyProperty.Register(
        "DisplayOuter", typeof(double), typeof(ProviderRingControl), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty DisplayInnerProperty = DependencyProperty.Register(
        "DisplayInner", typeof(double), typeof(ProviderRingControl), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void PercentChanged(DependencyObject value, DependencyPropertyChangedEventArgs e)
    {
        var ring = (ProviderRingControl)value;
        var property = e.Property == OuterPercentProperty ? DisplayOuterProperty : DisplayInnerProperty;
        var target = (double)e.NewValue;
        var previous = (double)ring.GetValue(property);
        ring.BeginAnimation(property, null);
        ring.SetValue(property, target);
        if (ring.IsVisible && HoverMotion.AllowAnimation && SystemParameters.ClientAreaAnimation
            && double.IsFinite(previous) && double.IsFinite(target))
            ring.BeginAnimation(property, new DoubleAnimation(previous, target, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    static ProviderRingControl()
    {
        WidthProperty.OverrideMetadata(typeof(ProviderRingControl), new FrameworkPropertyMetadata(52d));
        HeightProperty.OverrideMetadata(typeof(ProviderRingControl), new FrameworkPropertyMetadata(52d));
    }

    public ProviderRingControl()
    {
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty ProviderNameProperty = DependencyProperty.Register(nameof(ProviderName), typeof(string), typeof(ProviderRingControl), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string ProviderName { get => (string)GetValue(ProviderNameProperty); set => SetValue(ProviderNameProperty, value); }

    public double OuterPercent
    {
        get => (double)GetValue(OuterPercentProperty);
        set => SetValue(OuterPercentProperty, value);
    }

    public double InnerPercent
    {
        get => (double)GetValue(InnerPercentProperty);
        set => SetValue(InnerPercentProperty, value);
    }

    public Brush OuterBrush
    {
        get => (Brush)GetValue(OuterBrushProperty);
        set => SetValue(OuterBrushProperty, value);
    }

    public Brush InnerBrush
    {
        get => (Brush)GetValue(InnerBrushProperty);
        set => SetValue(InnerBrushProperty, value);
    }

    public string Monogram
    {
        get => (string)GetValue(MonogramProperty);
        set => SetValue(MonogramProperty, value);
    }

    public bool IsStale
    {
        get => (bool)GetValue(IsStaleProperty);
        set => SetValue(IsStaleProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4)
            return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var track = BrushOr("Brush.Track", Brushes.DimGray);
        var outerRadius = size / 2 - 3;
        var innerRadius = outerRadius - 7;
        DrawTrack(context, center, outerRadius, track, 3.2);
        DrawTrack(context, center, innerRadius, track, 2.6);
        DrawArc(context, center, outerRadius, (double)GetValue(DisplayOuterProperty), OuterBrush, 3.2);
        DrawArc(context, center, innerRadius, (double)GetValue(DisplayInnerProperty), InnerBrush, 2.6);

        var foreground = BrushOr("Brush.Foreground", Brushes.White);
        var logoSize = size * .43;
        if (ProviderLogo.Draw(context, ProviderName, new Rect(center.X-logoSize/2, center.Y-logoSize/2, logoSize, logoSize), foreground)) return;
        var text = new FormattedText(
            string.IsNullOrWhiteSpace(Monogram) ? "?" : Monogram,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            12,
            IsStale ? BrushOr("Brush.Warn", Brushes.Goldenrod) : foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private Brush BrushOr(string key, Brush fallback)
    {
        try
        {
            return TryFindResource(key) as Brush ?? fallback;
        }
        catch (ResourceReferenceKeyNotFoundException)
        {
            return fallback;
        }
    }

    private static void DrawTrack(DrawingContext context, Point center, double radius, Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.DrawEllipse(null, pen, center, radius, radius);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double percent, Brush brush, double thickness)
    {
        var clamped = Core.Usage.RingMath.NormalizePercent(double.IsNaN(percent) ? null : percent);
        if (clamped is null or <= 0)
            return;

        var sweep = clamped.Value / 100d * 359.99;
        var start = -Math.PI / 2;
        var end = start + sweep * Math.PI / 180d;
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(PointOn(center, radius, start), false, false);
            figure.ArcTo(PointOn(center, radius, end), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOn(Point center, double radius, double radians) =>
        new(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
}
