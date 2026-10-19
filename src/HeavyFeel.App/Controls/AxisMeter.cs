using System.Windows;
using System.Windows.Media;

namespace HeavyFeel.App.Controls;

/// <summary>
/// Centre-zero bar for one control axis. The filled bar is what the sim gets;
/// the thin tick is where the physical stick is. The gap between them is the
/// lag HeavyPro adds.
/// </summary>
public sealed class AxisMeter : FrameworkElement
{
    public static readonly DependencyProperty InputProperty = DependencyProperty.Register(
        nameof(Input), typeof(double), typeof(AxisMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutputProperty = DependencyProperty.Register(
        nameof(Output), typeof(double), typeof(AxisMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(AxisMeter),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Track = Frozen(Color.FromRgb(0x24, 0x24, 0x24));
    private static readonly Pen Centre = FrozenPen(Color.FromRgb(0x50, 0x50, 0x50), 1);
    private static readonly Pen Tick = FrozenPen(Color.FromRgb(0xF2, 0xF2, 0xF2), 2);

    public double Input
    {
        get => (double)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double Output
    {
        get => (double)GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 14);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var mid = w / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), 3, 3);

        var output = Clamp(Output) * mid;
        var left = Math.Min(mid, mid + output);
        dc.DrawRoundedRectangle(BarBrush, null, new Rect(left, 2, Math.Abs(output), h - 4), 2, 2);

        dc.DrawLine(Centre, new Point(mid, 0), new Point(mid, h));
        var x = mid + Clamp(Input) * mid;
        dc.DrawLine(Tick, new Point(x, -2), new Point(x, h + 2));
    }

    private static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Max(-1, Math.Min(1, v));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(Frozen(c), thickness);
        p.Freeze();
        return p;
    }
}
