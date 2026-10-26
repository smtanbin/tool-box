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

/// <summary>AES-256-GCM with a PBKDF2 key. The format matches the web version: 0x01 | salt(16) | iv(12) | ciphertext | tag(16), Base64.</summary>
public sealed class CryptPage : ToolPage
{
    readonly ComboBox _mode = new() { ItemsSource = new[] { "Encrypt", "Decrypt" }, SelectedIndex = 0, MinWidth = 140 };
    readonly PasswordBox _pass = new() { PlaceholderText = "Passphrase", MinWidth = 280 };
    readonly TextBox _in = new() { AcceptsReturn = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap, Header = "Text" };
    readonly TextBox _out = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap, Header = "Result" };
    readonly TextBlock _err = new() { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Ui.Red), TextWrapping = TextWrapping.Wrap };

    public CryptPage() : base("Encrypt / decrypt")
    {
        Body.Children.Add(Ui.Wrap(_mode, _pass, Btn("Run", Run, true)));
        Body.Children.Add(_err); Body.Children.Add(_in); Body.Children.Add(_out);
        Body.Children.Add(Ui.Hint("Everything happens on this PC. Lose the passphrase and the text cannot be recovered."));
    }

    static byte[] Key(string pass, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pass), salt, 210000, HashAlgorithmName.SHA256, 32);

    void Run()
    {
        _err.Text = ""; _out.Text = "";
        try
        {
            if (_pass.Password.Length == 0) throw new InvalidOperationException("Enter a passphrase.");
            if (_mode.SelectedIndex == 0)
            {
                var salt = RandomNumberGenerator.GetBytes(16); var iv = RandomNumberGenerator.GetBytes(12);
                var pt = Encoding.UTF8.GetBytes(_in.Text); var ct = new byte[pt.Length]; var tag = new byte[16];
                using (var aes = new AesGcm(Key(_pass.Password, salt), 16)) aes.Encrypt(iv, pt, ct, tag);
                _out.Text = Convert.ToBase64String(new byte[] { 1 }.Concat(salt).Concat(iv).Concat(ct).Concat(tag).ToArray());
            }
            else
            {
                byte[] b;
                try { b = Convert.FromBase64String(_in.Text.Trim()); } catch { throw new InvalidOperationException("This is not Base64 text encrypted by My Tool Box."); }
                if (b.Length < 1 + 16 + 12 + 16 || b[0] != 1) throw new InvalidOperationException("This is not text encrypted by My Tool Box (unknown format).");
                var salt = b[1..17]; var iv = b[17..29]; var ct = b[29..^16]; var tag = b[^16..]; var pt = new byte[ct.Length];
                try { using var aes = new AesGcm(Key(_pass.Password, salt), 16); aes.Decrypt(iv, ct, tag, pt); }
                catch (CryptographicException) { throw new InvalidOperationException("Wrong passphrase, or the text was changed or damaged."); }
                _out.Text = Encoding.UTF8.GetString(pt);
            }
        }
        catch (Exception e) { _err.Text = e.Message; }
    }
}
