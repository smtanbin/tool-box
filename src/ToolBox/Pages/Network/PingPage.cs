using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Controls;

namespace ToolBox.Pages;

/// <summary>One tab per host. Count, interval, timeout and packet size are set per tab; every tab keeps its own graph, statistics and log.</summary>
public sealed class PingPage : Page
{
    readonly TabView _tabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent, CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.OnPointerOver };

    public PingPage()
    {
        _tabs.AddTabButtonClick += (_, _) => AddTab("");
        _tabs.TabCloseRequested += (_, a) =>
        {
            if (a.Tab.Content is PingSession s) s.Stop();
            _tabs.TabItems.Remove(a.Tab);
            if (_tabs.TabItems.Count == 0) AddTab("");
        };
        var root = new Grid { Padding = new Thickness(24, 8, 24, 0), RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(new TextBlock { Text = "Ping", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        Grid.SetRow(_tabs, 1); root.Children.Add(_tabs);
        Content = root;
        AddTab("www.magestica.com");
        Unloaded += (_, _) => { foreach (TabViewItem t in _tabs.TabItems) (t.Content as PingSession)?.Stop(); };
    }

    void AddTab(string host)
    {
        var s = new PingSession(host);
        var item = new TabViewItem { Header = host.Length > 0 ? host : "New", Content = s, IconSource = new SymbolIconSource { Symbol = Symbol.Message } };
        s.HostChanged += h => item.Header = h;
        _tabs.TabItems.Add(item);
        _tabs.SelectedItem = item;
    }
}

public sealed class PingSession : UserControl
{
    readonly TextBox _host = new() { PlaceholderText = "host or IP", MinWidth = 240 };
    readonly NumberBox _count = new() { Header = "Count (0 = until stopped)", Value = 0, Minimum = 0, Maximum = 100000, Width = 190, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    readonly NumberBox _interval = new() { Header = "Interval (ms)", Value = 1000, Minimum = 100, Maximum = 60000, Width = 150, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    readonly NumberBox _timeout = new() { Header = "Timeout (ms)", Value = 1000, Minimum = 100, Maximum = 10000, Width = 150, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    readonly NumberBox _size = new() { Header = "Bytes", Value = 32, Minimum = 0, Maximum = 65500, Width = 130, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    readonly SparkChart _chart = new() { Height = 170, AutoMax = true, Capacity = 120 };
    readonly TextBlock _stats = Ui.Hint("");
    readonly TextBlock _big = Ui.Value("-");
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 160, MaxHeight = 260, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
    readonly Button _go;
    CancellationTokenSource? _cts;
    public event Action<string>? HostChanged;

    public PingSession(string host)
    {
        _host.Text = host;
        _go = new Button { Content = "Start", Style = (Style)Application.Current.Resources["AccentButtonStyle"], VerticalAlignment = VerticalAlignment.Bottom };
        _go.Click += (_, _) => { if (_cts != null) _cts.Cancel(); else _ = Loop(); };
        _host.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter && _cts == null) _ = Loop(); };
        var bar = Ui.Wrap(_host, _count, _interval, _timeout, _size, _go);
        var head = new Grid();
        head.Children.Add(Ui.Title("Round-trip time (ms)"));
        _big.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(_big);
        Content = Ui.Page(Ui.Stack(12, bar, Ui.Card(Ui.Stack(8, head, _chart, _stats)), _log));
    }

    public void Stop() => _cts?.Cancel();

    async Task Loop()
    {
        var host = Engine.NetEngine.CleanHost(_host.Text);
        if (host.Length == 0) return;
        _host.Text = host;
        HostChanged?.Invoke(host);
        _cts = new CancellationTokenSource(); var ct = _cts.Token;
        _go.Content = "Stop"; _log.Text = "";
        int sent = 0, lost = 0, limit = (int)_count.Value, okCount = 0; double min = double.MaxValue, max = 0, sum = 0, jitter = 0, last = -1;
        var log = new Queue<string>();
        var timeout = (int)_timeout.Value; var size = (int)_size.Value; var interval = (int)_interval.Value;
        while (!ct.IsCancellationRequested && (limit == 0 || sent < limit))
        {
            try
            {
                var r = await Backend.GetAsync($"/api/net/ping?host={Uri.EscapeDataString(host)}&timeout={timeout}&size={size}");
                sent++;
                var ok = r.Get("ok").ValueKind == JsonValueKind.True;
                if (ok)
                {
                    var ms = r.Num("ms"); okCount++; sum += ms; min = Math.Min(min, ms); max = Math.Max(max, ms);
                    if (last >= 0) jitter += (Math.Abs(ms - last) - jitter) / 16; // smoothed, like RFC 3550
                    last = ms; _chart.Tint = Ui.Blue; _chart.Add(ms); _big.Text = $"{ms:0} ms";
                }
                else { lost++; _chart.Tint = Ui.Red; _chart.Add(0); _big.Text = "lost"; }
                log.Enqueue($"{r.Str("from"),-16} {(ok ? $"{r.Num("ms"):0} ms  TTL {r.Num("ttl")}  {size} bytes" : r.Str("status"))}"); while (log.Count > 300) log.Dequeue();
                _log.Text = string.Join("\n", log); _log.SelectionStart = _log.Text.Length;
                _stats.Text = $"sent {sent} · lost {lost} ({lost * 100 / Math.Max(1, sent)} %)" + (okCount > 0 ? $" · min {min:0} · avg {sum / okCount:0} · max {max:0} ms · jitter {jitter:0.0} ms" : "");
            }
            catch (Exception e) { log.Enqueue(e.Message); _log.Text = string.Join("\n", log); break; }
            try { await Task.Delay(interval, ct); } catch { }
        }
        _cts.Dispose(); _cts = null; _go.Content = "Start";
    }
}
