using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ToolBox.Controls;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>Folder Size: scan a drive or folder, then explore it as a tree with size bars, a treemap, a donut chart,
/// file types, the largest files, search and duplicates.</summary>
public sealed class FolderSizePage : Page
{
    sealed class Row
    {
        public int Id; public string Name = ""; public bool Dir, Expanded, Loaded; public long Size, Files, Mod; public double Pct; public int Depth; public Row? Parent; public List<Row> Kids = new();
    }

    // state
    Row? _root; Row? _sel; string _rootPath = ""; string _sort = "Size"; bool _showMap = true, _showTabs = true;
    readonly List<int> _back = new(), _fwd = new();
    bool _scanning, _alive = true;

    // widgets
    readonly TextBox _path = new() { PlaceholderText = "C:\\ or a folder (press Enter to scan)", VerticalContentAlignment = VerticalAlignment.Center };
    readonly AutoSuggestBox _search = new() { PlaceholderText = "Search names", QueryIcon = new SymbolIcon(Symbol.Find), MinWidth = 260 };
    readonly Button _bBack = new() { Content = new FontIcon { Glyph = "\uE72B", FontSize = 14 } }, _bFwd = new() { Content = new FontIcon { Glyph = "\uE72A", FontSize = 14 } },
        _bUp = new() { Content = new FontIcon { Glyph = "\uE74A", FontSize = 14 } }, _bRef = new() { Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 } };
    readonly ProgressBar _bar = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    readonly ProgressRing _ring = new() { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed };
    readonly TextBlock _status = Ui.Hint("Pick a drive or folder and press Scan.");
    readonly StackPanel _rows = new();
    readonly TreemapView _map = new() { MinHeight = 240 };
    readonly TextBlock _mapTitle = Ui.Title("Treemap");
    readonly ComboBox _colorBy = new() { ItemsSource = new[] { "File type", "Folder" }, SelectedIndex = 0, MinWidth = 120 };
    readonly Pivot _tabs = new();
    readonly StackPanel _pie = new(), _types = new(), _top = new(), _hits = new(), _dupes = new();
    readonly DonutChart _donut = new();
    readonly StackPanel _legend = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, MinWidth = 280 };
    bool _pieByType;
    Grid _right = new(), _body = new();
    Border _mapCard = new(), _tabCard = new(), _leftCard = new();
    Grid _page = new(); ScrollViewer _scroll = new();

    static readonly string[] Sorts = { "Size", "Name", "Files", "Modified" };

    public FolderSizePage()
    {
        var root = new Grid { Padding = new Thickness(24, 8, 24, 12), RowSpacing = 8 };
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) }) root.RowDefinitions.Add(new RowDefinition { Height = h });
        root.Children.Add(new TextBlock { Text = "Folder Size", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });

        // address bar
        var addr = new Grid { ColumnSpacing = 6 };
        addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addr.ColumnDefinitions.Add(new ColumnDefinition());
        addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nav = new[] { _bBack, _bFwd, _bUp, _bRef };
        for (var i = 0; i < nav.Length; i++) { nav[i].Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); nav[i].BorderThickness = new Thickness(0); Grid.SetColumn(nav[i], i); addr.Children.Add(nav[i]); }
        Grid.SetColumn(_path, 4); addr.Children.Add(_path); Grid.SetColumn(_search, 5); addr.Children.Add(_search);
        Grid.SetRow(addr, 1); root.Children.Add(addr);

        // command bar
        var cmds = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsOpen = false, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Left };
        AppBarButton Cmd(string label, string glyph, Action a, Color? c = null) { var b = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph } }; if (c is Color cc) ((FontIcon)b.Icon).Foreground = new SolidColorBrush(cc); b.Click += (_, _) => a(); return b; }
        cmds.PrimaryCommands.Add(Cmd("Scan", "\uE768", () => _ = Scan(), Ui.Blue));
        cmds.PrimaryCommands.Add(Cmd("Browse...", "\uE838", async () => { var f = await Pickers.Folder(); if (f != null) { _path.Text = f; await Scan(); } }, Ui.Amber));
        cmds.PrimaryCommands.Add(Cmd("Reset", "\uE777", Reset));
        cmds.PrimaryCommands.Add(new AppBarSeparator());
        cmds.PrimaryCommands.Add(Cmd("Explorer", "\uE8DA", () => _ = Reveal(), Ui.Amber));
        cmds.PrimaryCommands.Add(Cmd("Export", "\uE896", () => _ = Export(), Ui.Green));
        var sortBtn = new AppBarButton { Label = "Sort", Icon = new FontIcon { Glyph = "\uE8CB" } };
        var sortMenu = new MenuFlyout();
        foreach (var s in Sorts) { var mi = new MenuFlyoutItem { Text = "By " + s.ToLowerInvariant() }; var name = s; mi.Click += (_, _) => { _sort = name; Resort(); }; sortMenu.Items.Add(mi); }
        sortBtn.Flyout = sortMenu; cmds.PrimaryCommands.Add(sortBtn);
        var viewBtn = new AppBarButton { Label = "View", Icon = new FontIcon { Glyph = "\uE8A9" } };
        var viewMenu = new MenuFlyout();
        var tm = new ToggleMenuFlyoutItem { Text = "Treemap", IsChecked = true }; tm.Click += (_, _) => { _showMap = tm.IsChecked; Layout(); };
        var tt = new ToggleMenuFlyoutItem { Text = "Charts and lists", IsChecked = true }; tt.Click += (_, _) => { _showTabs = tt.IsChecked; Layout(); };
        viewMenu.Items.Add(tm); viewMenu.Items.Add(tt); viewBtn.Flyout = viewMenu; cmds.PrimaryCommands.Add(viewBtn);
        Grid.SetRow(cmds, 2); root.Children.Add(cmds);

        // status line
        var st = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _ring, _status } };
        var statusBox = Ui.Stack(4, _bar, st);
        Grid.SetRow(statusBox, 3); root.Children.Add(statusBox);

        // body: left tree, right treemap + tabs
        _body = new Grid { ColumnSpacing = 12 };
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 520 });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        var header = RowGrid(); AddCells(header, bold: true, "Name", "% of parent", "Size", "Files", "Modified");
        left.Children.Add(header);
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows }; Grid.SetRow(sv, 1); left.Children.Add(sv);
        _leftCard = Ui.Card(left); _leftCard.Padding = new Thickness(8); _body.Children.Add(_leftCard);

        _right = new Grid { RowSpacing = 12 };
        _right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
        var mapHead = new Grid(); mapHead.ColumnDefinitions.Add(new ColumnDefinition()); mapHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var colorBox = Ui.Wrap(Ui.Hint("Color by"), _colorBy); Grid.SetColumn(colorBox, 1);
        mapHead.Children.Add(_mapTitle); mapHead.Children.Add(colorBox);
        var mapGrid = new Grid { RowSpacing = 8 }; mapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); mapGrid.RowDefinitions.Add(new RowDefinition());
        mapGrid.Children.Add(mapHead); Grid.SetRow(_map, 1); mapGrid.Children.Add(_map);
        _mapCard = Ui.Card(mapGrid); _right.Children.Add(_mapCard);

        _tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = "Pie chart", FontSize = 14 }, Content = PieTab() });
        _tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = "File types", FontSize = 14 }, Content = new ScrollViewer { Content = _types } });
        _tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = "Largest files", FontSize = 14 }, Content = new ScrollViewer { Content = _top } });
        _tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = "Search", FontSize = 14 }, Content = new ScrollViewer { Content = _hits } });
        _tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = "Duplicates", FontSize = 14 }, Content = DupesTab() });
        _tabs.SelectionChanged += (_, _) => _ = FillTab();
        _tabCard = Ui.Card(_tabs); _tabCard.Padding = new Thickness(8, 0, 8, 8); Grid.SetRow(_mapCard, 1); _right.Children.Add(_tabCard);
        Grid.SetColumn(_right, 1); _body.Children.Add(_right);
        Grid.SetRow(_body, 4); root.Children.Add(_body);
        _page = root;
        _scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled };
        Content = _scroll;

        // wiring
        _path.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) await Scan(); };
        _bBack.Click += (_, _) => Nav(_back, _fwd); _bFwd.Click += (_, _) => Nav(_fwd, _back);
        _bUp.Click += (_, _) => { if (_sel?.Parent != null) Select(_sel.Parent, true); };
        _bRef.Click += async (_, _) => await Scan();
        _map.NodeClicked += async (id, dir) => await Focus(id);
        _colorBy.SelectionChanged += (_, _) => { _map.ColorBy = (string)_colorBy.SelectedItem; _ = LoadMap(); };
        _search.QuerySubmitted += async (_, a) => { if (_root == null) return; _tabs.SelectedIndex = 3; await Search(a.QueryText); };
        _scroll.SizeChanged += (_, _) => Layout();
        Loaded += (_, _) => Layout();
        // leaving the page stops a running scan and lets the engine free the scanned tree (it can be millions of entries)
        Unloaded += async (_, _) => { _alive = false; try { await Backend.GetAsync("/api/reset"); } catch { } };
    }

    /// <summary>Wide windows: tree on the left, charts on the right, everything fits the window. Narrow windows: stacked, and the page scrolls.</summary>
    void Layout()
    {
        var w = _scroll.ActualWidth;
        if (w <= 0) return;
        var narrow = w < 1050;
        _mapCard.Visibility = _showMap ? Visibility.Visible : Visibility.Collapsed;
        _tabCard.Visibility = _showTabs ? Visibility.Visible : Visibility.Collapsed;
        var rightOn = _showMap || _showTabs;
        _right.Visibility = rightOn ? Visibility.Visible : Visibility.Collapsed;
        if (!narrow)
        {
            // short windows keep the side-by-side layout at a comfortable height and scroll, so the charts are never squeezed
            _scroll.VerticalScrollMode = _scroll.ActualHeight < 780 ? ScrollMode.Enabled : ScrollMode.Disabled;
            _page.Height = Math.Max(780, _scroll.ActualHeight);
            _page.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
            _body.RowDefinitions.Clear();
            _body.ColumnDefinitions[0].Width = new GridLength(1.3, GridUnitType.Star); _body.ColumnDefinitions[0].MinWidth = 480;
            _body.ColumnDefinitions[1].Width = rightOn ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(_leftCard, 0); Grid.SetColumn(_leftCard, 0); Grid.SetRow(_right, 0); Grid.SetColumn(_right, 1);
            _leftCard.Height = double.NaN; _right.Height = double.NaN;
            _right.RowDefinitions[0].Height = _showTabs ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _right.RowDefinitions[1].Height = _showMap ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        }
        else
        {
            _scroll.VerticalScrollMode = ScrollMode.Enabled;
            _page.Height = double.NaN;
            _page.RowDefinitions[4].Height = GridLength.Auto;
            _body.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star); _body.ColumnDefinitions[0].MinWidth = 0;
            _body.ColumnDefinitions[1].Width = new GridLength(0);
            if (_body.RowDefinitions.Count == 0) { _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            _leftCard.Height = 460;
            Grid.SetRow(_leftCard, 0); Grid.SetColumn(_leftCard, 0); Grid.SetRow(_right, 1); Grid.SetColumn(_right, 0);
            _right.Height = (_showTabs ? 380 : 0) + (_showMap ? 400 : 0) + 12;
            _right.RowDefinitions[0].Height = _showTabs ? new GridLength(380) : new GridLength(0);
            _right.RowDefinitions[1].Height = _showMap ? new GridLength(400) : new GridLength(0);
        }
    }

    /* ---------- tree ---------- */

    static Grid RowGrid()
    {
        var g = new Grid { ColumnSpacing = 8, Padding = new Thickness(6, 0, 6, 0), MinHeight = 30 };
        g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 160 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        return g;
    }

    static void AddCells(Grid g, bool bold, params string[] t)
    {
        for (var i = 0; i < t.Length; i++)
        {
            var tb = new TextBlock { Text = t[i], VerticalAlignment = VerticalAlignment.Center, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = i is 2 or 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            if (!bold) tb.Opacity = i == 0 ? 1 : 0.85;
            Grid.SetColumn(tb, i); g.Children.Add(tb);
        }
    }

    Row MakeRow(JsonElement n, Row? parent, long parentSize) => new()
    {
        Id = (int)n.Num("id"), Name = n.Str("name"), Dir = n.Get("dir").ValueKind == JsonValueKind.True, Size = (long)n.Num("size"), Files = (long)n.Num("files"), Mod = (long)n.Num("mod"),
        Pct = parentSize > 0 ? n.Num("size") * 100.0 / parentSize : 100, Depth = parent == null ? 0 : parent.Depth + 1, Parent = parent
    };

    async Task LoadKids(Row r)
    {
        if (r.Loaded || !r.Dir) return;
        var res = await Backend.GetAsync("/api/node?id=" + r.Id + "&limit=400");
        r.Kids = res.Get("children").Items().Select(c => MakeRow(c, r, r.Size)).ToList();
        r.Loaded = true; SortKids(r);
    }

    void SortKids(Row r)
    {
        r.Kids = (_sort switch
        {
            "Name" => r.Kids.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase),
            "Files" => r.Kids.OrderByDescending(k => k.Files),
            "Modified" => r.Kids.OrderByDescending(k => k.Mod),
            _ => r.Kids.OrderByDescending(k => k.Size)
        }).ToList();
        foreach (var k in r.Kids) SortKids(k);
    }

    void Resort() { if (_root == null) return; SortKids(_root); Render(); }

    void Render()
    {
        _rows.Children.Clear();
        if (_root == null) return;
        var n = 0;
        void Add(Row r)
        {
            if (n++ > 1500) return;
            _rows.Children.Add(RowView(r));
            if (r.Expanded) foreach (var k in r.Kids) Add(k);
        }
        Add(_root);
    }

    UIElement RowView(Row r)
    {
        var g = RowGrid(); g.MinHeight = 30;
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(r.Depth * 16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var chev = new Button { Padding = new Thickness(0), Width = 18, Height = 22, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Opacity = r.Dir && r.Files > 0 ? 1 : 0, IsHitTestVisible = r.Dir && r.Files > 0,
            Content = new FontIcon { Glyph = r.Expanded ? "\uE70D" : "\uE76C", FontSize = 9 } };
        chev.Click += async (_, _) => { r.Expanded = !r.Expanded; if (r.Expanded) await LoadKids(r); Render(); };
        name.Children.Add(chev);
        var col = r.Dir ? Ui.Amber : TreemapView.CategoryColor(System.IO.Path.GetExtension(r.Name).ToLowerInvariant());
        name.Children.Add(new FontIcon { Glyph = r.Dir ? "\uE8B7" : "\uE8A5", FontSize = 15, Foreground = new SolidColorBrush(col) });
        name.Children.Add(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = r == _sel ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal });
        g.Children.Add(name);

        // % of parent: a bar with the number on top
        var pct = new Grid { Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        pct.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) });
        pct.Children.Add(new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 100 * Math.Clamp(r.Pct, 0, 100) / 100), Background = new SolidColorBrush(Color.FromArgb(255, 59, 142, 240)) });
        pct.Children.Add(new TextBlock { Text = r.Pct < 0.1 ? "<0.1%" : $"{r.Pct:0.#}%", FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(pct, 1); g.Children.Add(pct);
        void Cell(int col2, string text, bool right) { var t = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Opacity = 0.9 }; Grid.SetColumn(t, col2); g.Children.Add(t); }
        Cell(2, Json.Bytes(r.Size), true); Cell(3, r.Dir ? r.Files.ToString("N0") : "", true); Cell(4, r.Mod > 0 ? DateTimeOffset.FromUnixTimeSeconds(r.Mod).LocalDateTime.ToString("yyyy-MM-dd") : "", false);

        var wrap = new Border { Child = g, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(r == _sel ? Color.FromArgb(48, 59, 142, 240) : Microsoft.UI.Colors.Transparent) };
        wrap.Tapped += async (_, _) => await SelectAsync(r, false);
        wrap.DoubleTapped += async (_, _) => { r.Expanded = !r.Expanded; if (r.Expanded) await LoadKids(r); Render(); };
        wrap.RightTapped += (_, e) => ShowMenu(wrap, r, e);
        return wrap;
    }

    void ShowMenu(FrameworkElement at, Row r, RightTappedRoutedEventArgs e)
    {
        var m = new MenuFlyout();
        void Item(string t, Action a) { var i = new MenuFlyoutItem { Text = t }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Item("Show in Explorer", () => { _sel = r; _ = Reveal(); });
        Item("Copy path", () => { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(PathOf(r)); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); });
        if (r.Dir) Item("Zoom treemap here", () => _ = SelectAsync(r, false));
        m.ShowAt(at, e.GetPosition(at));
    }

    string PathOf(Row r) { var parts = new List<string>(); for (var x = r; x != null; x = x.Parent) parts.Insert(0, x == _root ? _rootPath : x.Name); return System.IO.Path.Combine(parts.ToArray()); }

    /* ---------- scanning ---------- */

    void Busy(bool on)
    {
        _scanning = on;
        _bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed; _ring.IsActive = on;
    }

    void Reset()
    {
        _root = _sel = null; _rows.Children.Clear(); _map.SetData(default); _back.Clear(); _fwd.Clear(); _status.Text = "Pick a drive or folder and press Scan.";
        foreach (var s in new[] { _types, _top, _hits, _dupes }) s.Children.Clear();
        _legend.Children.Clear(); _donut.SetData(Array.Empty<(double, Color)>(), "", "");
    }

    async Task Scan()
    {
        var path = _path.Text.Trim();
        if (path.Length == 0) { var f = await Pickers.Folder(); if (f == null) return; path = _path.Text = f; }
        if (_scanning) return;
        Reset(); Busy(true); _status.Text = "Starting...";
        try { await Backend.GetAsync("/api/scan?path=" + Uri.EscapeDataString(path)); }
        catch (Exception e) { _status.Text = e.Message; Busy(false); return; }
        while (_alive)
        {
            try
            {
                var s = await Backend.GetAsync("/api/status");
                var on = s.Get("scanning").ValueKind == JsonValueKind.True;
                _status.Text = $"{(on ? "Scanning" : "Scanned")} {s.Num("files"):N0} files, {s.Num("dirs"):N0} folders, {Json.Bytes(s.Num("bytes"))} · {s.Num("elapsed"):0.0} s" + (s.Num("errors") > 0 ? $" · {s.Num("errors")} unreadable" : "") + (on ? "  " + Shorten(s.Str("current")) : "");
                if (!on) break;
            }
            catch (Exception e) { _status.Text = e.Message; break; }
            await Task.Delay(300);
        }
        if (!_alive) return;
        try
        {
            _rootPath = path;
            var res = await Backend.GetAsync("/api/node?id=0&limit=400");
            _root = MakeRow(res.Get("node"), null, 0); _root.Name = System.IO.Path.GetFileName(path.TrimEnd((char)92)) is { Length: > 0 } leaf ? leaf : path; _root.Pct = 100; _root.Expanded = true; _root.Loaded = true;
            _root.Kids = res.Get("children").Items().Select(c => MakeRow(c, _root, _root.Size)).ToList(); SortKids(_root);
            _sel = _root; Render(); await Refill();
        }
        catch (Exception e) { _status.Text = e.Message; }
        Busy(false);
    }

    static string Shorten(string p) => p.Length > 70 ? "..." + p[^67..] : p;

    /* ---------- selection and history ---------- */

    async Task SelectAsync(Row r, bool pushHistory)
    {
        if (_sel != null && pushHistory && _sel.Id != r.Id) { _back.Add(_sel.Id); _fwd.Clear(); }
        _sel = r; Render(); await Refill();
    }

    void Select(Row r, bool push) => _ = SelectAsync(r, push);

    Row? Find(Row from, int id) { if (from.Id == id) return from; foreach (var k in from.Kids) if (Find(k, id) is { } f) return f; return null; }

    async Task Focus(int id)
    {
        if (_root == null) return;
        // expand the path down to the clicked node, then select it
        var chain = new List<int>();
        var info = await Backend.GetAsync("/api/node?id=" + id + "&limit=1");
        foreach (var c in info.Get("crumbs").Items()) chain.Add((int)c.Num("id"));
        var cur = _root;
        foreach (var cid in chain.Skip(1))
        {
            await LoadKids(cur); cur.Expanded = true;
            var next = cur.Kids.FirstOrDefault(k => k.Id == cid); if (next == null) break; cur = next;
        }
        await SelectAsync(cur, true);
    }

    void Nav(List<int> from, List<int> to)
    {
        if (from.Count == 0 || _root == null) return;
        var id = from[^1]; from.RemoveAt(from.Count - 1);
        if (_sel != null) to.Add(_sel.Id);
        _ = Focus(id);
    }

    async Task Refill()
    {
        if (_sel == null) return;
        _path.Text = PathOf(_sel);
        _bBack.IsEnabled = _back.Count > 0; _bFwd.IsEnabled = _fwd.Count > 0; _bUp.IsEnabled = _sel.Parent != null;
        await LoadMap();
        await FillTab();
    }

    async Task LoadMap()
    {
        if (_sel == null) return;
        try
        {
            var tm = await Backend.GetAsync("/api/treemap?id=" + _sel.Id);
            _mapTitle.Text = "Treemap · " + _sel.Name; _map.ColorBy = (string)_colorBy.SelectedItem; _map.SetData(tm);
        }
        catch { }
    }

    /* ---------- tabs ---------- */

    UIElement PieTab()
    {
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var b1 = new ToggleButton { Content = "By folder / file", IsChecked = true }; var b2 = new ToggleButton { Content = "By file type" };
        b1.Click += (_, _) => { b1.IsChecked = true; b2.IsChecked = false; _pieByType = false; _ = FillTab(); };
        b2.Click += (_, _) => { b2.IsChecked = true; b1.IsChecked = false; _pieByType = true; _ = FillTab(); };
        toggle.Children.Add(b1); toggle.Children.Add(b2); toggle.Children.Add(Ui.Hint("Click a legend row to open it"));
        toggle.Children[2].SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        // The donut scales to whatever height the tab has (never cut off); the legend scrolls on its own.
        var box = new Viewbox { Child = _donut, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = 320, MaxHeight = 320, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var legendScroll = new ScrollViewer { Content = _legend, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _legend.MinWidth = 0; _legend.HorizontalAlignment = HorizontalAlignment.Stretch;
        var body = new Grid { ColumnSpacing = 24, RowSpacing = 8, Margin = new Thickness(8, 4, 8, 4) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        body.Children.Add(box); Grid.SetColumn(legendScroll, 1); body.Children.Add(legendScroll);
        body.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 520;
            Grid.SetColumn(legendScroll, narrow ? 0 : 1); Grid.SetRow(legendScroll, narrow ? 1 : 0);
            body.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1.6, GridUnitType.Star);
            body.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        };
        var page = new Grid { RowSpacing = 6 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(toggle); Grid.SetRow(body, 1); page.Children.Add(body);
        return page;
    }

    UIElement DupesTab()
    {
        var go = new Button { Content = "Find duplicates", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        go.Click += async (_, _) => await FindDupes();
        return new ScrollViewer { Content = Ui.Stack(8, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { go, Ui.Hint("Same size, same content. Reads files, so it can take a while on big folders.") } }, _dupes) };
    }

    async Task FillTab()
    {
        if (_sel == null) return;
        try
        {
            switch (_tabs.SelectedIndex)
            {
                case 0: await FillPie(); break;
                case 1: await FillTypes(); break;
                case 2: await FillTop(); break;
            }
        }
        catch (Exception e) { _status.Text = e.Message; }
    }

    static TextBlock Mono(string t, HorizontalAlignment ha = HorizontalAlignment.Left) => new() { Text = t, FontSize = 12.5, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    async Task FillPie()
    {
        var parts = new List<(string name, double size, Color color, int id, bool dir)>();
        if (_pieByType)
        {
            var t = await Backend.GetAsync("/api/types?id=" + _sel!.Id);
            var list = t.Get("types").Items().ToList();
            foreach (var x in list.Take(8)) { var e = x.Str("ext"); parts.Add((e.Length == 0 ? "(no extension)" : e, x.Num("size"), TreemapView.Palette[parts.Count % TreemapView.Palette.Length], -1, false)); }
            var rest = list.Skip(8).Sum(x => x.Num("size")); if (rest > 0) parts.Add(("other types", rest, Color.FromArgb(255, 140, 148, 162), -1, false));
        }
        else
        {
            var kids = _sel!.Kids; if (!_sel.Loaded) { await LoadKids(_sel); kids = _sel.Kids; }
            foreach (var k in kids.OrderByDescending(k => k.Size).Take(8).Where(k => k.Size > 0)) parts.Add((k.Name, k.Size, TreemapView.Palette[parts.Count % TreemapView.Palette.Length], k.Id, k.Dir));
            var rest = kids.OrderByDescending(k => k.Size).Skip(8).Sum(k => (double)k.Size); if (rest > 0) parts.Add(("other", rest, Color.FromArgb(255, 140, 148, 162), -1, false));
        }
        var total = parts.Sum(p => p.size);
        _donut.SetData(parts.Select(p => (p.size, p.color)).ToList(), Json.Bytes(_sel!.Size), "total");
        _legend.Children.Clear();
        foreach (var p in parts)
        {
            var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 3, 4, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 60 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(p.color), VerticalAlignment = VerticalAlignment.Center });
            var n = Mono(p.name); Grid.SetColumn(n, 1); var s = Mono(Json.Bytes(p.size), HorizontalAlignment.Right); Grid.SetColumn(s, 2); var pc = Mono(total > 0 ? $"{p.size * 100 / total:0.0}%" : "", HorizontalAlignment.Right); Grid.SetColumn(pc, 3);
            g.Children.Add(n); g.Children.Add(s); g.Children.Add(pc);
            if (p.id >= 0 && p.dir) { var id = p.id; g.Tapped += async (_, _) => await Focus(id); ToolTipService.SetToolTip(g, "Open " + p.name); }
            _legend.Children.Add(g);
        }
    }

    async Task FillTypes()
    {
        var t = await Backend.GetAsync("/api/types?id=" + _sel!.Id);
        _types.Children.Clear();
        var total = Math.Max(1, t.Num("total"));
        foreach (var x in t.Get("types").Items().Take(200))
        {
            var ext = x.Str("ext");
            var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 4, 4, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.Children.Add(Mono(ext.Length == 0 ? "(none)" : ext));
            var bar = new ProgressBar { Value = x.Num("size") * 100 / total, Maximum = 100, Foreground = new SolidColorBrush(TreemapView.CategoryColor(ext)) }; Grid.SetColumn(bar, 1); g.Children.Add(bar);
            var c = Mono($"{x.Num("count"):N0} files", HorizontalAlignment.Right); Grid.SetColumn(c, 2); var s = Mono(Json.Bytes(x.Num("size")), HorizontalAlignment.Right); Grid.SetColumn(s, 3);
            g.Children.Add(c); g.Children.Add(s); _types.Children.Add(g);
        }
    }

    void FileRow(StackPanel host, JsonElement x)
    {
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 4, 4, 4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var dir = x.Get("dir").ValueKind == JsonValueKind.True;
        g.Children.Add(new FontIcon { Glyph = dir ? "\uE8B7" : "\uE8A5", FontSize = 14, Foreground = new SolidColorBrush(dir ? Ui.Amber : TreemapView.CategoryColor(System.IO.Path.GetExtension(x.Str("name")).ToLowerInvariant())) });
        var t = Ui.Stack(0, Mono(x.Str("name")), Ui.Hint(x.Str("path"))); ((TextBlock)t.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(t, 1); g.Children.Add(t);
        var s = Mono(Json.Bytes(x.Num("size")), HorizontalAlignment.Right); Grid.SetColumn(s, 2); g.Children.Add(s);
        var id = (int)x.Num("id");
        g.DoubleTapped += (_, _) => _ = Backend.GetAsync("/api/reveal?id=" + id);
        ToolTipService.SetToolTip(g, "Double-click to show in Explorer");
        host.Children.Add(g);
    }

    async Task FillTop()
    {
        var top = await Backend.GetAsync("/api/top?id=" + _sel!.Id + "&n=200");
        _top.Children.Clear();
        foreach (var x in top.Items()) FileRow(_top, x);
        if (!top.Items().Any()) _top.Children.Add(Ui.Hint("No files here."));
    }

    async Task Search(string q)
    {
        _hits.Children.Clear();
        if (_sel == null) return;
        var r = await Backend.GetAsync($"/api/search?id={_sel.Id}&q={Uri.EscapeDataString(q)}");
        _hits.Children.Add(Ui.Hint($"{r.Num("total"):N0} matches for \"{q}\" in {_sel.Name}" + (r.Num("total") > 500 ? " (showing the largest 500)" : "")));
        foreach (var x in r.Get("items").Items()) FileRow(_hits, x);
    }

    async Task FindDupes()
    {
        if (_sel == null) return;
        _dupes.Children.Clear();
        var prog = new ProgressBar { Maximum = 100 }; var msg = Ui.Hint("Starting..."); _dupes.Children.Add(prog); _dupes.Children.Add(msg);
        try
        {
            await Backend.GetAsync("/api/dupes/start?id=" + _sel.Id + "&min=1024");
            JsonElement s;
            while (true)
            {
                s = await Backend.GetAsync("/api/dupes/status");
                var total = Math.Max(1, s.Num("total")); prog.Value = s.Num("done") * 100 / total; msg.Text = $"{s.Str("phase")}... {s.Num("done"):N0} of {s.Num("total"):N0}";
                if (s.Get("running").ValueKind != JsonValueKind.True) break;
                await Task.Delay(400);
            }
            _dupes.Children.Clear();
            var groups = s.Get("groups").Items().ToList();
            _dupes.Children.Add(Ui.Hint(groups.Count == 0 ? "No duplicate files found." : $"{groups.Count:N0} groups of identical files · {Json.Bytes(s.Num("wasted"))} could be freed"));
            foreach (var gp in groups.Take(200))
            {
                var items = gp.Get("items").Items().ToList();
                var ex = new Expander { Header = $"{items.Count} copies · {Json.Bytes(gp.Num("size"))} each · {Json.Bytes(gp.Num("wasted"))} wasted", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var host = new StackPanel();
                foreach (var it in items) { var row = Mono(it.Str("path")); var id = (int)it.Num("id"); row.Padding = new Thickness(4, 3, 4, 3); row.DoubleTapped += (_, _) => _ = Backend.GetAsync("/api/reveal?id=" + id); host.Children.Add(row); }
                ex.Content = host; _dupes.Children.Add(ex);
            }
        }
        catch (Exception e) { _dupes.Children.Clear(); _dupes.Children.Add(Ui.Hint(e.Message)); }
    }

    /* ---------- toolbar actions ---------- */

    async Task Reveal()
    {
        if (_sel == null) return;
        try { await Backend.GetAsync($"/api/reveal?id={_sel.Id}&open={(_sel.Dir ? 1 : 0)}"); } catch (Exception e) { _status.Text = e.Message; }
    }

    async Task Export()
    {
        if (_sel == null) { _status.Text = "Scan something first."; return; }
        try
        {
            var r = await Backend.GetAsync($"/api/export?id={_sel.Id}&files=1");
            var f = await Pickers.Save("folder-sizes", ".csv", "CSV");
            if (f == null) return;
            await Windows.Storage.FileIO.WriteTextAsync(f, "\uFEFF" + r.Str("csv"));
            _status.Text = "Saved " + f.Name;
        }
        catch (Exception e) { _status.Text = e.Message; }
    }
}
