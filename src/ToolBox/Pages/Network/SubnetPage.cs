using System.Net;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>Subnet calculator: IPv4 address and prefix or mask in; network, broadcast, host range, wildcard, binary view and a subnet splitter out.</summary>
public sealed class SubnetPage : ToolPage
{
    readonly TextBox _ip = new() { Text = "192.168.1.10/24", PlaceholderText = "10.0.0.5/16 or 10.0.0.5 255.255.0.0", Header = "IP address (CIDR or mask ok)", MinWidth = 320, FontFamily = new FontFamily("Consolas") };
    readonly ComboBox _mask = new() { Header = "Prefix / netmask", MinWidth = 230 };
    readonly TextBlock _err = new() { Foreground = new SolidColorBrush(Ui.Red), TextWrapping = TextWrapping.Wrap };
    readonly Border _netCard = Ui.Card(new TextBlock());
    readonly Border _binCard = Ui.Card(new TextBlock());
    readonly ComboBox _split = new() { Header = "New prefix", MinWidth = 300 };
    readonly TextBlock _splitInfo = Ui.Hint("");
    readonly StackPanel _splitRows = new();
    readonly Border _splitCard = Ui.Card(new TextBlock());
    bool _busy;
    uint _cur, _net; int _p = -1;

    static uint MaskOf(int p) => p == 0 ? 0 : uint.MaxValue << (32 - p);
    static string Ip(uint u) => $"{u >> 24}.{(u >> 16) & 255}.{(u >> 8) & 255}.{u & 255}";
    static uint? Parse(string s) => IPAddress.TryParse(s.Trim(), out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && s.Trim().Count(c => c == '.') == 3
        ? (uint)(a.GetAddressBytes()[0] << 24 | a.GetAddressBytes()[1] << 16 | a.GetAddressBytes()[2] << 8 | a.GetAddressBytes()[3]) : null;

    static (uint first, uint last, ulong hosts, uint bc) Hosts(uint net, int p)
    {
        var total = 1UL << (32 - p); var bc = (uint)(net + total - 1);
        return p == 32 ? (net, net, 1, bc) : p == 31 ? (net, bc, 2, bc) : (net + 1, bc - 1, total - 2, bc);
    }

    static string IpType(uint u)
    {
        bool In(string b, int p) => (u & MaskOf(p)) == Parse(b);
        if (In("10.0.0.0", 8) || In("172.16.0.0", 12) || In("192.168.0.0", 16)) return "Private (RFC 1918)";
        if (In("127.0.0.0", 8)) return "Loopback"; if (In("169.254.0.0", 16)) return "Link-local (APIPA)"; if (In("100.64.0.0", 10)) return "Carrier-grade NAT (RFC 6598)";
        if (In("224.0.0.0", 4)) return "Multicast"; if (u == 0xffffffff) return "Limited broadcast"; if (In("240.0.0.0", 4)) return "Reserved"; if (In("0.0.0.0", 8)) return "“This network”";
        if (In("192.0.2.0", 24) || In("198.51.100.0", 24) || In("203.0.113.0", 24)) return "Documentation (TEST-NET)";
        return "Public";
    }

    public SubnetPage() : base("Subnet calculator")
    {
        _mask.ItemsSource = Enumerable.Range(0, 33).Select(i => 32 - i).Select(p => $"/{p} — {Ip(MaskOf(p))}").ToList();
        _mask.SelectedIndex = 8;
        _ip.TextChanged += (_, _) => Calc();
        _mask.SelectionChanged += (_, _) =>
        {
            if (_busy || _mask.SelectedIndex < 0) return;
            var ipPart = _ip.Text.Trim().Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            _busy = true; _ip.Text = ipPart + "/" + (32 - _mask.SelectedIndex); _busy = false; Calc();
        };
        _split.SelectionChanged += (_, _) => { if (!_busy) FillSplit(); };
        var copy = new Button { Content = "Copy as CSV" }; copy.Click += (_, _) => CopyCsv();

        Body.Children.Add(Ui.Card(Ui.Stack(8, Ui.Wrap(_ip, _mask), _err)));
        Body.Children.Add(_netCard);
        Body.Children.Add(_binCard);
        Body.Children.Add(_splitCard);
        _splitCard.Child = Ui.Stack(10, Ui.Title("Split into subnets"), Ui.Wrap(_split, copy), _splitInfo, _splitRows);
        Calc();
    }

    void Calc()
    {
        if (_busy) return;
        _err.Text = "";
        var raw = _ip.Text.Trim();
        var parts = raw.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        uint? ip = parts.Length > 0 ? Parse(parts[0]) : null; var p = 32 - Math.Max(0, _mask.SelectedIndex);
        if (ip == null) { if (raw.Length > 0) _err.Text = "Enter an IPv4 address such as 192.168.1.10/24"; _p = -1; _netCard.Child = new TextBlock(); _binCard.Child = new TextBlock(); _splitRows.Children.Clear(); _splitInfo.Text = ""; return; }
        if (parts.Length > 1)
        {
            if (int.TryParse(parts[1], out var n) && n is >= 0 and <= 32) p = n;
            else if (Parse(parts[1]) is { } m && ((~m) & ((~m) + 1)) == 0) p = m == 0 ? 0 : 32 - BitOperations.TrailingZeroCount(m);
            else { _err.Text = "The mask must be a prefix like /24 or a valid netmask like 255.255.255.0"; return; }
        }
        _busy = true; _mask.SelectedIndex = 32 - p; _busy = false;

        var mask = MaskOf(p); var wild = ~mask; var net = ip.Value & mask; var h = Hosts(net, p);
        _cur = ip.Value; _net = net; _p = p;
        var o1 = ip.Value >> 24;
        var cls = o1 < 128 ? "A" : o1 < 192 ? "B" : o1 < 224 ? "C" : o1 < 240 ? "D (multicast)" : "E (reserved)";
        var left = new (string, string)[]
        {
            ("Address", Ip(ip.Value)), ("CIDR notation", $"{Ip(net)}/{p}"), ("Netmask", Ip(mask)), ("Wildcard mask", Ip(wild)), ("Network address", Ip(net)),
            ("Broadcast address", p >= 31 ? "-" : Ip(h.bc)), ("First usable host", Ip(h.first)), ("Last usable host", Ip(h.last))
        };
        var right = new (string, string)[]
        {
            ("Usable hosts", h.hosts.ToString("N0")), ("Total addresses", (1UL << (32 - p)).ToString("N0")), ("Host range", $"{Ip(h.first)} – {Ip(h.last)}"), ("IP class", cls),
            ("Address type", IpType(ip.Value)), ("Hex", string.Join(".", BitConverter.GetBytes(ip.Value).Reverse().Select(b => b.ToString("X2")))), ("Integer", ip.Value.ToString()),
            ("Reverse DNS zone", string.Join(".", Ip(ip.Value).Split('.').Reverse()) + ".in-addr.arpa")
        };
        var cols = Ui.Tiles(new UIElement[] { Ui.Kv(left), Ui.Kv(right) }, 380);
        _netCard.Child = Ui.Stack(10, Ui.Title("Network"), cols);

        // binary view: network bits bold and coloured, host bits dim
        var bin = new StackPanel { Spacing = 6 };
        foreach (var (k, u) in new[] { ("Address", ip.Value), ("Netmask", mask), ("Wildcard", wild), ("Network", net), ("Broadcast", h.bc) })
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Ui.Hint(k));
            var tb = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            var s = Convert.ToString(u, 2).PadLeft(32, '0');
            for (var i = 0; i < 32; i++)
            {
                if (i > 0 && i % 8 == 0) tb.Inlines.Add(new Run { Text = "." });
                tb.Inlines.Add(new Run { Text = s[i].ToString(), FontWeight = i < p ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, Foreground = new SolidColorBrush(i < p ? Ui.Blue : Color.FromArgb(150, 160, 160, 160)) });
            }
            Grid.SetColumn(tb, 1); row.Children.Add(tb); bin.Children.Add(row);
        }
        bin.Children.Insert(0, Ui.Title("Binary view"));
        bin.Children.Add(Ui.Hint("Network bits are highlighted; the rest are host bits."));
        _binCard.Child = bin;

