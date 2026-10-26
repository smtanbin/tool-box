// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace ToolBox.Pages;

/// <summary>User and system environment variables: edit, add, delete, and check that every PATH entry exists.</summary>
public sealed class EnvPage : ToolPage
{
    readonly ComboBox _scope = new() { ItemsSource = new[] { "User", "System" }, SelectedIndex = 0, MinWidth = 140 };
    readonly TextBox _filter = new() { PlaceholderText = "Filter", MinWidth = 240 };
    readonly TextBlock _status = Ui.Hint("");
    readonly StackPanel _rows = new() { Spacing = 2 };
    JsonElement _data;

    public EnvPage() : base("Environment variables")
    {
        _scope.SelectionChanged += (_, _) => Render();
        _filter.TextChanged += (_, _) => Render();
        Body.Children.Add(Ui.Wrap(_scope, _filter,
            Btn("New variable", async () => await Edit(null), true), Btn("Reload", async () => await Load())));
        Body.Children.Add(_status);
        Body.Children.Add(Ui.Card(_rows));
        Loaded += async (_, _) => await Load();
    }

    string Scope => _scope.SelectedIndex == 1 ? "system" : "user";

    async Task Load()
    {
        try { _data = await Backend.GetAsync("/api/env"); Render(); }
        catch (Exception e) { _status.Text = e.Message; }
    }

    void Render()
    {
        _rows.Children.Clear();
        var q = _filter.Text.Trim();
        var admin = _data.Get("admin").ValueKind == JsonValueKind.True;
        _status.Text = Scope == "system" && !admin ? "System variables can be read but not changed: run the app as administrator." : "";
        foreach (var v in _data.Get(Scope).Items().Where(v => q.Length == 0 || v.Str("name").Contains(q, StringComparison.OrdinalIgnoreCase) || v.Str("value").Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var item = v;
            var name = item.Str("name"); var value = item.Str("value");
            var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(6, 4, 6, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var val = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.85, MaxLines = 3 }; Grid.SetColumn(val, 1); g.Children.Add(val);
            var edit = new Button { Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 } }; edit.Click += async (_, _) => await Edit(item);
            Grid.SetColumn(edit, 2); g.Children.Add(edit);
            _rows.Children.Add(g);
        }
    }

    async Task Edit(JsonElement? existing)
    {
        var orig = existing?.Str("name") ?? "";
        var name = new TextBox { Header = "Name", Text = orig };
        var value = new TextBox { Header = "Value", Text = existing?.Str("value") ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120 };
        var expand = new CheckBox { Content = "Expandable (contains %OTHER% references)", IsChecked = existing?.Get("expand").ValueKind == JsonValueKind.True };
        var err = new TextBlock { Foreground = new SolidColorBrush(Ui.Red), TextWrapping = TextWrapping.Wrap };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = (orig.Length == 0 ? "New " : "Edit ") + Scope + " variable", PrimaryButtonText = "Save", CloseButtonText = "Cancel",
            SecondaryButtonText = orig.Length > 0 ? "Delete" : "", DefaultButton = ContentDialogButton.Primary,
            Content = Ui.Stack(10, name, value, expand, err)
        };
        var res = await dlg.ShowAsync();
        try
        {
            if (res == ContentDialogResult.Primary)
                await Backend.PostAsync("/api/env/set", new { scope = Scope, name = name.Text.Trim(), value = value.Text, expand = expand.IsChecked == true, rename = orig.Length > 0 && orig != name.Text.Trim() ? orig : "" });
            else if (res == ContentDialogResult.Secondary)
                await Backend.PostAsync("/api/env/delete", new { scope = Scope, name = orig });
            else return;
            await Load();
        }
        catch (Exception e) { _status.Text = e.Message; }
    }
}
