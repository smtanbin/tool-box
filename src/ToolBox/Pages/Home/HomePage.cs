using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ToolBox.Pages;

/// <summary>Start page: recently used and most used tools, then every tool grouped. Usage is remembered in %LocalAppData%\MyToolBox\usage.json.</summary>
public sealed class HomePage : Page
{
    public sealed record Tool(string Tag, string Name, string Desc, string Glyph, string Group);

    public static readonly Tool[] Tools =
    {
        new("foldersize", "Folder Size", "See what fills your disks", "", "Disk"),
        new("disk", "Disk info", "Health, SMART, DRAM-less check", "", "Disk"),
        new("hw", "Hardware info", "CPU, board, memory, graphics", "", "System"),
        new("temps", "Temperatures", "Live graphs of every sensor", "", "System"),
        new("proc", "Process monitor", "Processes, ports, end tasks", "", "System"),
        new("hosts", "Hosts file", "Edit the hosts file safely", "", "System"),
        new("env", "Environment & PATH", "Variables and PATH check", "", "System"),
        new("adapters", "Network adapters", "Addresses, gateways, DNS", "", "Network"),
        new("dns", "DNS lookup", "A, AAAA, MX, TXT, NS, PTR", "", "Network"),
        new("ping", "Ping", "Latency graph, several hosts", "", "Network"),
        new("trace", "Traceroute", "Hop by hop route", "", "Network"),
        new("subnet", "Subnet calculator", "CIDR, masks, host ranges", "", "Network"),
        new("ipscan", "IP scanner", "Find devices on your network", "", "Network"),
        new("telnet", "Telnet / ports", "Port test and raw TCP console", "", "Network"),
        new("hash", "Hash text", "MD5, SHA-1, SHA-256 ...", "", "Developer"),
        new("crypt", "Encrypt / decrypt", "AES-256 with a passphrase", "", "Developer"),
        new("token", "Token & UUID", "UUIDs, random tokens, passwords", "", "Developer"),
        new("encode", "Base64 & encoders", "Base64, URL, HTML, hex", "", "Developer"),
        new("json", "JSON formatter", "Format, minify, validate", "", "Developer"),
        new("jwt", "JWT decoder", "Read the claims of a token", "", "Developer"),
        new("time", "Timestamp", "Unix time and dates", "", "Developer"),
        new("base", "Number base", "Binary, octal, decimal, hex", "", "Developer"),
        new("img64", "Image to Base64", "Data URIs from a picture", "", "Developer"),
        new("ico", "Image to ICO", "Multi-size icons", "", "Developer"),
    };

    static readonly Dictionary<string, string> Colors = new()
    {
        ["home"] = "#0078D4", ["foldersize"] = "#F59E0B", ["disk"] = "#0EA5E9", ["hw"] = "#6366F1", ["temps"] = "#EF4444", ["proc"] = "#10B981", ["hosts"] = "#8B5CF6", ["env"] = "#F97316",
        ["adapters"] = "#06B6D4", ["dns"] = "#3B82F6", ["ping"] = "#22C55E", ["trace"] = "#14B8A6", ["subnet"] = "#A855F7", ["ipscan"] = "#EC4899", ["telnet"] = "#64748B",
        ["hash"] = "#84CC16", ["crypt"] = "#E11D48", ["token"] = "#F59E0B", ["encode"] = "#0EA5E9", ["json"] = "#EAB308", ["jwt"] = "#D946EF", ["time"] = "#06B6D4", ["base"] = "#6366F1",
        ["img64"] = "#10B981", ["ico"] = "#F97316", ["about"] = "#981F32"
    };

    /// <summary>The colour each tool wears in the menu and on its Home card.</summary>
    public static Windows.UI.Color ColorOf(string tag)
    {
        var hex = Colors.GetValueOrDefault(tag, "#0078D4");
        return Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }

    static string UsagePath => Path.Combine(Environment.GetEnvironmentVariable("LocalAppData") ?? Path.GetTempPath(), "MyToolBox", "usage.json");

    sealed class Usage { public List<string> Recent { get; set; } = new(); public Dictionary<string, int> Counts { get; set; } = new(); }

    static Usage Load()
    {
        try { return JsonSerializer.Deserialize<Usage>(File.ReadAllText(UsagePath)) ?? new(); } catch { return new(); }
    }

    public static void Record(string tag)
    {
        if (tag == "home") return;
        try
        {
            var u = Load();
            u.Recent.Remove(tag); u.Recent.Insert(0, tag); if (u.Recent.Count > 8) u.Recent.RemoveRange(8, u.Recent.Count - 8);
            u.Counts[tag] = u.Counts.GetValueOrDefault(tag) + 1;
            Directory.CreateDirectory(Path.GetDirectoryName(UsagePath)!);
            File.WriteAllText(UsagePath, JsonSerializer.Serialize(u));
        }
        catch { }
    }

    public HomePage()
    {
        var u = Load();
        var page = new StackPanel { Spacing = 10 };
        page.Children.Add(Hero());
        page.Children.Add(HomeParts.Brand());
        page.Children.Add(new TextBlock { Text = "At a glance", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 10, 0, 0) });
        page.Children.Add(new HomeParts.LiveStrip());

        var recent = u.Recent.Select(t => Tools.FirstOrDefault(x => x.Tag == t)).Where(t => t != null).Take(6).ToList();
        if (recent.Count > 0) Section(page, "Recent", recent!);
        var top = u.Counts.OrderByDescending(kv => kv.Value).Select(kv => Tools.FirstOrDefault(x => x.Tag == kv.Key)).Where(t => t != null).Take(4).ToList();
        if (top.Count > 0) Section(page, "Most used", top!);
        foreach (var g in Tools.GroupBy(t => t.Group)) Section(page, g.Key, g);

