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

public sealed class HashPage : ConvPage
{
    public HashPage() : base("Hash text", new[] { "All", "MD5", "SHA-1", "SHA-256", "SHA-384", "SHA-512" }, (m, s) =>
    {
        var data = Encoding.UTF8.GetBytes(s);
        string One(string n) => n switch
        {
            "MD5" => Hex(MD5.HashData(data)), "SHA-1" => Hex(SHA1.HashData(data)), "SHA-256" => Hex(SHA256.HashData(data)),
            "SHA-384" => Hex(SHA384.HashData(data)), _ => Hex(SHA512.HashData(data))
        };
        return m == "All" ? string.Join("\n", new[] { "MD5", "SHA-1", "SHA-256", "SHA-384", "SHA-512" }.Select(n => $"{n,-8}{One(n)}")) : One(m);
    }) { }
}

public sealed class EncodePage : ConvPage
{
    public EncodePage() : base("Base64 & encoders", new[] { "Base64 encode", "Base64 decode", "Base64url encode", "Base64url decode", "URL encode", "URL decode", "HTML escape", "HTML unescape", "Hex encode", "Hex decode" }, (m, s) => m switch
    {
        "Base64 encode" => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)),
        "Base64 decode" => Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim())),
        "Base64url encode" => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        "Base64url decode" => Encoding.UTF8.GetString(FromB64Url(s.Trim())),
        "URL encode" => Uri.EscapeDataString(s),
        "URL decode" => Uri.UnescapeDataString(s),
        "HTML escape" => System.Net.WebUtility.HtmlEncode(s),
        "HTML unescape" => System.Net.WebUtility.HtmlDecode(s),
        "Hex encode" => Hex(Encoding.UTF8.GetBytes(s)),
        _ => Encoding.UTF8.GetString(Convert.FromHexString(s.Replace(" ", "").Trim()))
    }) { }

    internal static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

public sealed class JsonPage : ConvPage
{
    public JsonPage() : base("JSON formatter", new[] { "Format", "Minify", "Validate" }, (m, s) =>
    {
        using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return m switch
        {
            "Format" => JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            "Minify" => JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            _ => "Valid JSON (" + doc.RootElement.ValueKind + ")"
        };
    }) { }
}

public sealed class JwtPage : ConvPage
{
    public JwtPage() : base("JWT decoder", new[] { "Decode" }, (_, s) =>
    {
        var parts = s.Trim().Split('.');
        if (parts.Length < 2) throw new FormatException("A JWT has three parts separated by dots.");
        string Pretty(string p)
        {
            using var d = JsonDocument.Parse(EncodePage.FromB64Url(p));
            return JsonSerializer.Serialize(d.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        var sb = new StringBuilder("HEADER\n" + Pretty(parts[0]) + "\n\nPAYLOAD\n" + Pretty(parts[1]));
        using var pay = JsonDocument.Parse(EncodePage.FromB64Url(parts[1]));
        foreach (var claim in new[] { "iat", "nbf", "exp" })
            if (pay.RootElement.TryGetProperty(claim, out var v) && v.TryGetInt64(out var t))
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(t);
                sb.Append($"\n{claim}: {dt.LocalDateTime:yyyy-MM-dd HH:mm:ss}" + (claim == "exp" ? (dt < DateTimeOffset.UtcNow ? "  (expired)" : "  (valid)") : ""));
            }
        sb.Append("\n\nThe signature is not verified.");
        return sb.ToString();
    }) { }
}

public sealed class TimePage : ConvPage
{
    public TimePage() : base("Timestamp converter", new[] { "Unix to date", "Date to Unix" }, (m, s) =>
    {
        s = s.Trim();
        if (m == "Unix to date")
        {
            var n = long.Parse(s);
            var dt = Math.Abs(n) > 99_999_999_999 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
            return $"UTC    {dt.UtcDateTime:yyyy-MM-dd HH:mm:ss}\nLocal  {dt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\nISO    {dt:O}";
        }
        var d = DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        return $"Seconds       {d.ToUnixTimeSeconds()}\nMilliseconds  {d.ToUnixTimeMilliseconds()}";
    })
    {
        In.Text = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    }
}

public sealed class BasePage : ConvPage
{
    public BasePage() : base("Number base converter", new[] { "Auto detect (0x, 0b, 0o)", "Decimal", "Hexadecimal", "Binary", "Octal" }, (m, s) =>
    {
        s = s.Trim().Replace("_", "").Replace(" ", "");
        var neg = s.StartsWith('-');
        if (neg) s = s[1..];
        var radix = m switch { "Decimal" => 10, "Hexadecimal" => 16, "Binary" => 2, "Octal" => 8, _ => 10 };
        if (m.StartsWith("Auto"))
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { radix = 16; s = s[2..]; }
            else if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { radix = 2; s = s[2..]; }
            else if (s.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) { radix = 8; s = s[2..]; }
        }
        BigInteger v = 0;
        foreach (var c in s.ToLowerInvariant())
        {
            var dgt = char.IsDigit(c) ? c - '0' : c - 'a' + 10;
            if (dgt < 0 || dgt >= radix) throw new FormatException($"'{c}' is not a base {radix} digit");
            v = v * radix + dgt;
        }
        if (neg) v = -v;
        string To(int b)
        {
            if (v.IsZero) return "0";
            var a = BigInteger.Abs(v); var sb = new StringBuilder();
            while (a > 0) { sb.Insert(0, "0123456789abcdef"[(int)(a % b)]); a /= b; }
            return (v < 0 ? "-" : "") + sb;
        }
        return $"Decimal      {v}\nHexadecimal  0x{To(16)}\nBinary       0b{To(2)}\nOctal        0o{To(8)}";
    }) { }
}

public sealed class TokenPage : ToolPage
{
    readonly TextBox _out = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 220, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
    readonly ComboBox _kind = new() { ItemsSource = new[] { "UUID v4", "Random hex", "Random Base64", "Password" }, SelectedIndex = 0, MinWidth = 180 };
    readonly NumberBox _len = new() { Value = 32, Minimum = 4, Maximum = 256, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Header = "Length / bytes" };
    readonly NumberBox _count = new() { Value = 5, Minimum = 1, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Header = "How many" };

    public TokenPage() : base("Token & UUID generator")
    {
        Body.Children.Add(Ui.Wrap(_kind, _len, _count, Btn("Generate", Generate, true)));
        Body.Children.Add(_out);
        Generate();
    }

    void Generate()
    {
        var n = (int)_len.Value; var lines = new List<string>();
        for (var i = 0; i < (int)_count.Value; i++)
            lines.Add(_kind.SelectedItem switch
            {
                "UUID v4" => Guid.NewGuid().ToString(),
                "Random hex" => Convert.ToHexString(RandomNumberGenerator.GetBytes(n)).ToLowerInvariant(),
                "Random Base64" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(n)),
                _ => RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_", n)
            });
        _out.Text = string.Join("\n", lines);
    }
}
