using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Controls;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>Pieces of the Home dashboard: the Magestica brand band, a live "at a glance" strip and the open-source support card.</summary>
public static class HomeParts
{
    public const string Repo = "https://github.com/smtanbin/tool-box", Site = "https://www.magestica.com/";
    static readonly Color Burgundy = Color.FromArgb(255, 152, 31, 50), Deep = Color.FromArgb(255, 94, 18, 33);
    static SolidColorBrush White(byte a = 255) => new(Color.FromArgb(a, 255, 255, 255));

    static HyperlinkButton Link(string text, string url, Brush? fg = null) => new() { Content = text, NavigateUri = new Uri(url), Padding = new Thickness(0), Foreground = fg };

    static Button LinkButton(string glyph, string text, string url, bool accent = false)
    {
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = glyph, FontSize = 15 }, new TextBlock { Text = text } } },
            Padding = new Thickness(14, 8, 14, 9)
        };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        return b;
    }

    /// <summary>The Magestica band: the wordmark as text (no icon), who made the app, and a way to the website. Follows the system theme.</summary>
    public static UIElement Brand()
    {
        var word = Ui.BrandText("Magestica", 34); word.CharacterSpacing = -25;
        var tag = Ui.Hint("My Tool Box is designed and developed by Tanbin at Magestica. Software that stays out of your way.");
        var text = Ui.Stack(2, word, tag); text.VerticalAlignment = VerticalAlignment.Center;
        var visit = Ui.Markup<HyperlinkButton>($"<HyperlinkButton xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Foreground='{{ThemeResource BrandBrush}}' FontWeight='SemiBold' Content='www.magestica.com'/>");
        visit.NavigateUri = new Uri(Site); visit.VerticalAlignment = VerticalAlignment.Center;
        var stripe = Ui.Markup<Border>("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource BrandBrush}' Width='4' CornerRadius='2' HorizontalAlignment='Left' Margin='-12,0,0,0'/>");
        var g = new Grid { ColumnSpacing = 18, Margin = new Thickness(8, 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(text); Grid.SetColumn(visit, 1); g.Children.Add(visit);
        var wrap = new Grid(); wrap.Children.Add(stripe); wrap.Children.Add(g);
        var card = Ui.Card(wrap);
        card.Padding = new Thickness(22, 16, 20, 16);
        return card;
    }

    /// <summary>Free and open source: star, report, contribute, share.</summary>
    public static UIElement OpenSource()
    {
        var heart = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(40, 232, 72, 72)), VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon { Glyph = "", FontSize = 22, Foreground = new SolidColorBrush(Ui.Red) }
        };
        var share = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 15 }, new TextBlock { Text = "Copy link to share" } } }, Padding = new Thickness(14, 8, 14, 9) };
        share.Click += (_, _) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText("My Tool Box: free, private IT tools for Windows. " + Repo);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            share.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 15 }, new TextBlock { Text = "Copied" } } };
        };
        var buttons = Ui.Wrap(LinkButton("", "Star on GitHub", Repo, true), LinkButton("", "Report an issue", Repo + "/issues"), LinkButton("", "Contribute code", Repo + "/pulls"), share);
        var body = Ui.Stack(10,
            new TextBlock { Text = "Support open source", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] },
            Ui.Hint("My Tool Box is free and the source is open (Apache 2.0 with the Commons Clause). It has no ads, no accounts and no tracking. If it saves you time, the best ways to help are to star the project, tell us what is broken or missing, send a fix, or pass it on to a colleague."),
            buttons);
        var g = new Grid { ColumnSpacing = 16 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(heart); Grid.SetColumn(body, 1); g.Children.Add(body);
        var card = Ui.Card(g); card.Padding = new Thickness(20);
        return card;
    }

    /// <summary>Live CPU, memory, disk and network cards. Updates itself while the page is on screen.</summary>
    public sealed class LiveStrip : UserControl
    {
        readonly SparkChart _cpuChart = new() { Height = 44, Capacity = 40, Tint = Ui.Blue };
        readonly DualChart _netChart = new() { Height = 44, Capacity = 40, ColorA = Ui.Blue, ColorB = Ui.Green };
        readonly SparkChart _memChart = new() { Height = 44, Capacity = 40, Tint = Ui.Amber };
        readonly TextBlock _cpu = Ui.Value("-", 26), _cpuSub = Ui.Hint(""), _mem = Ui.Value("-", 26), _memSub = Ui.Hint(""), _disk = Ui.Value("-", 26), _diskSub = Ui.Hint(""), _netD = Ui.Value("-", 20), _netU = Ui.Value("-", 20), _netSub = Ui.Hint("");
        readonly ProgressBar _diskBar = new() { Maximum = 100 };
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        int _ticks; bool _busy;

        public LiveStrip()
        {
            Card Make(string glyph, Color c, string title, params UIElement[] items) => new(glyph, c, title, items);
            var cards = new UIElement[]
            {
                Make("", Ui.Blue, "Processor", _cpu, _cpuChart, _cpuSub).Build(),
                Make("", Ui.Amber, "Memory", _mem, _memChart, _memSub).Build(),
                Make("", Ui.Green, "Disk C:", _disk, _diskBar, _diskSub).Build(),
                Make("", Color.FromArgb(255, 6, 182, 212), "Network", Ui.Wrap(_netD, _netU), _netChart, _netSub).Build()
            };
            Content = Ui.Tiles(cards, 250);
            _timer.Tick += async (_, _) => await Tick();
            Loaded += async (_, _) => { _timer.Start(); await Tick(); };
            Unloaded += (_, _) => _timer.Stop();
            _netD.Foreground = new SolidColorBrush(Ui.Blue); _netU.Foreground = new SolidColorBrush(Ui.Green);
        }

        sealed record Card(string Glyph, Color Color, string Title, UIElement[] Items)
        {
            public UIElement Build()
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = Glyph, FontSize = 15, Foreground = new SolidColorBrush(Color) }, Ui.Hint(Title) } };
                var s = Ui.Stack(6, head); foreach (var i in Items) s.Children.Add(i);
                return Ui.Card(s);
            }
        }

        static string Rate(double b) => b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB/s" : b >= 1024 ? $"{b / 1024:0} KB/s" : $"{b:0} B/s";

        async Task Tick()
        {
            if (_busy) return; _busy = true;
            try
            {
                var c = await Backend.GetAsync("/api/hw/cpu");
                _cpu.Text = $"{c.Num("total"):0} %"; _cpuChart.Add(c.Num("total")); _cpuSub.Text = c.Num("speed") > 0 ? $"{c.Num("speed") / 1000:0.00} GHz · {c.Num("processes"):N0} processes" : "";
                var tot = c.Num("memTotal"); var used = c.Num("memUsed");
                if (tot > 0) { _mem.Text = $"{used * 100 / tot:0} %"; _memChart.Add(used * 100 / tot); _memSub.Text = $"{Json.Bytes(used)} of {Json.Bytes(tot)}"; }
                if (_ticks % 30 == 0)
                {
                    var d = (await Backend.GetAsync("/api/drives")).Items().FirstOrDefault(x => x.Str("path").StartsWith("C", StringComparison.OrdinalIgnoreCase));
                    if (d.ValueKind == JsonValueKind.Object && d.Num("total") > 0)
                    { var u = (d.Num("total") - d.Num("free")) * 100 / d.Num("total"); _disk.Text = $"{u:0} % used"; _diskBar.Value = u; _diskSub.Text = $"{Json.Bytes(d.Num("free"))} free of {Json.Bytes(d.Num("total"))}"; }
                }
                if (_ticks % 2 == 0)
                {
                    var p = await Backend.GetAsync("/api/hw/perf");
                    var best = p.Get("net").Items().OrderByDescending(n => n.Num("rx") + n.Num("tx")).FirstOrDefault();
                    if (best.ValueKind == JsonValueKind.Object) { _netChart.Add(best.Num("rx"), best.Num("tx")); _netD.Text = "▼ " + Rate(best.Num("rx")); _netU.Text = "▲ " + Rate(best.Num("tx")); _netSub.Text = ""; }
                }
                _ticks++;
            }
            catch { }
            _busy = false;
        }
    }
}
