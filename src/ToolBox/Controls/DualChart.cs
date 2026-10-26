using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ToolBox.Controls;

/// <summary>Two filled lines on one grid (download and upload) that scales itself to the highest value seen.</summary>
public sealed class DualChart : Grid
{
    readonly Canvas _canvas = new();
    readonly Microsoft.UI.Xaml.Shapes.Path _grid = new() { StrokeThickness = 1, Opacity = 0.3, Stroke = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)) };
    readonly Polygon _fillA = new(), _fillB = new();
    readonly Polyline _lineA = new() { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round }, _lineB = new() { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
    readonly List<double> _a = new(), _b = new();
    public int Capacity { get; set; } = 90;
    public double Peak { get; private set; }

    public Color ColorA { set { _lineA.Stroke = new SolidColorBrush(value); _fillA.Fill = new SolidColorBrush(Color.FromArgb(55, value.R, value.G, value.B)); } }
    public Color ColorB { set { _lineB.Stroke = new SolidColorBrush(value); _fillB.Fill = new SolidColorBrush(Color.FromArgb(55, value.R, value.G, value.B)); } }

    public DualChart()
    {
        Height = 190; CornerRadius = new CornerRadius(6); BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)); Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128));
        _canvas.Children.Add(_grid); _canvas.Children.Add(_fillA); _canvas.Children.Add(_fillB); _canvas.Children.Add(_lineA); _canvas.Children.Add(_lineB);
        Children.Add(_canvas);
        SizeChanged += (_, _) => Redraw();
    }

    public void Clear() { _a.Clear(); _b.Clear(); Peak = 0; Redraw(); }

    public void Add(double a, double b)
    {
        _a.Add(a); _b.Add(b);
        while (_a.Count > Capacity) { _a.RemoveAt(0); _b.RemoveAt(0); }
        Peak = Math.Max(Peak, Math.Max(a, b));
        Redraw();
    }

    void Redraw()
    {
        double w = ActualWidth - 2, h = ActualHeight - 2;
        if (w <= 0 || h <= 0) return;
        var g = new GeometryGroup();
        for (var i = 1; i < 10; i++) g.Children.Add(new LineGeometry { StartPoint = new Point(w * i / 10, 0), EndPoint = new Point(w * i / 10, h) });
        for (var i = 1; i < 4; i++) g.Children.Add(new LineGeometry { StartPoint = new Point(0, h * i / 4), EndPoint = new Point(w, h * i / 4) });
        _grid.Data = g;
        if (_a.Count < 2) { _lineA.Points.Clear(); _lineB.Points.Clear(); _fillA.Points.Clear(); _fillB.Points.Clear(); return; }
        var top = Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(4096, Math.Max(_a.Max(), _b.Max()) * 1.1))));
        Draw(_a, _lineA, _fillA, w, h, top); Draw(_b, _lineB, _fillB, w, h, top);
    }

    void Draw(List<double> d, Polyline line, Polygon fill, double w, double h, double top)
    {
        var step = w / (Capacity - 1); var x0 = w - (d.Count - 1) * step;
        var pts = new PointCollection();
        for (var i = 0; i < d.Count; i++) pts.Add(new Point(x0 + i * step, h - Math.Clamp(d[i] / top, 0, 1) * (h - 2)));
        line.Points = pts;
        var poly = new PointCollection(); foreach (var p in pts) poly.Add(p);
        poly.Add(new Point(w, h)); poly.Add(new Point(x0, h));
        fill.Points = poly;
    }
}
