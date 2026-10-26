// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ToolBox.Pages;

/// <summary>Input box, a mode box and an output box that updates as you type. Everything runs locally in C#.</summary>
public abstract class ConvPage : ToolPage
{
    protected readonly TextBox In = new() { AcceptsReturn = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap };
    protected readonly TextBox Out = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
    protected readonly ComboBox Mode = new() { MinWidth = 200 };
    readonly Func<string, string, string> _fn;

    protected ConvPage(string title, string[] modes, Func<string, string, string> fn) : base(title)
    {
        _fn = fn;
        Mode.ItemsSource = modes;
        Mode.SelectedIndex = 0;
        Mode.SelectionChanged += (_, _) => Run();
        In.TextChanged += (_, _) => Run();
        var copy = Btn("Copy output", () =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(Out.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        });
        Body.Children.Add(Ui.Wrap(Mode, copy));
        Body.Children.Add(In);
        Body.Children.Add(Out);
    }

    void Run()
    {
        if (Mode.SelectedItem is not string m) return;
        try { Out.Text = In.Text.Length == 0 ? "" : _fn(m, In.Text); }
        catch (Exception e) { Out.Text = "Error: " + e.Message; }
    }

    protected static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}
