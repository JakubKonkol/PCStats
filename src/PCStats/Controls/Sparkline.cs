using System.Windows;
using System.Windows.Media;

namespace PCStats.Controls;

/// <summary>
/// Lightweight history graph. Newest sample is drawn at the right edge; the horizontal
/// scale is fixed to <see cref="Capacity"/> samples so the line grows in from the right.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fixed upper bound, or NaN to auto-scale to the largest sample.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(90, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        var width = ActualWidth;
        var height = ActualHeight;
        if (values is null || values.Count < 2 || width <= 0 || height <= 0)
            return;

        var max = double.IsNaN(Maximum) ? values.Max() : Maximum;
        if (max <= 0)
            max = 1;

        var capacity = Math.Max(2, Capacity);
        var count = Math.Min(values.Count, capacity);
        var step = width / (capacity - 1);
        var startX = width - (count - 1) * step;
        const double inset = 1.5; // keep the stroke fully inside the bounds

        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var v = values[values.Count - count + i];
            var y = height - inset - Math.Clamp(v / max, 0, 1) * (height - 2 * inset);
            points[i] = new Point(startX + i * step, y);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
            ctx.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: true);
        }

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, height), isFilled: true, isClosed: true);
            ctx.PolyLineTo(points, isStroked: false, isSmoothJoin: true);
            ctx.LineTo(new Point(points[^1].X, height), isStroked: false, isSmoothJoin: false);
        }

        line.Freeze();
        area.Freeze();

        var color = (Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x55, color.R, color.G, color.B),
            Color.FromArgb(0x00, color.R, color.G, color.B),
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();

        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
    }
}
