using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ToolBox.Controls;

/// <summary>A small live line chart with a grid and a filled area. Values are pushed in with Add().</summary>
public sealed class SparkChart : Grid
{
    readonly List<double> _data = new();
    readonly Canvas _canvas = new();
    readonly Polyline _line = new() { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
    readonly Polygon _fill = new();
    readonly Microsoft.UI.Xaml.Shapes.Path _grid = new() { StrokeThickness = 1, Opacity = 0.3 };
    Color _color = Color.FromArgb(255, 0, 120, 212);

    public int Capacity { get; set; } = 60;
    public double Max { get; set; } = 100;
    public bool AutoMax { get; set; }
    public IReadOnlyList<double> Data => _data;

    public Color Tint
    {
        get => _color;
        set { _color = value; Apply(); }
    }

    public SparkChart()
    {
        Height = 90;
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));
        Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128));
        _canvas.Children.Add(_grid);
        _canvas.Children.Add(_fill);
        _canvas.Children.Add(_line);
        Children.Add(_canvas);
        SizeChanged += (_, _) => Redraw();
        Apply();
    }

    void Apply()
    {
        _line.Stroke = new SolidColorBrush(_color);
        _fill.Fill = new SolidColorBrush(Color.FromArgb(45, _color.R, _color.G, _color.B));
        _grid.Stroke = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    public void Add(double v)
    {
        _data.Add(v);
        while (_data.Count > Capacity) _data.RemoveAt(0);
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
        if (_data.Count < 2) { _line.Points.Clear(); _fill.Points.Clear(); return; }
        var top = Max;
        if (AutoMax) top = Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(1, _data.Max()))));
        var step = w / (Capacity - 1);
        var x0 = w - (_data.Count - 1) * step;
        var pts = new PointCollection();
        for (var i = 0; i < _data.Count; i++)
            pts.Add(new Point(x0 + i * step, h - Math.Clamp(_data[i] / top, 0, 1) * (h - 2)));
        _line.Points = pts;
        var poly = new PointCollection();
        foreach (var p in pts) poly.Add(p);
        poly.Add(new Point(w, h));
        poly.Add(new Point(x0, h));
        _fill.Points = poly;
    }
}
