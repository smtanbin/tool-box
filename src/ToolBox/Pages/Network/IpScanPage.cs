// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ToolBox.Pages;

public sealed class IpScanPage : ToolPage
{
    readonly TextBox _range = new() { PlaceholderText = "192.168.1.0/24", MinWidth = 220 };
    readonly TextBox _ports = new() { PlaceholderText = "ports, e.g. 22,80,443 (optional)", MinWidth = 240 };
    readonly ComboBox _local = new() { PlaceholderText = "My networks", MinWidth = 220 };
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100 };
    readonly TextBlock _status = Ui.Hint("");
    readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.None, MinHeight = 300 };
    readonly Button _go, _stop;
    List<JsonElement> _nets = new();
    bool _polling, _alive = true;
    int _shown = -1;

    public IpScanPage() : base("IP scanner")
    {
        _go = Btn("Scan", async () => await Start(), true);
        _stop = Btn("Stop", async () => { try { await Backend.GetAsync("/api/net/scan/cancel"); } catch { } });
        _stop.Visibility = Visibility.Collapsed;
        _local.SelectionChanged += (_, _) => { if (_local.SelectedIndex >= 0) _range.Text = _nets[_local.SelectedIndex].Str("cidr"); };
        Body.Children.Add(Ui.Wrap(_local, _range, _ports, _go, _stop));
        Body.Children.Add(_bar);
        Body.Children.Add(_status);
        Body.Children.Add(Ui.Card(_list));
        Unloaded += async (_, _) => { _alive = false; try { await Backend.GetAsync("/api/net/scan/cancel"); } catch { } };
        Loaded += async (_, _) =>
        {
            try
            {
                _nets = (await Backend.GetAsync("/api/net/local")).Items().ToList();
                _local.ItemsSource = _nets.Select(n => $"{n.Str("cidr")}   {n.Str("name")}").ToList();
                if (_range.Text.Length == 0 && _nets.Count > 0) { _range.Text = _nets[0].Str("cidr"); }
            }
            catch { }
        };
    }

    async Task Start()
    {
        var q = $"range={Uri.EscapeDataString(_range.Text.Trim())}&ports={Uri.EscapeDataString(_ports.Text.Trim())}&timeout=800&arp=1&dns=1";
        try { await Backend.GetAsync("/api/net/scan/start?" + q); } catch (Exception e) { _status.Text = e.Message; return; }
        await Poll();
    }

    async Task Poll()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            while (_alive)
            {
                var s = await Backend.GetAsync("/api/net/scan/status");
                var running = s.Get("running").ValueKind == JsonValueKind.True;
                _go.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
                _stop.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
                var total = s.Num("total");
                _bar.Value = total > 0 ? s.Num("done") / total * 100 : 0;
                var res = s.Get("results").Items().OrderBy(r => Version.TryParse(r.Str("ip"), out var v) ? v : new Version(0, 0)).ToList();
                if (res.Count != _shown) { _shown = res.Count; _list.Items.Clear(); }
                else res.Clear();
                foreach (var r in res)
                {
                    var ports = string.Join(" ", r.Get("ports").Items().Select(p => p.ToString()));
                    var ms = r.Str("via") == "arp" ? "-" : $"{r.Num("ms"):0} ms";
                    _list.Items.Add(new TextBlock { Text = $"{r.Str("ip"),-16}{ms,-9}{r.Str("mac"),-19}{(r.Str("host").Length > 0 ? r.Str("host") : "-"),-34}{ports}", FontFamily = new FontFamily("Consolas"), IsTextSelectionEnabled = true });
                }
                _status.Text = (running ? $"Scanning {s.Str("range")}... {s.Num("done"):N0} / {total:N0}" : $"{(s.Get("cancelled").ValueKind == JsonValueKind.True ? "Cancelled" : "Finished")}: {s.Num("done"):N0} of {total:N0} addresses") + $" · {res.Count} hosts · {s.Num("elapsed"):0.0} s";
                if (!running) break;
                await Task.Delay(500);
            }
        }
        catch (Exception e) { _status.Text = e.Message; }
        _polling = false;
    }
}
