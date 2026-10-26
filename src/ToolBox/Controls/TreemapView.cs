using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolBox.Controls;

/// <summary>Squarified treemap of a scanned folder: nested rectangles whose area is the size on disk. Colour by file type or by top-level folder.</summary>
public sealed class TreemapView : Canvas
{
    public event Action<int, bool>? NodeClicked;
    JsonElement _root; bool _has;
    public string ColorBy { get; set; } = "File type";

    static readonly Dictionary<string, Color> Cats = new()
    {
        ["Code"] = C(0x2A, 0x9D, 0x8F), ["Images"] = C(0x43, 0xA0, 0x47), ["Video"] = C(0x8E, 0x44, 0xAD), ["Audio"] = C(0xD8, 0x4A, 0x8B), ["Archives"] = C(0xE0, 0x8A, 0x1E),
        ["Documents"] = C(0x3F, 0x6F, 0xD0), ["Programs"] = C(0xC6, 0x4B, 0x3C), ["Data"] = C(0x2D, 0x9C, 0xC7), ["Other"] = C(0x7A, 0x86, 0x99)
    };
    static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    static readonly string[] CodeExt = { ".js", ".ts", ".tsx", ".jsx", ".go", ".cs", ".py", ".java", ".c", ".cpp", ".h", ".rs", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sh", ".ps1", ".sql", ".vue", ".md", ".map", ".mod", ".sum" };
    static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tif", ".tiff", ".heic" };
    static readonly string[] VidExt = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv" };
    static readonly string[] AudExt = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" };
    static readonly string[] ArcExt = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".msi" };
    static readonly string[] DocExt = { ".doc", ".docx", ".pdf", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".odt", ".csv" };
    static readonly string[] ExeExt = { ".exe", ".dll", ".sys", ".so", ".dylib", ".bin", ".com", ".lib", ".obj", ".pdb" };
    static readonly string[] DataExt = { ".db", ".sqlite", ".mdb", ".log", ".dat", ".bak", ".vhd", ".vhdx", ".vmdk", ".mdf", ".ldf" };

    public static string Category(string ext) =>
        CodeExt.Contains(ext) ? "Code" : ImgExt.Contains(ext) ? "Images" : VidExt.Contains(ext) ? "Video" : AudExt.Contains(ext) ? "Audio" : ArcExt.Contains(ext) ? "Archives"
        : DocExt.Contains(ext) ? "Documents" : ExeExt.Contains(ext) ? "Programs" : DataExt.Contains(ext) ? "Data" : "Other";

    public static Color CategoryColor(string ext) => Cats[Category(ext)];
    public static readonly Color[] Palette = { C(0x1E, 0x88, 0xE5), C(0xE5, 0x39, 0x35), C(0x43, 0xA0, 0x47), C(0xFB, 0x8C, 0x00), C(0x8E, 0x24, 0xAA), C(0x00, 0x97, 0xA7), C(0xC0, 0xCA, 0x33), C(0xD8, 0x1B, 0x60), C(0x6D, 0x4C, 0x41), C(0x54, 0x6E, 0x7A) };

    public TreemapView()
    {
        SizeChanged += (_, _) => Redraw();
        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    }

    public void SetData(JsonElement root) { _root = root; _has = root.ValueKind == JsonValueKind.Object; Redraw(); }

    int _count;

