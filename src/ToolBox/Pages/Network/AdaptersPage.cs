using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolBox.Controls;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>Network adapters: pick one on the left and see its live download / upload graph and settings on the right.</summary>
public sealed class AdaptersPage : Page
{
    List<JsonElement> _all = new();
    JsonElement? _sel;
    bool _onlyUp = true; string _q = "";
    double _totalRx, _totalTx; DateTime _last = DateTime.UtcNow;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _busy;

    readonly StackPanel _list = new() { Spacing = 4 };
    readonly StackPanel _detail = new() { Spacing = 12 };
    readonly DualChart _chart = new() { ColorA = Ui.Blue, ColorB = Ui.Green };
    readonly TextBlock _down = Ui.Value("-", 24), _up = Ui.Value("-", 24), _stats = Ui.Hint("");
    readonly TextBlock _msg = Ui.Hint("");
    readonly Grid _body = new() { ColumnSpacing = 12, RowSpacing = 12 };
    readonly ScrollViewer _detailScroll;

    static readonly Color Down = Ui.Blue, Up = Ui.Green;

    public AdaptersPage()
    {
        var root = new Grid { Padding = new Thickness(24, 8, 24, 12), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock { Text = "Network adapters", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });

        var q = new TextBox { PlaceholderText = "Filter by name, MAC or IP", MinWidth = 260 };
        q.TextChanged += (_, _) => { _q = q.Text.Trim().ToLowerInvariant(); RenderList(); };
        var up = new CheckBox { Content = "Only adapters that are up", IsChecked = true };
        up.Click += (_, _) => { _onlyUp = up.IsChecked == true; RenderList(); };
        var refresh = new Button { Content = "Refresh" }; refresh.Click += async (_, _) => await LoadAsync();
        var bar = Ui.Wrap(q, up, refresh, _msg); Grid.SetRow(bar, 1); root.Children.Add(bar);

        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340), MinWidth = 260 });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var listScroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var listCard = Ui.Card(listScroll); listCard.Padding = new Thickness(8);
        _body.Children.Add(listCard);
        _detailScroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(_detailScroll, 1); _body.Children.Add(_detailScroll);
        Grid.SetRow(_body, 2); root.Children.Add(_body);
        Content = root;

        // narrow windows: the list goes above the details
        root.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 820;
            _body.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(340);
            _body.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            _body.RowDefinitions.Clear();
            if (narrow) { _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) }); _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); }
            Grid.SetColumn(_detailScroll, narrow ? 0 : 1); Grid.SetRow(_detailScroll, narrow ? 1 : 0);
        };

        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => { await LoadAsync(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    async Task LoadAsync()
    {
        try
        {
            _all = (await Backend.GetAsync("/api/net/adapters")).Get("adapters").Items().ToList();
            _msg.Text = $"{_all.Count} adapters";
            _sel ??= _all.FirstOrDefault(a => a.Str("status") == "Up" && a.Str("type") != "Loopback" && a.Get("gateways").Items().Any());
            RenderList(); RenderDetail();
        }
        catch (Exception e) { _msg.Text = e.Message; }
    }

    static readonly System.Text.RegularExpressions.Regex Plumbing = new(@"-(WFP|QoS)|Filter-\d{4}|Virtual Switch Extension|Packet Scheduler|Native MAC Layer", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // "only up" also hides the driver plumbing Windows lists as adapters (WFP, QoS, Hyper-V switch filters)
    IEnumerable<JsonElement> Filtered() => _all.Where(a => (!_onlyUp || (a.Str("status") == "Up" && a.Str("type") != "Loopback" && !Plumbing.IsMatch(a.Str("name") + " " + a.Str("description"))))
        && (_q.Length == 0 || new[] { a.Str("name"), a.Str("description"), a.Str("mac") }.Concat(a.Get("addrs").Items().Select(x => x.Str("ip"))).Any(s => s.ToLowerInvariant().Contains(_q))));

    void RenderList()
    {
        _list.Children.Clear();
        foreach (var a in Filtered())
        {
            var item = a; var up = a.Str("status") == "Up"; var selected = _sel is { } s && s.Str("guid") == a.Str("guid");
            var v4 = a.Get("addrs").Items().FirstOrDefault(x => x.Get("v6").ValueKind != JsonValueKind.True);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = a.Str("type") switch { "Wi-Fi" => "", "Ethernet" => "", "Loopback" => "", _ => "" };
            var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(40, up ? Ui.Blue.R : (byte)140, up ? Ui.Blue.G : (byte)140, up ? Ui.Blue.B : (byte)140)), VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = new SolidColorBrush(up ? Ui.Blue : Color.FromArgb(255, 140, 140, 140)) } };
            row.Children.Add(tile);
            var text = Ui.Stack(0, Ui.Title(a.Str("name")), Ui.Hint(v4.ValueKind == JsonValueKind.Object ? $"{v4.Str("ip")}  ·  {a.Str("type")}" : $"{a.Str("type")}  ·  {a.Str("status")}"));
            ((TextBlock)text.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(text, 1); row.Children.Add(text);
            var b = new Border { Child = row, Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(selected ? Color.FromArgb(50, 59, 142, 240) : Microsoft.UI.Colors.Transparent) };
            b.Tapped += (_, _) => { _sel = item; _chart.Clear(); _totalRx = _totalTx = 0; RenderList(); RenderDetail(); };
            _list.Children.Add(b);
        }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Hint("No adapters match."));
    }

    static string Speed(double b) => b <= 0 ? "-" : b >= 1e9 ? $"{b / 1e9:0.#} Gbps" : $"{b / 1e6:0} Mbps";
    static string Mask(int p) => string.Join(".", Enumerable.Range(0, 4).Select(i => Math.Clamp(p - 8 * i, 0, 8) is var n ? (256 - (1 << (8 - n))).ToString() : ""));
    static string Rate(double b) => b >= 1 << 20 ? $"{b / (1 << 20):0.00} MB/s" : b >= 1024 ? $"{b / 1024:0.0} KB/s" : $"{b:0} B/s";

    void RenderDetail()
    {
        _detail.Children.Clear();
        if (_sel is not { } a) { _detail.Children.Add(Ui.Hint("Select an adapter.")); return; }
        var up = a.Str("status") == "Up";

        // header
        var head = new Grid(); head.Children.Add(Ui.Stack(0, new TextBlock { Text = a.Str("name"), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, Ui.Hint(a.Str("description"))));
        var pill = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 2, 10, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(up ? Color.FromArgb(255, 46, 125, 50) : Color.FromArgb(255, 110, 118, 132)), Child = new TextBlock { Text = a.Str("status"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
        head.Children.Add(pill);

        // live throughput
        Border Legend(string arrow, string label, TextBlock val, Color c) => new() { Padding = new Thickness(0, 0, 18, 0), Child = Ui.Stack(0, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = arrow, Foreground = new SolidColorBrush(c), FontSize = 14 }, Ui.Hint(label) } }, val) };
        var legend = Ui.Wrap(Legend("▼", "Download", _down, Down), Legend("▲", "Upload", _up, Up));
        _down.Foreground = new SolidColorBrush(Down); _up.Foreground = new SolidColorBrush(Up);
        _detail.Children.Add(Ui.Card(Ui.Stack(10, head, legend, _chart, _stats)));

        // settings
        var v4 = a.Get("addrs").Items().Where(x => x.Get("v6").ValueKind != JsonValueKind.True).ToList();
        var v6 = a.Get("addrs").Items().Where(x => x.Get("v6").ValueKind == JsonValueKind.True).ToList();
        var rows = new List<(string, string)> { ("Type", a.Str("type")), ("Speed", Speed(a.Num("speed"))), ("MAC address", a.Str("mac")), ("MTU", a.Num("mtu") > 0 ? a.Num("mtu").ToString() : "") };
        foreach (var x in v4) { rows.Add(("IPv4 address", $"{x.Str("ip")}/{x.Num("prefix")}")); rows.Add(("Subnet mask", Mask((int)x.Num("prefix")))); }
        rows.Add(("Default gateway", string.Join(", ", a.Get("gateways").Items().Select(g => g.GetString()))));
        rows.Add(("DNS servers", string.Join(", ", a.Get("dns").Items().Select(g => g.GetString()))));
        rows.Add(("DHCP", a.Get("dhcp").ValueKind == JsonValueKind.True ? "Enabled" + (a.Str("dhcpServer").Length > 0 ? $" ({a.Str("dhcpServer")})" : "") : "Static (off)"));
        rows.Add(("DNS suffix", a.Str("dnsSuffix")));
        foreach (var x in v6.Where(x => !x.Str("ip").StartsWith("fe80", StringComparison.OrdinalIgnoreCase))) rows.Add(("IPv6 address", $"{x.Str("ip")}/{x.Num("prefix")}"));
        if (v6.FirstOrDefault(x => x.Str("ip").StartsWith("fe80", StringComparison.OrdinalIgnoreCase)) is { ValueKind: JsonValueKind.Object } ll) rows.Add(("IPv6 link-local", ll.Str("ip")));
        rows.Add(("Interface index", a.Num("index").ToString()));
        _detail.Children.Add(Ui.Card(Ui.Stack(10, Ui.Title("Settings"), Ui.Kv(rows))));
    }

    static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    async Task TickAsync()
    {
        if (_busy || _sel is not { } a || a.Str("status") != "Up") return;
        _busy = true;
        try
        {
            var r = await Backend.GetAsync("/api/hw/perf");
            var want = Norm(a.Str("description"));
            var inst = r.Get("net").Items().FirstOrDefault(n => Norm(n.Str("name")) == want) is { ValueKind: JsonValueKind.Object } x ? x
                : r.Get("net").Items().FirstOrDefault(n => Norm(n.Str("name")).Length > 6 && (want.Contains(Norm(n.Str("name"))) || Norm(n.Str("name")).Contains(want)));
            if (inst.ValueKind != JsonValueKind.Object) { _stats.Text = "Windows has no throughput counter for this adapter."; _down.Text = _up.Text = "-"; _busy = false; return; }
            double rx = inst.Num("rx"), tx = inst.Num("tx");
            var now = DateTime.UtcNow; var dt = Math.Min(2, (now - _last).TotalSeconds); _last = now;
            _totalRx += rx * dt; _totalTx += tx * dt;
            _chart.Add(rx, tx); _down.Text = Rate(rx); _up.Text = Rate(tx);
            _stats.Text = $"Peak {Rate(_chart.Peak)}  ·  since you opened it: {Json.Bytes(_totalRx)} down, {Json.Bytes(_totalTx)} up  ·  link {Speed(a.Num("speed"))}";
        }
        catch { }
        _busy = false;
    }
}
