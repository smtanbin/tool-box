using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Controls;

namespace ToolBox.Pages;

/// <summary>CPU-Z style hardware details with live graphs: processor (per logical CPU), memory, every GPU / NPU, network.</summary>
public sealed partial class HardwarePage : Page
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    JsonElement _hw, _gpus;
    bool _busy, _loaded;

    // live widgets
    readonly SparkChart _cpuTotal = new() { Height = 110 };
    readonly List<SparkChart> _cores = new();
    readonly TextBlock _util = Ui.Value("-"), _speed = Ui.Value("-"), _procs = Ui.Value("-"), _threads = Ui.Value("-"), _uptime = Ui.Value("-");
    readonly SparkChart _memChart = new() { Height = 110 };
    readonly TextBlock _memText = Ui.Hint("");
    readonly Dictionary<string, (SparkChart util, SparkChart mem, TextBlock utilText, TextBlock memText)> _gpuLive = new();
    readonly Dictionary<string, (SparkChart chart, TextBlock text)> _netLive = new();

    public HardwarePage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => { await LoadAsync(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(true);

    async Task LoadAsync(bool fresh = false)
    {
        Busy.IsActive = true;
        try
        {
            var hw = Backend.GetAsync("/api/hw" + (fresh ? "?fresh=1" : ""));
            var gpus = Backend.GetAsync("/api/hw/gpus");
            _hw = await hw; _gpus = await gpus;
            _cores.Clear(); _gpuLive.Clear(); _netLive.Clear();
            CpuTab.Content = BuildCpu();
            MemTab.Content = BuildMemory();
            GpuTab.Content = BuildGpu();
            SysTab.Content = BuildSystem();
            NetTab.Content = await BuildNetAsync();
            Tabs.Visibility = Visibility.Visible;
            _loaded = true;
        }
        catch (Exception e)
        {
            CpuTab.Content = new TextBlock { Text = e.Message, Margin = new Thickness(24) };
            Tabs.Visibility = Visibility.Visible;
        }
        Busy.IsActive = false;
    }

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = TickAsync();

    async Task TickAsync()
    {
        if (_busy || !_loaded) return;
        _busy = true;
        try
        {
            var tab = Tabs.SelectedIndex;
            if (tab is 0 or 1) await TickCpuAsync();
            if (tab is 2 or 3) await TickPerfAsync();
        }
        catch { }
        _busy = false;
    }

    async Task TickCpuAsync()
    {
        var r = await Backend.GetAsync("/api/hw/cpu");
        _cpuTotal.Add(r.Num("total"));
        var cores = r.Get("cores").Items().Select(x => x.GetDouble()).ToList();
        for (var i = 0; i < cores.Count && i < _cores.Count; i++) _cores[i].Add(cores[i]);
        _util.Text = $"{r.Num("total"):0} %";
        _speed.Text = r.Num("speed") > 0 ? $"{r.Num("speed") / 1000:0.00} GHz" : "-";
        _procs.Text = r.Num("processes").ToString("N0");
        _threads.Text = r.Num("threads").ToString("N0");
        var up = TimeSpan.FromSeconds(r.Num("uptime"));
        _uptime.Text = $"{(int)up.TotalDays}:{up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}";
        var tot = r.Num("memTotal");
        if (tot > 0)
        {
            _memChart.Add(r.Num("memUsed") / tot * 100);
            _memText.Text = $"{Json.Bytes(r.Num("memUsed"))} of {Json.Bytes(tot)} ({r.Num("memUsed") / tot * 100:0} %) · committed {Json.Bytes(r.Num("commitUsed"))} of {Json.Bytes(r.Num("commitTotal"))}";
        }
    }

    async Task TickPerfAsync()
    {
        var r = await Backend.GetAsync("/api/hw/perf");
        foreach (var g in r.Get("gpu").Items())
        {
            if (!_gpuLive.TryGetValue(g.Str("luid"), out var w)) continue;
            w.util.Add(g.Num("util"));
            w.utilText.Text = $"{g.Num("util"):0} % utilisation";
            if (w.mem.Capacity > 0 && w.mem.Max > 0) w.mem.Add(g.Num("dedicated") / w.mem.Max * 100);
            w.memText.Text = $"{Json.Bytes(g.Num("dedicated"))} of {Json.Bytes(w.mem.Max)} dedicated memory in use";
        }
        foreach (var n in r.Get("net").Items())
        {
            var key = n.Str("name");
            var hit = _netLive.FirstOrDefault(x => Norm(key).Contains(Norm(x.Key)) || Norm(x.Key).Contains(Norm(key)));
            if (hit.Value.chart == null) continue;
            hit.Value.chart.Add(n.Num("rx"));
            hit.Value.text.Text = $"receive {Rate(n.Num("rx"))}   ·   send {Rate(n.Num("tx"))}";
        }
    }

    static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    static string Rate(double b) => b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB/s" : b >= 1024 ? $"{b / 1024:0} KB/s" : $"{b:0} B/s";

    /* ---------- tabs ---------- */

    UIElement BuildCpu()
    {
        var w = _hw.Get("wmi"); var cpu = w.Get("cpu").Items().FirstOrDefault(); var id = _hw.Get("cpuid");
        var name = cpu.Str("Name").Trim();
        var cores = w.Get("cpu").Items().Sum(c => c.Num("NumberOfCores"));
        var threads = w.Get("cpu").Items().Sum(c => c.Num("NumberOfLogicalProcessors"));

        string[] names = { "Utilisation", "Speed", "Processes", "Threads", "Up time" };
        TextBlock[] vals = { _util, _speed, _procs, _threads, _uptime };
        var stats = Ui.Tiles(Enumerable.Range(0, 5).Select(i => (UIElement)Ui.Stack(2, Ui.Hint(names[i]), vals[i])), 120);

        var coreGrid = Ui.Tiles(Enumerable.Range(0, (int)threads).Select(i =>
        {
            var c = new SparkChart { Height = 56 };
            _cores.Add(c);
            return (UIElement)Ui.Stack(2, Ui.Hint($"CPU {i}"), c);
        }), 150);

        var caches = id.Get("caches").Items().Select(c => (c.Str("name"), $"{c.Num("count")} x {Json.Bytes(c.Num("size"))}, {c.Num("ways")}-way"));
        var rows = new List<(string, string)>
        {
            ("Name", name), ("Code name", id.Str("codename")), ("Package", _hw.Str("socket")), ("Technology", id.Str("tech")),
            ("Vendor", cpu.Str("Manufacturer")), ("Cores / threads", $"{cores} / {threads}"),
            ("Family / model / stepping", id.Str("family").Length > 0 ? $"{id.Str("family")} / {id.Str("model")} / {id.Str("stepping")}" : ""),
            ("Instructions", id.Str("instructions")), ("Rated clock", cpu.Num("MaxClockSpeed") > 0 ? $"{cpu.Num("MaxClockSpeed") / 1000:0.00} GHz" : "")
        };
        rows.AddRange(caches);
        return Ui.Page(Ui.Stack(12,
            Ui.Card(Ui.Stack(4, Ui.Title(name), Ui.Hint($"{id.Str("codename")}  {_hw.Str("socket")}  {cores} cores, {threads} threads"))),
            Ui.Card(Ui.Stack(10, Ui.Title("Live load"), stats, _cpuTotal, Ui.Hint("Per logical processor, last 60 seconds"), coreGrid)),
            Ui.Card(Ui.Stack(10, Ui.Title("Processor"), Ui.Kv(rows)))));
    }

    UIElement BuildMemory()
    {
        var w = _hw.Get("wmi"); var mods = w.Get("memory").Items().ToList(); var live = _hw.Get("live");
        var total = mods.Sum(m => m.Num("Capacity"));
        var types = new Dictionary<int, string> { [20] = "DDR", [21] = "DDR2", [24] = "DDR3", [26] = "DDR4", [34] = "DDR5", [35] = "LPDDR5" };
        var first = mods.FirstOrDefault();
        var speed = first.Num("ConfiguredClockSpeed") > 0 ? first.Num("ConfiguredClockSpeed") : first.Num("Speed");
        var general = new List<(string, string)>
        {
            ("Type", types.GetValueOrDefault((int)first.Num("SMBIOSMemoryType"), "")), ("Size", Json.Bytes(live.Num("memTotal") > 0 ? live.Num("memTotal") : total)),
            ("Modules", mods.Count.ToString()), ("Speed", speed > 0 ? $"{speed} MT/s" : ""), ("Page file", Json.Bytes(live.Num("pageTotal")))
        };
        var table = Ui.Stack(8, Ui.Title($"Memory modules ({mods.Count})"));
        foreach (var m in mods)
            table.Children.Add(Ui.Kv(new[]
            {
                ("Slot", m.Str("DeviceLocator")), ("Size", Json.Bytes(m.Num("Capacity"))), ("Manufacturer", m.Str("Manufacturer").Trim()),
                ("Part number", m.Str("PartNumber").Trim()), ("Speed", $"{m.Num("ConfiguredClockSpeed")} MT/s"), ("Voltage", m.Num("ConfiguredVoltage") > 0 ? $"{m.Num("ConfiguredVoltage") / 1000:0.000} V" : "")
            }));
        return Ui.Page(Ui.Stack(12,
            Ui.Card(Ui.Stack(10, Ui.Title("Memory in use"), _memText, _memChart)),
            Ui.Card(Ui.Stack(10, Ui.Title("General"), Ui.Kv(general))),
            Ui.Card(table)));
    }

    UIElement BuildGpu()
    {
        var wmi = _hw.Get("wmi").Get("gpu").Items().ToList();
        var page = new StackPanel { Spacing = 12 };
        foreach (var a in _gpus.Get("adapters").Items().Where(a => a.Str("kind") != "software"))
        {
            var name = a.Str("name"); var luid = a.Str("luid"); var ded = a.Num("dedicated");
            var w = wmi.FirstOrDefault(g => g.Str("Name").Trim() == name.Trim());
            var kind = a.Str("kind") switch { "igpu" => "Integrated GPU", "npu" => "NPU", _ => "Graphics card" };
            var utilText = Ui.Hint("..."); var memText = Ui.Hint("...");
            var util = new SparkChart { Height = 100, Tint = Ui.Blue };
            var mem = new SparkChart { Height = 80, Tint = Ui.Green, Max = ded };
            if (luid.Length > 0) _gpuLive[luid] = (util, mem, utilText, memText);
            var body = Ui.Stack(10, Ui.Title(name.Trim()), Ui.Hint($"{kind}" + (ded > 0 ? " · " + Json.Bytes(ded) : "")));
            if (luid.Length > 0) { body.Children.Add(utilText); body.Children.Add(util); }
            if (luid.Length > 0 && ded > 0) { body.Children.Add(memText); body.Children.Add(mem); }
            body.Children.Add(Ui.Kv(new[]
            {
                ("Vendor", a.Str("vendor")), ("Dedicated memory", ded > 0 ? Json.Bytes(ded) : "none (uses system memory)"), ("Shared memory", a.Num("shared") > 0 ? Json.Bytes(a.Num("shared")) : ""),
                ("Driver version", w.Str("DriverVersion")), ("Driver date", w.Str("DriverDate")),
                ("Resolution", w.Num("CurrentHorizontalResolution") > 0 ? $"{w.Num("CurrentHorizontalResolution")} x {w.Num("CurrentVerticalResolution")} @ {w.Num("CurrentRefreshRate")} Hz" : "")
            }));
            page.Children.Add(Ui.Card(body));
        }
        foreach (var n in _hw.Get("wmi").Get("npu").Items())
            page.Children.Add(Ui.Card(Ui.Stack(4, Ui.Title("AI accelerator (NPU)"), Ui.Hint(n.Str("Name").Trim()))));
        if (page.Children.Count == 0) page.Children.Add(Ui.Hint("No graphics adapter reported."));
        return Ui.Page(page);
    }

    async Task<UIElement> BuildNetAsync()
    {
        var adapters = (await Backend.GetAsync("/api/net/adapters")).Get("adapters").Items().Where(a => a.Str("type") != "Loopback" && a.Str("status") == "Up").ToList();
        var page = new StackPanel { Spacing = 12 };
        foreach (var a in adapters)
        {
            var chart = new SparkChart { Height = 100, AutoMax = true, Tint = Ui.Blue };
            var text = Ui.Hint("...");
            _netLive[a.Str("description")] = (chart, text);
            var v4 = string.Join(", ", a.Get("addrs").Items().Where(x => x.Get("v6").ValueKind != JsonValueKind.True).Select(x => x.Str("ip")));
            var speed = a.Num("speed");
            page.Children.Add(Ui.Card(Ui.Stack(10, Ui.Title(a.Str("name")), Ui.Hint(a.Str("description")), text, chart, Ui.Kv(new[]
            {
                ("Type", a.Str("type")), ("Link speed", speed >= 1e9 ? $"{speed / 1e9:0.#} Gbps" : speed > 0 ? $"{speed / 1e6:0} Mbps" : ""), ("IPv4", v4), ("MAC", a.Str("mac"))
            }))));
        }
        if (page.Children.Count == 0) page.Children.Add(Ui.Hint("No connected network adapters."));
        return Ui.Page(page);
    }

    UIElement BuildSystem()
    {
        var w = _hw.Get("wmi"); var bb = w.Get("board").Items().FirstOrDefault(); var bios = w.Get("bios").Items().FirstOrDefault();
        var cs = w.Get("system").Items().FirstOrDefault(); var os = w.Get("os").Items().FirstOrDefault();
        return Ui.Page(Ui.Stack(12,
            Ui.Card(Ui.Stack(10, Ui.Title("Operating system"), Ui.Kv(new[]
            {
                ("Edition", os.Str("Caption").Trim()), ("Version", $"{os.Str("Version")} (build {os.Str("BuildNumber")})"), ("Architecture", os.Str("OSArchitecture")),
                ("Installed", os.Str("InstallDate")), ("Last start", os.Str("LastBoot"))
            }))),
            Ui.Card(Ui.Stack(10, Ui.Title("Mainboard"), Ui.Kv(new[]
            {
                ("Manufacturer", bb.Str("Manufacturer")), ("Model", bb.Str("Product").Trim()), ("Version", bb.Str("Version")), ("Chipset", _hw.Str("chipset"))
            }))),
            Ui.Card(Ui.Stack(10, Ui.Title("BIOS"), Ui.Kv(new[]
            {
                ("Brand", bios.Str("Manufacturer")), ("Version", bios.Str("SMBIOSBIOSVersion")), ("Date", bios.Str("ReleaseDate")), ("CPU microcode", _hw.Str("microcode"))
            }))),
            Ui.Card(Ui.Stack(10, Ui.Title("Computer"), Ui.Kv(new[]
            {
                ("Manufacturer", cs.Str("Manufacturer")), ("Model", cs.Str("Model")), ("System type", cs.Str("SystemType"))
            })))));
    }
}