    void Redraw()
    {
        Children.Clear(); _count = 0;
        if (!_has || ActualWidth < 20 || ActualHeight < 20) return;
        Node(_root, new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight), 0, -1);
    }

    static double Size(JsonElement e) => e.TryGetProperty("s", out var v) ? v.GetDouble() : 0;

    void Node(JsonElement n, Windows.Foundation.Rect r, int depth, int branch)
    {
        if (r.Width < 3 || r.Height < 3 || _count > 3500) return;
        var kids = n.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.Array ? k.EnumerateArray().ToList() : new List<JsonElement>();
        var isDir = n.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.True;
        var id = n.TryGetProperty("i", out var iv) ? iv.GetInt32() : -1;
        var name = n.TryGetProperty("n", out var nv) ? nv.GetString() ?? "" : "";
        var ext = n.TryGetProperty("e", out var ev) ? ev.GetString() ?? "" : "";

        if (depth > 0 && (!isDir || kids.Count == 0))
        {
            var col = ColorBy == "Folder" && branch >= 0 ? Palette[branch % Palette.Length] : id < 0 ? Cats["Other"] : isDir ? Cats["Other"] : CategoryColor(ext);
            Add(r, col, name, Size(n), id, isDir, false);
            return;
        }
        var inner = r;
        if (depth > 0)
        {
            Add(r, Color.FromArgb(255, 176, 184, 200), name, Size(n), id, isDir, true);
            var head = r.Width > 36 && r.Height > 26 ? 14.0 : 0;
            inner = new Windows.Foundation.Rect(r.X + 1, r.Y + head + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - head - 2));
        }
        var items = kids.Where(x => Size(x) > 0).OrderByDescending(Size).ToList();
        var i = 0;
        foreach (var (child, rect) in Squarify(items, inner))
        {
            var b = depth == 0 ? i++ : branch;
            Node(child, rect, depth + 1, b);
        }
    }

    void Add(Windows.Foundation.Rect r, Color fill, string name, double size, int id, bool isDir, bool header)
    {
        _count++;
        var b = new Border { Width = Math.Max(1, r.Width - 1), Height = Math.Max(1, r.Height - 1), Background = new SolidColorBrush(fill), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), BorderThickness = new Thickness(1), IsHitTestVisible = true };
        if (r.Width > 34 && r.Height > (header ? 14 : 24))
        {
            var t = new TextBlock { FontSize = 10.5, Foreground = new SolidColorBrush(header ? Color.FromArgb(255, 30, 36, 48) : Microsoft.UI.Colors.White), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(3, 1, 2, 0), IsHitTestVisible = false };
            t.Text = header || r.Height < 36 ? name : name + "\n" + Json.Bytes(size);
            t.MaxHeight = Math.Max(0, r.Height - 2);
            b.Child = t;
        }
        SetLeft(b, r.X); SetTop(b, r.Y);
        ToolTipService.SetToolTip(b, $"{name}\n{Json.Bytes(size)}");
        if (id >= 0) b.Tapped += (_, e) => { e.Handled = true; NodeClicked?.Invoke(id, isDir); };
        Children.Add(b);
    }

    /// <summary>Squarified treemap (Bruls et al.): lay items out in rows along the shorter side so cells stay close to square.</summary>
    static IEnumerable<(JsonElement, Windows.Foundation.Rect)> Squarify(List<JsonElement> items, Windows.Foundation.Rect r)
    {
        var total = items.Sum(Size);
        if (total <= 0 || r.Width <= 0 || r.Height <= 0) yield break;
        var scale = r.Width * r.Height / total;
        var rest = items.ToList(); double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        while (rest.Count > 0)
        {
            var side = Math.Min(w, h); var row = new List<JsonElement>(); double rowArea = 0, best = double.MaxValue, rowMin = double.MaxValue, rowMax = 0;
            foreach (var it in rest)
            {
                var a = Size(it) * scale; var ra = rowArea + a;
                var mn = Math.Min(rowMin, a); var mx = Math.Max(rowMax, a);
                var worst = Math.Max(side * side * mx / (ra * ra), ra * ra / (side * side * mn));
                if (row.Count > 0 && worst > best) break;
                best = worst; row.Add(it); rowArea = ra; rowMin = mn; rowMax = mx;
            }
            rest.RemoveRange(0, row.Count);
            var thick = rowArea / side;
            double pos = 0;
            foreach (var it in row)
            {
                var len = Size(it) * scale / thick;
                yield return (it, w >= h ? new Windows.Foundation.Rect(x, y + pos, thick, len) : new Windows.Foundation.Rect(x + pos, y, len, thick));
                pos += len;
            }
            if (w >= h) { x += thick; w -= thick; } else { y += thick; h -= thick; }
        }
    }
}
