using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ToolBox.Controls;

namespace ToolBox.Pages;

/// <summary>One live graph per temperature sensor Windows shares: thermal zones, disks and NVIDIA GPUs.</summary>
public sealed partial class TempsPage : Page
{
    sealed record Sensor(string Group, string Name)
    {
        public TextBlock Value = Ui.Value("-");
        public ProgressBar Bar = new() { Minimum = 0, Maximum = 100 };
        public SparkChart Chart = new() { Max = 110, Height = 120 };
        public TextBlock Range = Ui.Hint("");
    }

    readonly Dictionary<string, Sensor> _sensors = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _busy;

    public TempsPage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => { _timer.Start(); await TickAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _sensors.Clear();
        Body.Content = null;
        Busy.Visibility = Visibility.Visible;
        await TickAsync();
    }

    async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var r = await Backend.GetAsync("/api/hw/temps");
            var list = r.Get("temps").Items().ToList();
            var rebuild = list.Count != _sensors.Count;
            foreach (var t in list)
            {
                var key = t.Str("group") + "|" + t.Str("name");
                if (!_sensors.TryGetValue(key, out var s)) { s = new Sensor(t.Str("group"), t.Str("name")); _sensors[key] = s; }
                var c = t.Num("c");
                var col = Ui.TempColor(c, t.Num("warn"), t.Num("crit"));
                s.Value.Text = $"{c:0} °C";
                s.Value.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(col);
                s.Bar.Value = Math.Clamp(c, 0, 100);
                s.Bar.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(col);
                s.Chart.Tint = col;
                s.Chart.Add(c);
                var d = s.Chart.Data;
                s.Range.Text = $"min {d.Min():0} · max {d.Max():0} °C" + (t.Num("crit") > 0 ? $" · limit {t.Num("crit"):0} °C" : "");
            }
            if (rebuild || Body.Content == null) Build();
            Busy.Visibility = Visibility.Collapsed;
        }
        catch (Exception e)
        {
            Body.Content = new TextBlock { Text = e.Message, Margin = new Thickness(24) };
            Busy.Visibility = Visibility.Collapsed;
        }
        _busy = false;
    }

    void Build()
    {
        if (_sensors.Count == 0)
        {
            Body.Content = new TextBlock { Text = "Windows reports no temperature sensors for this PC.", Margin = new Thickness(24) };
            return;
        }
        var page = new StackPanel { Spacing = 8 };
        foreach (var g in _sensors.Values.GroupBy(s => s.Group))
        {
            page.Children.Add(new TextBlock { Text = g.Key, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 10, 0, 0) });
            page.Children.Add(Ui.Tiles(g.Select(Card)));
        }
        page.Children.Add(Ui.Hint("Shown: ACPI thermal zones, disks and NVIDIA GPUs. Per-core CPU temperature and AMD / Intel GPU sensors need a kernel driver, which Windows does not share with normal apps."));
        Body.Content = Ui.Page(page);
    }

    static UIElement Card(Sensor s)
    {
        var head = new Grid();
        head.Children.Add(Ui.Title(s.Name));
        s.Value.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(s.Value);
        return Ui.Card(Ui.Stack(10, head, s.Bar, s.Chart, s.Range));
    }
}
