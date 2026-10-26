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

/// <summary>Builds a multi-size .ico (PNG-compressed frames) from any image Windows can decode.</summary>
public sealed class IcoPage : ToolPage
{
    static readonly int[] Sizes = { 16, 24, 32, 48, 64, 96, 128, 256 };
    readonly List<CheckBox> _boxes = new();
    readonly TextBlock _info = Ui.Hint("Choose an image (a square one works best).");
    readonly Image _preview = new() { MaxHeight = 160, HorizontalAlignment = HorizontalAlignment.Left };
    BitmapDecoder? _decoder; string _name = "icon";

    public IcoPage() : base("Image to ICO")
    {
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var s in Sizes) { var c = new CheckBox { Content = s.ToString(), IsChecked = s is 16 or 32 or 48 or 64 or 128 or 256, MinWidth = 0 }; _boxes.Add(c); sizes.Children.Add(c); }
        Body.Children.Add(Ui.Wrap(Btn("Choose image...", async () => await Choose(), true), Btn("Save .ico...", async () => await Save())));
        Body.Children.Add(Ui.Card(Ui.Stack(8, Ui.Title("Sizes (px)"), sizes)));
        Body.Children.Add(_info); Body.Children.Add(_preview);
    }

    async Task Choose()
    {
        var f = await Pickers.Open(".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico");
        if (f == null) return;
        try
        {
            using var s = await f.OpenReadAsync();
            _decoder = await BitmapDecoder.CreateAsync(s);
            _name = Path.GetFileNameWithoutExtension(f.Name);
            _info.Text = $"{f.Name} · {_decoder.PixelWidth} x {_decoder.PixelHeight}" + (_decoder.PixelWidth != _decoder.PixelHeight ? " · not square, it will be fitted with transparent margins" : "");
            s.Seek(0);
            var bmp = new BitmapImage(); await bmp.SetSourceAsync(s); _preview.Source = bmp;
        }
        catch (Exception e) { _decoder = null; _info.Text = "Could not read that image: " + e.Message; }
    }

    async Task<byte[]> MakeFrame(int size)
    {
        var d = _decoder!;
        var scale = Math.Min((double)size / d.PixelWidth, (double)size / d.PixelHeight);
        uint w = (uint)Math.Max(1, Math.Round(d.PixelWidth * scale)), h = (uint)Math.Max(1, Math.Round(d.PixelHeight * scale));
        var pix = await d.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform { ScaledWidth = w, ScaledHeight = h, InterpolationMode = BitmapInterpolationMode.Fant }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var src = pix.DetachPixelData();
        var canvas = new byte[size * size * 4];
        int ox = (size - (int)w) / 2, oy = (size - (int)h) / 2;
        for (var y = 0; y < h; y++) System.Buffer.BlockCopy(src, y * (int)w * 4, canvas, ((y + oy) * size + ox) * 4, (int)w * 4);
        using var ms = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)size, (uint)size, 96, 96, canvas);
        await enc.FlushAsync();
        ms.Seek(0);
        var bytes = new byte[ms.Size]; await ms.ReadAsync(bytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
        return bytes;
    }

    async Task Save()
    {
        if (_decoder == null) { _info.Text = "Choose an image first."; return; }
        var chosen = Sizes.Where((s, i) => _boxes[i].IsChecked == true).ToList();
        if (chosen.Count == 0) { _info.Text = "Tick at least one size."; return; }
        var file = await Pickers.Save(_name, ".ico", "Icon");
        if (file == null) return;
        var frames = new List<byte[]>();
        foreach (var s in chosen) frames.Add(await MakeFrame(s));
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)chosen.Count);
        var offset = 6 + 16 * chosen.Count;
        for (var i = 0; i < chosen.Count; i++)
        {
            bw.Write((byte)(chosen[i] >= 256 ? 0 : chosen[i])); bw.Write((byte)(chosen[i] >= 256 ? 0 : chosen[i]));
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((ushort)1); bw.Write((ushort)32); bw.Write(frames[i].Length); bw.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) bw.Write(f);
        await Windows.Storage.FileIO.WriteBytesAsync(file, ms.ToArray());
        _info.Text = $"Saved {file.Name}: {chosen.Count} sizes, {Json.Bytes(ms.Length)}.";
    }
}
