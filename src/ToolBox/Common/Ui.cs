using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolBox;

/// <summary>Small builders so the pages stay short: cards, key/value tables, colors.</summary>
public static class Ui
{
    public static readonly Color Blue = Color.FromArgb(255, 0, 120, 212);
    public static readonly Color Green = Color.FromArgb(255, 16, 185, 129);
    public static readonly Color Amber = Color.FromArgb(255, 245, 158, 11);
    public static readonly Color Red = Color.FromArgb(255, 232, 72, 72);

    /// <summary>A brush of the current Windows theme (light or dark). Looked up per call, so pages built after a theme change get the new colours.</summary>
    public static Brush ThemeBrush(string key)
    {
        var theme = (App.Window?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        if (Find(Application.Current.Resources, key, theme) is Brush b) return b;
        return (Brush)Application.Current.Resources[key];
    }

    static object? Find(ResourceDictionary d, string key, string theme)
    {
        if (d.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary t && t.TryGetValue(key, out var v)) return v;
        foreach (var m in d.MergedDictionaries) if (Find(m, key, theme) is { } r) return r;
        return null;
    }

    const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";

    /// <summary>Builds an element from markup so brushes can be {ThemeResource}s and follow the system theme.</summary>
    public static T Markup<T>(string xaml) => (T)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);

    public static Border Card(UIElement content, Thickness? margin = null)
    {
        var b = Markup<Border>($"<Border {Ns} Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}' BorderThickness='1' CornerRadius='8' Padding='16'/>");
        b.Margin = margin ?? new Thickness(0);
        b.Child = content;
        return b;
    }

    /// <summary>Text in the Magestica brand colour (burgundy in light mode, a lighter rose in dark mode).</summary>
    public static TextBlock BrandText(string text, double size, bool bold = true)
    {
        var t = Markup<TextBlock>($"<TextBlock {Ns} Foreground='{{ThemeResource BrandBrush}}'/>");
        t.Text = text; t.FontSize = size; if (bold) t.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        return t;
    }

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    public static TextBlock Hint(string text)
    {
        var t = Markup<TextBlock>($"<TextBlock {Ns} Foreground='{{ThemeResource TextFillColorSecondaryBrush}}' FontSize='12' TextWrapping='Wrap'/>");
        t.Text = text;
        return t;
    }

    public static TextBlock Value(string text, double size = 26) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    /// <summary>Two-column key/value table; rows with an empty value are skipped.</summary>
    public static Grid Kv(IEnumerable<(string k, string v)> rows)
    {
        var g = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var i = 0;
        foreach (var (k, v) in rows)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var kt = Hint(k);
            kt.FontSize = 13;
            var vt = new TextBlock { Text = v, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(kt, i); Grid.SetRow(vt, i); Grid.SetColumn(vt, 1);
            g.Children.Add(kt); g.Children.Add(vt);
            i++;
        }
        return g;
    }

    public static StackPanel Stack(double spacing = 12, params UIElement[] items)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var e in items) s.Children.Add(e);
        return s;
    }

    public static ScrollViewer Page(UIElement content) => new()
    {
        Padding = new Thickness(24, 8, 24, 24),
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    /// <summary>Controls in a row that wraps onto more lines when the window is narrow.</summary>
    public static Controls.WrapPanel Wrap(params UIElement[] items)
    {
        var p = new Controls.WrapPanel();
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    /// <summary>Wrapping grid of equal cards, stretched to the page width.</summary>
    public static ItemsRepeater Tiles(IEnumerable<UIElement> items, double minWidth = 360) => new()
    {
        ItemsSource = items.ToList(),
        Layout = new UniformGridLayout { MinItemWidth = minWidth, MinColumnSpacing = 12, MinRowSpacing = 12, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
    };

    public static Color TempColor(double c, double warn = 0, double crit = 0)
    {
        var hi = crit > 0 ? crit : 100;
        var wn = warn > 0 ? warn : hi - 15;
        return c >= hi - 5 ? Red : c >= wn - 10 ? Amber : Green;
    }
}