        page.Children.Add(new Border { Margin = new Thickness(0, 18, 0, 0), Child = HomeParts.OpenSource() });
        page.Children.Add(new TextBlock { Text = "About", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 18, 0, 0) });
        page.Children.Add(AboutPage.Build());
        var scroller = Ui.Page(page); Content = scroller;

        var ctrlK = new KeyboardAccelerator { Key = Windows.System.VirtualKey.K, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        ctrlK.Invoked += (_, e) => { _search?.Focus(FocusState.Keyboard); e.Handled = true; };
        KeyboardAccelerators.Add(ctrlK);
    }

    AutoSuggestBox? _search;

    /// <summary>The blue welcome banner: greeting, name, one-line pitch, search box and a few facts, with the toolbox logo on the right.</summary>
    UIElement Hero()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "GOOD MORNING" : hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING";
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var soft = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 255, 255, 255));

        _search = new AutoSuggestBox { PlaceholderText = "Search tools and commands", QueryIcon = new SymbolIcon(Symbol.Find), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left, Width = 440 };
        _search.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = s.Text.Trim();
            s.ItemsSource = q.Length == 0 ? null : Tools.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Desc.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).ToList();
        };
        void Open(string name) { var t = Tools.FirstOrDefault(x => x.Name == name); if (t != null) App.Window?.GoTo(t.Tag); }
        _search.SuggestionChosen += (_, e) => Open((string)e.SelectedItem);
        _search.QuerySubmitted += (s, e) =>
        {
            var q = (e.ChosenSuggestion as string) ?? e.QueryText.Trim();
            var hit = Tools.FirstOrDefault(t => t.Name.Equals(q, StringComparison.OrdinalIgnoreCase)) ?? Tools.FirstOrDefault(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Desc.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (hit != null) App.Window?.GoTo(hit.Tag);
        };
        var key = new Border { VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)), Child = new TextBlock { Text = "Ctrl+K", FontSize = 11, Foreground = white, TextWrapping = TextWrapping.NoWrap } };
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, key } };

        Border Chip(string glyph, string text) => new()
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 5), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(56, 255, 255, 255)),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new FontIcon { Glyph = glyph, FontSize = 12, Foreground = white }, new TextBlock { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = white } } }
        };
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        var chips = Ui.Wrap(Chip("\uE8A9", $"{Tools.Length} tools"), Chip("\uE72E", "Private and offline"), Chip("\uE946", $"v{(v == null ? "2.2.0" : $"{v.Major}.{v.Minor}.{v.Build}")}"));

        var text = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = greeting, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 120, Foreground = soft });
        text.Children.Add(new TextBlock { Text = "My Tool Box.", FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = -20, Foreground = white });
        text.Children.Add(new TextBlock { Text = "Everyday IT tools for Windows, in one private, offline app.", FontSize = 14, Foreground = white, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 0), Child = searchRow });
        text.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), Child = chips });

        // logo with soft translucent circles behind it
        var art = new Grid { Width = 190, Height = 160, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Ellipse(art, 150, 20, 10, 0, 80); Ellipse(art, 34, 14, 26, 120, 60); Ellipse(art, 120, 10, 0, 0, 40, HorizontalAlignment.Center, VerticalAlignment.Center);
        var logo = new Image { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        try { logo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon-256.png"))); } catch { }
        art.Children.Add(logo);

        // the logo bobs up and down like the web page's hhfloat keyframes: 7 px over 6 s, ease-in-out, forever
        var lift = new TranslateTransform();
        logo.RenderTransform = lift;
        var bob = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0, To = -7, Duration = new Duration(TimeSpan.FromSeconds(3)), AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.SineEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(bob, lift);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(bob, "Y");
        var bobBoard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        bobBoard.Children.Add(bob);
        logo.Loaded += (_, _) => bobBoard.Begin();

        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text); Grid.SetColumn(art, 1); grid.Children.Add(art);
        grid.SizeChanged += (_, e) => art.Visibility = e.NewSize.Width < 760 ? Visibility.Collapsed : Visibility.Visible;

        return new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(30, 26, 30, 24), Child = grid,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.2), EndPoint = new Windows.Foundation.Point(1, 0.9),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 61, 150, 255), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 120, 255), Offset = 0.45 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 76, 108, 247), Offset = 1 }
                }
            }
        };
    }

    static void Ellipse(Grid host, double size, double left, double top, double right, byte alpha, HorizontalAlignment h = HorizontalAlignment.Right, VerticalAlignment v = VerticalAlignment.Top)
    {
        host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = size, Height = size, HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(0, top, right, 0),
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 255, 255, 255))
        });
    }

    static void Section(StackPanel page, string title, IEnumerable<Tool> tools)
    {
        page.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 12, 0, 0) });
        page.Children.Add(Ui.Tiles(tools.Select(Card), 260));
    }

    static UIElement Card(Tool t)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var c = ColorOf(t.Tag);
        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, c.R, c.G, c.B)),
            Child = new FontIcon { Glyph = t.Glyph, FontSize = 18, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(c) }
        };
        row.Children.Add(tile);
        var text = Ui.Stack(2, Ui.Title(t.Name), Ui.Hint(t.Desc));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var card = Ui.Card(row);
        card.Tapped += (_, _) => App.Window?.GoTo(t.Tag);
        card.PointerEntered += (_, _) => Hover(card, true);
        card.PointerExited += (_, _) => Hover(card, false);
        return card;
    }

    static void Hover(Border b, bool hover) => b.Opacity = hover ? 0.85 : 1;
}
