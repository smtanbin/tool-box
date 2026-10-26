using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ToolBox.Pages;

/// <summary>Disk health in the look of the earlier web page: gradient health and temperature tiles on the left, name and DRAM badge on top, details in columns.</summary>
public sealed class DiskPage : ToolPage
{
    static Button MakeRefresh(out Button b) => b = new Button { Content = "Refresh" };

    public DiskPage() : base("Disk info", MakeRefresh(out var refresh))
    {
        refresh.Click += async (_, _) => await LoadAsync();
        Loaded += async (_, _) => await LoadAsync();
    }

    async Task LoadAsync()
    {
        Body.Children.Clear();
        Body.Children.Add(new ProgressBar { IsIndeterminate = true });
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new ProgressRing { Width = 22, Height = 22, IsActive = true }, Ui.Hint("Reading drives...") } });
        try
        {
            var r = await Backend.GetAsync("/api/disks");
            Body.Children.Clear();
            if (r.Get("admin").ValueKind != JsonValueKind.True)
                Body.Children.Add(Ui.Hint("Not running as administrator: SMART details may be limited."));
            foreach (var d in r.Get("disks").Items()) Body.Children.Add(Card(d));
            if (!r.Get("disks").Items().Any()) Body.Children.Add(Ui.Hint("No physical drives found."));
        }
        catch (Exception e) { Body.Children.Clear(); Body.Children.Add(Ui.Hint(e.Message)); }
    }

    static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));

    /// <summary>A rounded gradient tile: small caption, big value, small footer.</summary>
    static Border Tile(string caption, string value, string footer, byte[] from, byte[] to) => new()
    {
        CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), MinWidth = 190, MinHeight = 92,
        BorderBrush = Brush(255, 255, 255, 60), BorderThickness = new Thickness(1),
        Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, from[0], from[1], from[2]), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, to[0], to[1], to[2]), Offset = 1 }
            }
        },
        Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 2,
            Children =
            {
                new TextBlock { Text = caption, FontSize = 12, Foreground = Brush(255, 255, 255, 235), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = value, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(255, 255, 255), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = footer, FontSize = 12, Foreground = Brush(255, 255, 255, 235), HorizontalAlignment = HorizontalAlignment.Center }
            }
        }
    };

    static Border Pill(string text, byte r, byte g, byte b) => new()
    {
        CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 2, 10, 3), Background = Brush(r, g, b), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(255, 255, 255) }
    };

    static UIElement Cell(string key, string value)
    {
        var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 6, 0, 6), BorderBrush = Brush(128, 128, 128, 50), BorderThickness = new Thickness(0, 0, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var k = Ui.Hint(key); k.FontSize = 13; k.VerticalAlignment = VerticalAlignment.Center;
        var v = new TextBlock { Text = value, FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v, 1); g.Children.Add(k); g.Children.Add(v);
        return g;
    }

    static string Dec(double v) => v >= 1e12 ? $"{v / 1e12:0.##} TB" : $"{v / 1e9:0.#} GB";

    static UIElement Card(JsonElement d)
    {
        var health = d.Str("health"); var temp = d.Num("temp"); var size = d.Num("size");
        var (from, to) = health switch
        {
            "Good" => (new byte[] { 21, 101, 192 }, new byte[] { 66, 153, 235 }),
            "Caution" => (new byte[] { 180, 110, 0 }, new byte[] { 240, 170, 40 }),
            "Bad" => (new byte[] { 170, 30, 30 }, new byte[] { 232, 84, 84 }),
            _ => (new byte[] { 90, 98, 112 }, new byte[] { 140, 148, 162 })
        };
        var tiles = Ui.Stack(8, Tile("Health status", health, d.Num("healthPct") > 0 ? $"{d.Num("healthPct"):0} %" : d.Str("source").Length > 0 ? "" : "no SMART data", from, to));
        if (temp > 0)
        {
            var hot = temp >= (d.Num("tempWarn") > 0 ? d.Num("tempWarn") : 70);
            tiles.Children.Add(Tile("Temperature", $"{temp:0} °C", $"{temp * 9 / 5 + 32:0} °F",
                hot ? new byte[] { 180, 110, 0 } : new byte[] { 21, 101, 192 }, hot ? new byte[] { 240, 170, 40 } : new byte[] { 66, 153, 235 }));
        }
        tiles.VerticalAlignment = VerticalAlignment.Top;

        var name = $"{d.Str("vendor")} {d.Str("model")}".Trim();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = name.Length > 0 ? name : "Disk", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Ui.Hint($"{size / (1L << 30):0} GB · Disk {d.Num("index")}"));

        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        (string text, byte r, byte g, byte b)? p = d.Str("dram") switch
        {
            "dram" => ("DRAM cache", 46, 125, 50), "dram-likely" => ("Probably DRAM cache", 46, 125, 50),
            "dramless" => ("DRAM-less", 191, 100, 0), "dramless-likely" => ("Probably DRAM-less", 191, 100, 0),
            "unknown" => ("DRAM unknown", 110, 118, 132), _ => null
        };
        if (p is { } pp) badge.Children.Add(Pill(pp.text, pp.r, pp.g, pp.b));
        if (d.Str("dramNote").Length > 0) badge.Children.Add(Ui.Hint(d.Str("dramNote")));

        var ctrl = d.Str("controller").Length > 0 ? d.Str("controller") : d.Str("controllerVendor").Length > 0 ? d.Str("controllerVendor") + " (model unknown)" : "";
        var hours = d.Num("powerHours");
        var cells = new List<(string, string)>
        {
            ("Firmware", d.Str("firmware")), ("Serial number", d.Str("serial")), ("Interface", d.Str("bus")), ("Manufacturer", d.Str("maker")), ("Controller", ctrl),
            ("Controller maker", d.Str("controllerVendor")), ("Media type", d.Str("media") switch { "SSD" => "SSD (solid state)", "HDD" => "HDD (spinning)", var m => m }),
            ("Capacity", size > 0 ? $"{size / (1L << 30):0} GB · {Dec(size)} (decimal)" : ""), ("Partition style", d.Str("style")),
            ("Drive letters", string.Join(" ", d.Get("letters").Items().Select(x => x.GetString()))),
            ("Power-on count", d.Num("powerCount") > 0 ? $"{d.Num("powerCount"):N0} times" : ""), ("Power-on hours", hours > 0 ? $"{hours:N0} h ({hours / 24:N0} days)" : ""),
            ("Host reads", d.Num("hostReads") > 0 ? Json.Bytes(d.Num("hostReads")) : ""), ("Host writes", d.Num("hostWrites") > 0 ? Json.Bytes(d.Num("hostWrites")) : ""), ("Health source", d.Str("source"))
        }.Where(c => c.Item2.Length > 0).Select(c => Cell(c.Item1, c.Item2));

        var right = Ui.Stack(8, title);
        if (badge.Children.Count > 0) right.Children.Add(badge);
        right.Children.Add(Ui.Tiles(cells, 330));
        foreach (var reason in d.Get("reasons").Items()) right.Children.Add(Ui.Hint("• " + reason.GetString()));
        foreach (var note in d.Get("notes").Items()) right.Children.Add(Ui.Hint(note.GetString() ?? ""));

        var parts = d.Get("parts").Items().ToList();
        if (parts.Count > 0)
        {
            var pe = new Expander { Header = $"Partitions ({parts.Count})", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            pe.Content = Ui.Kv(parts.Select(x => ($"#{x.Num("num")} {x.Str("letter")} {x.Str("label")}".Trim(),
                $"{Json.Bytes(x.Num("size"))}  {x.Str("fs")}  {x.Str("type")}" + (x.Num("free") > 0 ? $"  ·  {Json.Bytes(x.Num("free"))} free" : ""))));
            right.Children.Add(pe);
        }
        var attrs = d.Get("attrs").Items().ToList();
        if (attrs.Count > 0)
        {
            var ae = new Expander { Header = $"SMART attributes ({attrs.Count})", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ae.Content = Ui.Kv(attrs.Select(a => ($"{a.Str("id")}  {a.Str("name")}", $"{a.Str("raw")}   (value {a.Num("cur")}, worst {a.Num("worst")}, threshold {a.Num("thr")})  {a.Str("status")}")));
            right.Children.Add(ae);
        }

        var root = new Grid { ColumnSpacing = 18 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(tiles);
        Grid.SetColumn(right, 1); root.Children.Add(right);
        return Ui.Card(root);
    }
}
