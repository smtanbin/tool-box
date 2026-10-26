// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Controls;

namespace ToolBox.Pages;

public sealed class DnsPage : ToolPage
{
    readonly TextBox _name = new() { Text = "www.magestica.com", PlaceholderText = "example.com or an IP address", MinWidth = 320 };
    readonly TextBox _server = new() { PlaceholderText = "DNS server (optional)", MinWidth = 200 };
    readonly ComboBox _type = new() { ItemsSource = new[] { "All", "A", "AAAA", "CNAME", "MX", "NS", "TXT", "PTR" }, SelectedIndex = 0 };

    public DnsPage() : base("DNS lookup")
    {
        var go = Btn("Look up", async () => await Run(), true);
        _name.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) await Run(); };
        Body.Children.Add(Ui.Wrap(_name, _type, _server, go));
    }

    async Task Run()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) return;
        while (Body.Children.Count > 1) Body.Children.RemoveAt(1);
        Body.Children.Add(new ProgressBar { IsIndeterminate = true });
        try
        {
            var q = $"/api/net/dns?name={Uri.EscapeDataString(name)}&type={_type.SelectedItem}&server={Uri.EscapeDataString(_server.Text.Trim())}";
            var r = await Backend.GetAsync(q);
            while (Body.Children.Count > 1) Body.Children.RemoveAt(1);
            foreach (var res in r.Get("results").Items())
            {
                var recs = res.Get("records").Items().Select(x => (x.Str("type"), x.Str("value") + (x.Str("extra").Length > 0 ? "  (" + x.Str("extra") + ")" : ""))).ToList();
                var head = Ui.Title($"{res.Str("type")}   {res.Num("ms"):0} ms");
                var content = recs.Count > 0 ? (UIElement)Ui.Kv(recs.Select(x => (x.Item1, x.Item2))) : Ui.Hint(res.Str("error").Length > 0 ? res.Str("error") : "No records");
                Body.Children.Add(Ui.Card(Ui.Stack(8, head, content)));
            }
        }
        catch (Exception e) { while (Body.Children.Count > 1) Body.Children.RemoveAt(1); Body.Children.Add(Ui.Hint(e.Message)); }
    }
}
