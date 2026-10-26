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

public sealed class TracePage : ToolPage
{
    readonly TextBox _host = new() { Text = "www.magestica.com", PlaceholderText = "host or IP", MinWidth = 280 };
    readonly CheckBox _names = new() { Content = "Resolve names", IsChecked = true };
    readonly TextBlock _status = Ui.Hint("");
    readonly Button _go;
    readonly Grid _table = new() { RowSpacing = 4, ColumnSpacing = 16 };
    CancellationTokenSource? _cts;

    public TracePage() : base("Traceroute")
    {
        _go = Btn("Trace", Toggle, true);
        Body.Children.Add(Ui.Wrap(_host, _names, _go));
        Body.Children.Add(_status);
        Body.Children.Add(Ui.Card(_table));
        Unloaded += (_, _) => _cts?.Cancel();
    }

    void Toggle() { if (_cts != null) _cts.Cancel(); else _ = Run(); }

    void Row(int r, params string[] cells)
    {
        _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Length; i++)
        {
            var t = new TextBlock { Text = cells[i], FontFamily = i < 4 ? new FontFamily("Consolas") : null, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(t, r); Grid.SetColumn(t, i);
            _table.Children.Add(t);
        }
    }

    async Task Run()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _go.Content = "Stop";
        _table.Children.Clear(); _table.RowDefinitions.Clear(); _table.ColumnDefinitions.Clear();
        foreach (var w in new[] { 50.0, 90, 90, 90 }) _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        _table.ColumnDefinitions.Add(new ColumnDefinition());
        Row(0, "Hop", "Probe 1", "Probe 2", "Probe 3", "Router");
        var host = Engine.NetEngine.CleanHost(_host.Text); _host.Text = host; var silent = 0; var row = 1;
        _status.Text = "Tracing...";
        for (var ttl = 1; ttl <= 30 && !ct.IsCancellationRequested; ttl++)
        {
            try
            {
                var h = await Backend.GetAsync($"/api/net/hop?host={Uri.EscapeDataString(host)}&ttl={ttl}&timeout=1500&names={(_names.IsChecked == true ? 1 : 0)}");
                var probes = h.Get("probes").Items().Select(p => p.Str("from").Length > 0 ? (p.Num("ms") < 1 ? "<1 ms" : $"{p.Num("ms"):0} ms") : "*").ToArray();
                var hop = h.Str("hop"); var name = h.Str("name");
                Row(row++, new[] { ttl.ToString() }.Concat(probes).Concat(new[] { hop.Length == 0 ? "Request timed out" : name.Length > 0 ? $"{name} [{hop}]" : hop }).ToArray());
                silent = hop.Length > 0 ? 0 : silent + 1;
                if (h.Get("done").ValueKind == JsonValueKind.True) { _status.Text = $"Reached {host} ({h.Str("ip")}) in {ttl} hops."; break; }
                if (silent >= 8) { _status.Text = "Eight hops in a row did not answer, so the trace was stopped."; break; }
            }
            catch (Exception e) { _status.Text = e.Message; break; }
        }
        if (_status.Text == "Tracing...") _status.Text = "Stopped.";
        _cts.Dispose(); _cts = null;
        _go.Content = "Trace";
    }
}
