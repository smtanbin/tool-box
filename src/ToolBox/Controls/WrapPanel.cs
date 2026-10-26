using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ToolBox.Controls;

/// <summary>Lays children out in a row and starts a new line when the next one would not fit.</summary>
public sealed class WrapPanel : Panel
{
    public double HSpacing { get; set; } = 8;
    public double VSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, lineH = 0, maxW = 0;
        var limit = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        foreach (var c in Children)
        {
            c.Measure(new Size(limit, available.Height));
            var s = c.DesiredSize;
            if (x > 0 && x + s.Width > limit) { y += lineH + VSpacing; x = 0; lineH = 0; }
            x += s.Width + HSpacing; lineH = Math.Max(lineH, s.Height); maxW = Math.Max(maxW, x - HSpacing);
        }
        return new Size(maxW, y + lineH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineH = 0;
        foreach (var c in Children)
        {
            var s = c.DesiredSize;
            if (x > 0 && x + s.Width > finalSize.Width) { y += lineH + VSpacing; x = 0; lineH = 0; }
            c.Arrange(new Rect(x, y, Math.Min(s.Width, finalSize.Width), s.Height));
            x += s.Width + HSpacing; lineH = Math.Max(lineH, s.Height);
        }
        return finalSize;
    }
}
