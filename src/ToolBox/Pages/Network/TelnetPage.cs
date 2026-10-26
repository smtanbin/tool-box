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

public sealed class TelnetPage : ToolPage
{
    readonly TextBox _ptHost = new() { Text = "www.magestica.com", PlaceholderText = "host", MinWidth = 220 };
    readonly TextBox _ptPorts = new() { Text = "22,80,443,3389", MinWidth = 220 };
    readonly TextBlock _ptStatus = Ui.Hint("");
    readonly StackPanel _ptRows = new() { Spacing = 4 };
    readonly TextBox _tnHost = new() { Text = "www.magestica.com", PlaceholderText = "host", MinWidth = 220 };
    readonly NumberBox _tnPort = new() { Value = 23, Minimum = 1, Maximum = 65535, Width = 110 };
    readonly TextBox _term = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 260, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
    readonly TextBox _line = new() { PlaceholderText = "type a line and press Enter", IsEnabled = false };
    readonly Button _connect, _disconnect;
    int? _id;

    static readonly Dictionary<int, string> Services = new() { [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP", [53] = "DNS", [80] = "HTTP", [110] = "POP3", [135] = "MS RPC", [139] = "NetBIOS", [143] = "IMAP", [443] = "HTTPS", [445] = "SMB", [3306] = "MySQL", [3389] = "RDP", [5432] = "PostgreSQL", [5900] = "VNC", [8080] = "HTTP alt" };

    public TelnetPage() : base("Telnet / ports")
    {
        _connect = Btn("Connect", async () => await Connect(), true);
        _disconnect = Btn("Disconnect", Disconnect);
        _disconnect.Visibility = Visibility.Collapsed;
        _line.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) await Send(); };
        Body.Children.Add(Ui.Card(Ui.Stack(8, Ui.Title("Port test"),
            Ui.Wrap(_ptHost, _ptPorts, Btn("Test", async () => await PortTest(), true)), _ptStatus, _ptRows)));
        Body.Children.Add(Ui.Card(Ui.Stack(8, Ui.Title("Telnet / raw TCP"),
            Ui.Wrap(_tnHost, _tnPort, _connect, _disconnect), _term, _line)));
        Unloaded += (_, _) => Disconnect();
    }

    async Task PortTest()
    {
        var host = _ptHost.Text.Trim();
        if (host.Length == 0) { _ptStatus.Text = "Enter a host name or IP address."; return; }
        _ptStatus.Text = "Testing..."; _ptRows.Children.Clear();
        try
        {
            var r = await Backend.GetAsync($"/api/net/ports?host={Uri.EscapeDataString(host)}&ports={Uri.EscapeDataString(_ptPorts.Text.Trim())}&timeout=1500");
            var results = r.Get("results").Items().ToList();
            foreach (var x in results)
            {
                var open = x.Get("open").ValueKind == JsonValueKind.True;
                var state = open ? "Open" : x.Str("err").Contains("timed out") ? "Filtered" : "Closed";
                var t = new TextBlock { Text = $"{x.Num("port"),-7}{Services.GetValueOrDefault((int)x.Num("port"), ""),-12}{state,-10}{x.Num("ms"):0} ms", FontFamily = new FontFamily("Consolas") };
                t.Foreground = new SolidColorBrush(open ? Ui.Green : state == "Filtered" ? Ui.Amber : Ui.Red);
                _ptRows.Children.Add(t);
            }
            _ptStatus.Text = $"{host} ({r.Str("ip")}): {results.Count(x => x.Get("open").ValueKind == JsonValueKind.True)} of {results.Count} ports open";
        }
        catch (Exception e) { _ptStatus.Text = e.Message; }
    }

    const int MaxTerminalChars = 200_000;

    void Write(string s)
    {
        var t = _term.Text + s;
        if (t.Length > MaxTerminalChars) t = t[^(MaxTerminalChars / 2)..];
        _term.Text = t; _term.SelectionStart = t.Length;
    }

    async Task Connect()
    {
        var host = _tnHost.Text.Trim(); var port = (int)_tnPort.Value;
        if (host.Length == 0) return;
        _term.Text = "";
        Write($"Connecting to {host}:{port} ...\n");
        JsonElement r;
        try { r = await Backend.GetAsync($"/api/net/tcp/open?host={Uri.EscapeDataString(host)}&port={port}&telnet=1"); }
        catch (Exception e) { Write($"Could not connect: {e.Message}\n"); return; }
        if (r.Get("ok").ValueKind != JsonValueKind.True) { Write($"Could not connect: {r.Str("error")}\n"); return; }
        Write($"Connected to {r.Str("remote")} in {r.Num("ms"):0} ms\n");
        var id = (int)r.Num("id"); _id = id;
        _connect.Visibility = Visibility.Collapsed; _disconnect.Visibility = Visibility.Visible; _line.IsEnabled = true;
        while (_id == id)
        {
            try
            {
                var d = await Backend.GetAsync("/api/net/tcp/read?id=" + id);
                var data = d.Str("data");
                if (data.Length > 0) Write(System.Text.RegularExpressions.Regex.Replace(data.Replace("\r\n", "\n"), @"\x1b\[[0-9;?]*[ -/]*[@-~]|[\x00-\x08\x0b-\x1f\x7f]", ""));
                if (d.Get("closed").ValueKind == JsonValueKind.True) { Write("\n[Connection closed]\n"); break; }
            }
            catch (Exception e) { Write($"\n[{e.Message}]\n"); break; }
        }
        if (_id == id) { _id = null; Reset(); }
    }

    void Reset() { _connect.Visibility = Visibility.Visible; _disconnect.Visibility = Visibility.Collapsed; _line.IsEnabled = false; }

    void Disconnect()
    {
        if (_id is not int id) return;
        _id = null; Reset(); Write("[Disconnected]\n");
        _ = Backend.GetAsync("/api/net/tcp/close?id=" + id).ContinueWith(_ => { });
    }

    async Task Send()
    {
        if (_id is not int id) return;
        var text = _line.Text; _line.Text = ""; Write(text + "\n");
        try { await Backend.PostAsync("/api/net/tcp/send?id=" + id, new { data = text + "\r\n" }); } catch (Exception e) { Write($"[send failed: {e.Message}]\n"); }
    }
}
