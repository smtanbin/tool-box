// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Controls;

namespace ToolBox.Pages;

/// <summary>Pages built in code (no XAML) on top of the Go engine's /api/net and /api/hosts endpoints.</summary>
public abstract class ToolPage : Page
{
    protected readonly StackPanel Body = new() { Spacing = 12 };
    protected ToolPage(string title, params UIElement[] actions)
    {
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var a in actions) bar.Children.Add(a);
        Grid.SetColumn(bar, 1);
        head.Children.Add(bar);
        var root = new Grid { Padding = new Thickness(24, 8, 24, 0), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(head);
        var scroll = Ui.Page(Body);
        scroll.Margin = new Thickness(-24, 0, -24, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }

    protected static Button Btn(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => click();
        return b;
    }

    protected void Msg(string text) { Body.Children.Clear(); Body.Children.Add(Ui.Hint(text)); }
}
