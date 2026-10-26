using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ToolBox.Pages;

public sealed class ProcRow
{
    public uint Pid { get; init; }
    public string Name { get; init; } = "";
    public double Cpu { get; init; }
    public long Mem { get; init; }
    public uint Threads { get; init; }
    public string Path { get; init; } = "";
    public bool Locked { get; init; }
    public string CpuText => Cpu.ToString("0.0");
    public string MemText => Json.Bytes(Mem);
}

/// <summary>Processes with CPU and memory, search, sort and end task. Updates every 2 seconds.</summary>
public sealed partial class ProcessPage : Page
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    List<ProcRow> _rows = new();
    string _query = "";
    string _sort = "Cpu";
    int _dir = -1;
    uint? _selected;
    bool _busy;

    public ProcessPage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await LoadAsync();
        Loaded += async (_, _) => { _timer.Start(); await LoadAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var r = await Backend.GetAsync("/api/proc/list");
            _rows = r.Get("procs").Items().Select(p => new ProcRow
            {
                Pid = (uint)p.Num("pid"), Name = p.Str("name"), Cpu = p.Num("cpu"), Mem = (long)p.Num("mem"),
                Threads = (uint)p.Num("threads"), Path = p.Str("path"), Locked = p.Get("locked").ValueKind == JsonValueKind.True
            }).ToList();
            Summary.Text = $"{_rows.Count} processes · CPU {r.Num("cpu"):0} % · memory {Json.Bytes(r.Num("memUsed"))} of {Json.Bytes(r.Num("memTotal"))}";
            Render();
        }
        catch (Exception e) { Summary.Text = e.Message; }
        _busy = false;
    }

    void Render()
    {
        IEnumerable<ProcRow> q = _rows;
        if (_query.Length > 0)
            q = q.Where(p => p.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(_query, StringComparison.OrdinalIgnoreCase) || p.Pid.ToString() == _query);
        Func<ProcRow, object> key = _sort switch
        {
            "Name" => p => p.Name.ToLowerInvariant(),
            "Pid" => p => p.Pid,
            "Mem" => p => p.Mem,
            "Threads" => p => p.Threads,
            "Path" => p => p.Path.ToLowerInvariant(),
            _ => p => p.Cpu
        };
        var sorted = (_dir > 0 ? q.OrderBy(key) : q.OrderByDescending(key)).ToList();
        List.ItemsSource = sorted;
        if (_selected is uint pid)
        {
            var again = sorted.FirstOrDefault(p => p.Pid == pid);
            if (again != null) List.SelectedItem = again;
        }
    }

    void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _query = sender.Text.Trim();
        Render();
    }

    void Sort_Click(object sender, RoutedEventArgs e)
    {
        var k = (string)((HyperlinkButton)sender).Tag;
        _dir = _sort == k ? -_dir : (k is "Name" or "Path" ? 1 : -1);
        _sort = k;
        Render();
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var p = List.SelectedItem as ProcRow;
        if (p != null) _selected = p.Pid;
        EndTask.IsEnabled = EndTree.IsEnabled = p is { Locked: false };
    }

    async void EndTask_Click(object sender, RoutedEventArgs e) => await Kill(false);
    async void EndTree_Click(object sender, RoutedEventArgs e) => await Kill(true);

    async Task Kill(bool tree)
    {
        if (List.SelectedItem is not ProcRow p || p.Locked) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = tree ? "End process tree?" : "End task?",
            Content = $"{p.Name} (PID {p.Pid}) will be closed immediately. Unsaved work in it is lost.",
            PrimaryButtonText = "End",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        try { await Backend.PostAsync("/api/proc/kill", new { pids = new[] { p.Pid }, tree }); }
        catch (Exception e) { Summary.Text = e.Message; }
        await LoadAsync();
    }
}
