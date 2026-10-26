using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>Hosts file editor: one row per entry (on/off, IP, host names, note), a raw text view, filter, flush DNS, discard and save.</summary>
public sealed class HostsPage : Page
{
    sealed class Line { public bool Entry, Enabled = true; public string Ip = "", Hosts = "", Note = "", Raw = ""; }

    List<Line> _lines = new();
    string _original = "", _path = "";
    bool _admin = true, _loading, _rawMode;
    string _filter = "";

    readonly StackPanel _rows = new() { Spacing = 0 };
    readonly TextBox _raw = new() { AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.NoWrap, MinHeight = 420, Visibility = Visibility.Collapsed };
    readonly TextBlock _info = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Opacity = 0.85 };
    readonly TextBlock _msg = Ui.Hint("");
    readonly Border _banner = new();
    readonly Button _save = new() { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = "Save" } } } };
    readonly Button _discard = new() { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = "Discard" } } } };
    readonly Grid _header = new();
    readonly Border _table = new();

    public HostsPage()
    {
        var root = new Grid { Padding = new Thickness(24, 8, 24, 12), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock { Text = "Hosts file", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });

        // toolbar: view switch, filter, add entry ... flush, reload, discard, save
        var tabs = new SelectorBar();
        tabs.Items.Add(new SelectorBarItem { Text = "Entries", IsSelected = true });
        tabs.Items.Add(new SelectorBarItem { Text = "Raw text" });
        tabs.SelectionChanged += (s, _) => SetMode(s.Items.IndexOf(s.SelectedItem) == 1);
        var filter = new TextBox { PlaceholderText = "IP, host name or note", MinWidth = 240, Header = "Filter" };
        filter.TextChanged += (_, _) => { _filter = filter.Text.Trim(); Render(); };
        Button B(string glyph, string text, Color? c = null) { var ic = new FontIcon { Glyph = glyph, FontSize = 14 }; if (c is Color cc) ic.Foreground = new SolidColorBrush(cc); return new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ic, new TextBlock { Text = text } } }, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) }; }
        var add = B("", "Add entry", Ui.Blue); add.Click += (_, _) => { _lines.Add(new Line { Entry = true, Ip = "127.0.0.1" }); Render(); Dirty(); };
        var flush = B("", "Flush DNS", Ui.Blue); flush.Click += async (_, _) => { try { await Backend.PostAsync("/api/hosts/flush", new { }); _msg.Text = "DNS cache flushed."; } catch (Exception e) { _msg.Text = e.Message; } };
        var reload = B("", "Reload"); reload.Click += async (_, _) => await LoadAsync();
        _discard.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); _discard.BorderThickness = new Thickness(0); _discard.Click += (_, _) => { Parse(_original); Render(); Dirty(); };
        _save.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); _save.BorderThickness = new Thickness(0); _save.Click += async (_, _) => await Save();

        var left = Ui.Wrap(new Border { Padding = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Child = tabs }, filter, new Border { VerticalAlignment = VerticalAlignment.Bottom, Child = add });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Children = { flush, reload, _discard, _save } };
        var bar = new Grid { ColumnSpacing = 12 };
        bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(left); Grid.SetColumn(right, 1); bar.Children.Add(right);
        Grid.SetRow(bar, 1); root.Children.Add(bar);

        // admin banner
        var warn = new FontIcon { Glyph = "", FontSize = 22, Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)) };
        var restart = new Button { Content = "Restart as administrator", Style = (Style)Application.Current.Resources["AccentButtonStyle"], VerticalAlignment = VerticalAlignment.Center };
        restart.Click += (_, _) => RestartElevated();
        var texts = Ui.Stack(2, new TextBlock { Text = "Run as administrator to save changes", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, Ui.Hint("Windows protects the hosts file. You can edit here, but saving needs an administrator copy of My Tool Box."));
        var bg = new Grid { ColumnSpacing = 14 };
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bg.ColumnDefinitions.Add(new ColumnDefinition()); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bg.Children.Add(warn); Grid.SetColumn(texts, 1); bg.Children.Add(texts); Grid.SetColumn(restart, 2); bg.Children.Add(restart);
        _banner.Child = bg; _banner.CornerRadius = new CornerRadius(6); _banner.Padding = new Thickness(16, 12, 16, 12); _banner.Background = new SolidColorBrush(Color.FromArgb(48, 245, 158, 11)); _banner.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 245, 158, 11)); _banner.BorderThickness = new Thickness(1);
        Grid.SetRow(_banner, 2); root.Children.Add(_banner);

        var infoRow = Ui.Stack(2, _info, _msg); Grid.SetRow(infoRow, 3); root.Children.Add(infoRow);

        // table
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        void Cap(string t, int c) { var h = Ui.Hint(t); h.FontSize = 12; Grid.SetColumn(h, c); _header.Children.Add(h); }
        Cap("IP address", 1); Cap("Host names (space separated)", 2); Cap("Note", 3);
        _header.Padding = new Thickness(8, 8, 8, 8);
        var scroll = new ScrollViewer { Content = Ui.Stack(0, _header, _rows), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var inner = new Grid(); inner.Children.Add(scroll); inner.Children.Add(_raw);
        _table.Child = inner; _table.CornerRadius = new CornerRadius(8); _table.BorderThickness = new Thickness(1);
        _table.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]; _table.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        Grid.SetRow(_table, 4); root.Children.Add(_table);
        Content = root;

        _raw.TextChanged += (_, _) => { if (!_loading && _rawMode) Dirty(); };
        Loaded += async (_, _) => await LoadAsync();
    }

    /* ---------- model ---------- */

    void Parse(string text)
    {
        _lines = new();
        foreach (var raw in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            if (raw.Trim().Length == 0) { _lines.Add(new Line { Raw = "" }); continue; }
            var l = TryEntry(raw);
            _lines.Add(l ?? new Line { Raw = raw });
        }
        while (_lines.Count > 0 && !_lines[^1].Entry && _lines[^1].Raw.Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    static Line? TryEntry(string raw)
    {
        var t = raw.Trim(); var enabled = true;
        if (t.StartsWith('#')) { enabled = false; t = t.TrimStart('#').Trim(); }
        var note = "";
        var h = t.IndexOf('#');
        if (h >= 0) { note = t[(h + 1)..].Trim(); t = t[..h].Trim(); }
        var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out _)) return null;
        if (parts.Skip(1).Any(p => p.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or '_')))) return null;
        return new Line { Entry = true, Enabled = enabled, Ip = parts[0], Hosts = string.Join(" ", parts.Skip(1)), Note = note };
    }

    string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines)
            sb.Append(l.Entry ? $"{(l.Enabled ? "" : "# ")}{l.Ip.Trim(),-16}{l.Hosts.Trim()}{(l.Note.Trim().Length > 0 ? "  # " + l.Note.Trim() : "")}" : l.Raw).Append('\n');
        return sb.ToString();
    }

    string CurrentText() => _rawMode ? _raw.Text : Serialize();

    /* ---------- load / save ---------- */

    async Task LoadAsync()
    {
        try
        {
            var r = await Backend.GetAsync("/api/hosts");
            _path = r.Str("path"); _admin = r.Get("admin").ValueKind == JsonValueKind.True;
            _original = r.Str("text");
            _loading = true; Parse(_original); _raw.Text = _original.Replace("\r\n", "\n"); _loading = false;
            _banner.Visibility = _admin ? Visibility.Collapsed : Visibility.Visible;
            _msg.Text = ""; Render(); Dirty();
        }
        catch (Exception e) { _msg.Text = e.Message; }
    }

    async Task Save()
    {
        try
        {
            var text = CurrentText();
            var r = await Backend.PostAsync("/api/hosts/save", new { text });
            _original = text; _msg.Text = "Saved. Backup: " + r.Str("backup");
            if (_rawMode) Parse(text);
            Render(); Dirty();
        }
        catch (Exception e) { _msg.Text = _admin ? e.Message : "Not saved: " + e.Message; }
    }

    void Dirty()
    {
        var changed = CurrentText().Replace("\r\n", "\n").TrimEnd() != _original.Replace("\r\n", "\n").TrimEnd();
        _save.IsEnabled = _discard.IsEnabled = changed;
        UpdateInfo();
    }

    void UpdateInfo()
    {
        var active = _lines.Count(l => l.Entry && l.Enabled); var off = _lines.Count(l => l.Entry && !l.Enabled);
        _info.Text = $"{_path}  ·  {active} active · {off} disabled";
    }

    void SetMode(bool raw)
    {
        if (raw == _rawMode) return;
        if (raw) { _loading = true; _raw.Text = Serialize(); _loading = false; }
        else Parse(_raw.Text);
        _rawMode = raw;
        _raw.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = _rows.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
        if (!raw) Render();
        Dirty();
    }

    void RestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--tool=hosts") { UseShellExecute = true, Verb = "runas" });
            Environment.Exit(0);
        }
        catch (Exception e) { _msg.Text = "Administrator rights were not granted: " + e.Message; }
    }

    /* ---------- rows ---------- */

    void Render()
    {
        _rows.Children.Clear();
        var n = 0;
        foreach (var l in _lines.Where(l => l.Entry))
        {
            if (_filter.Length > 0 && !(l.Ip + " " + l.Hosts + " " + l.Note).Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            var line = l;
            var g = new Grid { Padding = new Thickness(8, 4, 8, 4), ColumnSpacing = 0, MinHeight = 46, BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), BorderThickness = new Thickness(0, 1, 0, 0), Background = new SolidColorBrush(n++ % 2 == 1 ? Color.FromArgb(10, 128, 128, 128) : Microsoft.UI.Colors.Transparent) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var cb = new CheckBox { IsChecked = line.Enabled, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            ToolTipService.SetToolTip(cb, "Active (unchecked = commented out)");
            cb.Click += (_, _) => { line.Enabled = cb.IsChecked == true; UpdateInfo(); Dirty(); };
            g.Children.Add(cb);
            TextBox Box(string text, string ph, bool mono, Action<string> set, int col)
            {
                var t = new TextBox { Text = text, PlaceholderText = ph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(col == 1 ? 0 : 8, 0, 0, 0) };
                if (mono) t.FontFamily = new FontFamily("Consolas");
                t.TextChanged += (_, _) => { set(t.Text); Dirty(); };
                Grid.SetColumn(t, col); g.Children.Add(t); return t;
            }
            Box(line.Ip, "IP address", true, v => line.Ip = v, 1);
            Box(line.Hosts, "host names", true, v => line.Hosts = v, 2);
            Box(line.Note, "note", false, v => line.Note = v, 3);
            var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(del, "Delete this entry");
            del.Click += (_, _) => { _lines.Remove(line); Render(); Dirty(); };
            Grid.SetColumn(del, 4); g.Children.Add(del);
            _rows.Children.Add(g);
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(new TextBlock { Text = _filter.Length > 0 ? "No entries match the filter." : "No entries. Use Add entry.", Opacity = 0.6, Margin = new Thickness(16) });
        UpdateInfo();
    }
}
