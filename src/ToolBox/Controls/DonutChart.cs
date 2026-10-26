using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ToolBox.Controls;

/// <summary>A donut chart with a total in the middle.</summary>
public sealed class DonutChart : Grid
{
    readonly Canvas _canvas = new() { Width = 210, Height = 210 };
    readonly TextBlock _total = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _caption = new() { FontSize = 12, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center };

    public DonutChart()
    {
        Width = 210; Height = 210;
        Children.Add(_canvas);
        Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { _total, _caption } });
    }

    public void SetData(IReadOnlyList<(double value, Color color)> parts, string total, string caption)
    {
        _canvas.Children.Clear(); _total.Text = total; _caption.Text = caption;
        const double cx = 105, cy = 105, ro = 102, ri = 62;
        var sum = parts.Sum(p => p.value);
        if (sum <= 0) return;
        double a0 = -Math.PI / 2;
        foreach (var (value, color) in parts)
        {
            var sweep = value / sum * Math.PI * 2;
            if (sweep < 0.002) continue;
            Windows.UI.Color c = color;
            if (sweep > Math.PI * 2 - 0.001) sweep = Math.PI * 2 - 0.001;
            var a1 = a0 + sweep;
            Point P(double r, double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var large = sweep > Math.PI;
            var fig = new PathFigure { StartPoint = P(ro, a0), IsClosed = true };
            fig.Segments.Add(new ArcSegment { Point = P(ro, a1), Size = new Size(ro, ro), SweepDirection = SweepDirection.Clockwise, IsLargeArc = large });
            fig.Segments.Add(new LineSegment { Point = P(ri, a1) });
            fig.Segments.Add(new ArcSegment { Point = P(ri, a0), Size = new Size(ri, ri), SweepDirection = SweepDirection.Counterclockwise, IsLargeArc = large });
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geo, Fill = new SolidColorBrush(c), Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), StrokeThickness = 1 });
            a0 = a1;
        }
    }
}
