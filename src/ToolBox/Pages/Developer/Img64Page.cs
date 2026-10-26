// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ToolBox.Pages;

public sealed class Img64Page : ToolPage
{
    readonly Image _preview = new() { MaxHeight = 200, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _info = Ui.Hint("Choose an image to convert.");
    readonly TextBox _out = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 220, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
    readonly CheckBox _dataUri = new() { Content = "Data URI (data:image/...;base64,)", IsChecked = true };
    byte[]? _bytes; string _mime = "image/png";

    public Img64Page() : base("Image to Base64")
    {
        _dataUri.Click += (_, _) => Fill();
        Body.Children.Add(Ui.Wrap(Btn("Choose image...", async () => await Choose(), true), _dataUri, Btn("Copy", () =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(_out.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        })));
        Body.Children.Add(_info); Body.Children.Add(_preview); Body.Children.Add(_out);
    }

    async Task Choose()
    {
        var f = await Pickers.Open(".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".svg");
        if (f == null) return;
        _bytes = (await Windows.Storage.FileIO.ReadBufferAsync(f)).ToArray();
        _mime = f.ContentType is { Length: > 0 } ? f.ContentType : "application/octet-stream";
        _info.Text = $"{f.Name} · {Json.Bytes(_bytes.Length)} · Base64 is {Json.Bytes(_bytes.Length * 4 / 3.0)}";
        try
        {
            using var s = (await f.OpenReadAsync());
            var bmp = new BitmapImage(); await bmp.SetSourceAsync(s); _preview.Source = bmp;
        }
        catch { _preview.Source = null; }
        Fill();
    }

    void Fill() { if (_bytes != null) _out.Text = (_dataUri.IsChecked == true ? $"data:{_mime};base64," : "") + Convert.ToBase64String(_bytes); }
}