        // splitter options
        _busy = true;
        var prev = _split.SelectedIndex;
        if (p >= 32) { _split.ItemsSource = new[] { "-" }; _split.IsEnabled = false; }
        else
        {
            _split.IsEnabled = true;
            _split.ItemsSource = Enumerable.Range(p + 1, Math.Min(32, p + 16) - p).Select(n => $"/{n} — {(1UL << (n - p)):N0} subnets × {Hosts(0, n).hosts:N0} hosts").ToList();
            _split.SelectedIndex = Math.Clamp(prev >= 0 ? prev : 1, 0, Math.Max(0, Math.Min(32, p + 16) - p - 1));
        }
        _busy = false;
        FillSplit();
    }

    List<(string cidr, string first, string last, string bc, ulong hosts)> SplitRows(int limit)
    {
        var res = new List<(string, string, string, string, ulong)>();
        if (_p < 0 || _p >= 32 || _split.SelectedIndex < 0) return res;
        var n = _p + 1 + _split.SelectedIndex; var size = 1UL << (32 - n); var count = 1UL << (n - _p);
        for (ulong i = 0; i < Math.Min(count, (ulong)limit); i++)
        {
            var net = (uint)(_net + i * size); var h = Hosts(net, n);
            res.Add(($"{Ip(net)}/{n}", Ip(h.first), Ip(h.last), n >= 31 ? "-" : Ip(h.bc), h.hosts));
        }
        return res;
    }

    void FillSplit()
    {
        _splitRows.Children.Clear();
        if (_p < 0) return;
        if (_p >= 32) { _splitInfo.Text = "A /32 is a single address and cannot be split."; return; }
        var n = _p + 1 + Math.Max(0, _split.SelectedIndex); var count = 1UL << (n - _p);
        _splitInfo.Text = count > 1024 ? $"Showing the first 1,024 of {count:N0} subnets" : $"{count:N0} subnets";
        var g = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        foreach (var w in new[] { 48.0, 150, 130, 130, 130, 80 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        void Cell(string t, int r, int c, bool head = false, bool right = false)
        {
            var tb = new TextBlock { Text = t, FontSize = 12.5, FontWeight = head ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Opacity = head ? 0.7 : 1, IsTextSelectionEnabled = !head };
            if (!head) tb.FontFamily = new FontFamily("Consolas");
            Grid.SetRow(tb, r); Grid.SetColumn(tb, c); g.Children.Add(tb);
        }
        var rows = SplitRows(1024);
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = { "#", "Network", "First host", "Last host", "Broadcast", "Hosts" };
        for (var c = 0; c < heads.Length; c++) Cell(heads[c], 0, c, true, c == 5);
        for (var i = 0; i < rows.Count; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = rows[i];
            Cell((i + 1).ToString(), i + 1, 0); Cell(r.cidr, i + 1, 1); Cell(r.first, i + 1, 2); Cell(r.last, i + 1, 3); Cell(r.bc, i + 1, 4); Cell(r.hosts.ToString("N0"), i + 1, 5, false, true);
        }
        _splitRows.Children.Add(new ScrollViewer { Content = g, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 });
    }

    void CopyCsv()
    {
        var r = SplitRows(100000);
        if (r.Count == 0) return;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(string.Join("\n", new[] { "Network,First host,Last host,Broadcast,Hosts" }.Concat(r.Select(x => $"{x.cidr},{x.first},{x.last},{x.bc},{x.hosts}"))));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
