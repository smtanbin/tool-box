using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ToolBox.Engine;

/// <summary>Queries over the scanned folder tree: treemap, file types, largest files, search, duplicates, CSV export and "show in Explorer".</summary>
public static partial class SysEngine
{
    static void RegisterFolder()
    {
        Router.Map("/api/treemap", Treemap);
        Router.Map("/api/types", Types);
        Router.Map("/api/top", Top);
        Router.Map("/api/search", Search);
        Router.Map("/api/export", Export);
        Router.Map("/api/reveal", Reveal);
        Router.Map("/api/dupes/start", DupesStart);
        Router.Map("/api/dupes/status", _ => DupesStatus());
        Router.MapVoid("/api/dupes/cancel", _ => { _dupeCancel = true; });
    }

    static Node NodeOf(Req r)
    {
        lock (ScanLock)
            return int.TryParse(r.S("id", "0"), out var id) && id >= 0 && id < _nodes.Count ? _nodes[id] : throw new InvalidOperationException("no such node");
    }

    static IEnumerable<Node> Walk(Node n)
    {
        var stack = new Stack<Node>(); stack.Push(n);
        while (stack.Count > 0)
        {
            var x = stack.Pop(); yield return x;
            foreach (var k in x.Kids) stack.Push(k);
        }
    }

    static object Item(Node n) => new { id = n.Id, name = n.Name, dir = n.Dir, size = n.Size, mod = n.Mod, path = n.Path() };

    /* ---------- treemap ---------- */

    sealed class Tm { public int i { get; set; } public string n { get; set; } = ""; public long s { get; set; } public bool d { get; set; } public string e { get; set; } = ""; public int o { get; set; } public List<Tm>? k { get; set; } }

    static object Treemap(Req r)
    {
        var n = NodeOf(r);
        var limits = new[] { 400, 120, 60, 40, 30 };
        var budget = 6000; var count = 1;
        Tm Mk(Node c) => new() { i = c.Id, n = c.Name, s = c.Size, d = c.Dir, e = c.Dir ? "" : System.IO.Path.GetExtension(c.Name).ToLowerInvariant() };
        var root = Mk(n);
        var q = new Queue<(Tm tm, Node n, int lvl)>(); q.Enqueue((root, n, 0));
        while (q.Count > 0)
        {
            var (tm, node, lvl) = q.Dequeue();
            var lim = limits[Math.Min(lvl, limits.Length - 1)];
            var shown = 0; var restN = 0; long rest = 0;
            foreach (var c in node.Kids)
            {
                if (c.Size == 0) continue;
                if (shown >= lim || count >= budget) { rest += c.Size; restN++; continue; }
                var ct = Mk(c); (tm.k ??= new()).Add(ct); count++; shown++;
                if (c.Dir && c.Kids.Count > 0) q.Enqueue((ct, c, lvl + 1));
            }
            if (rest > 0) { (tm.k ??= new()).Add(new Tm { i = -1, n = $"({restN} smaller items)", s = rest, o = restN }); tm.k.Sort((a, b) => b.s.CompareTo(a.s)); }
        }
        return root;
    }

    static object Types(Req r)
    {
        var n = NodeOf(r);
        var m = new Dictionary<string, (long size, long count)>();
        foreach (var x in Walk(n).Where(x => !x.Dir))
        {
            var e = System.IO.Path.GetExtension(x.Name).ToLowerInvariant();
            var cur = m.GetValueOrDefault(e);
            m[e] = (cur.size + x.Size, cur.count + 1);
        }
        return new { types = m.OrderByDescending(kv => kv.Value.size).Select(kv => new { ext = kv.Key, size = kv.Value.size, count = kv.Value.count }).ToList(), total = n.Size };
    }

    static object Top(Req r)
    {
        var n = NodeOf(r); var cnt = r.I("n", 200, 1, 5000);
        var ext = r.Q["ext"];
        return Walk(n).Where(x => !x.Dir && (ext == null || System.IO.Path.GetExtension(x.Name).Equals(ext, StringComparison.OrdinalIgnoreCase))).OrderByDescending(x => x.Size).Take(cnt).Select(Item).ToList();
    }

    static object Search(Req r)
    {
        var n = NodeOf(r); var q = r.S("q").Trim().ToLowerInvariant();
        var minSize = long.TryParse(r.S("min"), out var ms) ? ms : 0;
        var wild = q.Contains('*') || q.Contains('?');
        var rx = wild ? new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(q).Replace(@"\*", ".*").Replace(@"\?", ".") + "$") : null;
        var hits = Walk(n).Where(x => x != n && x.Size >= minSize && (q.Length == 0 || (wild ? rx!.IsMatch(x.Name.ToLowerInvariant()) : x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))).OrderByDescending(x => x.Size).ToList();
        return new { items = hits.Take(500).Select(Item).ToList(), total = hits.Count };
    }

    static object? Export(Req r)
    {
        var n = NodeOf(r); var withFiles = r.S("files") == "1";
        var sb = new StringBuilder("Path,Type,SizeBytes,Files,Folders,Modified\r\n");
        string C(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        foreach (var x in Walk(n).Where(x => x.Dir || withFiles))
            sb.Append($"{C(x.Path())},{(x.Dir ? "Folder" : "File")},{x.Size},{x.Files},{x.Dirs},{(x.Mod > 0 ? DateTimeOffset.FromUnixTimeSeconds(x.Mod).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "")}\r\n");
        return new { csv = sb.ToString() };
    }

    static object? Reveal(Req r)
    {
        var n = NodeOf(r); var p = n.Path();
        if (n.Dir && r.S("open") == "1") Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p}\"") { UseShellExecute = true })?.Dispose();
        else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true })?.Dispose();
        return null;
    }

    /* ---------- duplicates ---------- */

    static bool _dupeRunning, _dupeCancel; static string _dupePhase = ""; static long _dupeDone, _dupeTotal; static List<object> _dupeGroups = new(); static long _dupeWasted;

    static string HashFile(string path, long limit)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var h = SHA256.Create();
        if (limit <= 0) return Convert.ToHexString(h.ComputeHash(f));
        var buf = new byte[limit]; var n = f.Read(buf, 0, buf.Length);
        return Convert.ToHexString(h.ComputeHash(buf, 0, n));
    }

    static object? DupesStart(Req r)
    {
        var root = NodeOf(r); var minSize = long.TryParse(r.S("min"), out var m) ? Math.Max(1, m) : 1024;
        lock (ScanLock)
        {
            if (_dupeRunning) throw new InvalidOperationException("a duplicate search is already running");
            _dupeRunning = true; _dupeCancel = false; _dupeDone = 0; _dupeTotal = 0; _dupeGroups = new(); _dupeWasted = 0; _dupePhase = "Grouping by size";
        }
        _ = Task.Run(() =>
        {
            try
            {
                var bySize = Walk(root).Where(x => !x.Dir && x.Size >= minSize).GroupBy(x => x.Size).Where(g => g.Count() > 1).ToList();
                var cands = bySize.SelectMany(g => g).ToList();
                _dupeTotal = cands.Count * 2; _dupePhase = "Reading the start of each file";
                var quick = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
                Parallel.ForEach(cands, new ParallelOptions { MaxDegreeOfParallelism = 6 }, c =>
                {
                    if (_dupeCancel) return;
                    try { quick[c.Id] = HashFile(c.Path(), 65536); } catch { }
                    Interlocked.Increment(ref _dupeDone);
                });
                var stage2 = cands.Where(c => quick.ContainsKey(c.Id)).GroupBy(c => (c.Size, quick[c.Id])).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
                _dupePhase = "Comparing whole files"; _dupeDone = cands.Count - stage2.Count;
                var full = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
                Parallel.ForEach(stage2, new ParallelOptions { MaxDegreeOfParallelism = 4 }, c =>
                {
                    if (_dupeCancel) return;
                    try { full[c.Id] = HashFile(c.Path(), 0); } catch { }
                    Interlocked.Increment(ref _dupeDone);
                });
                var groups = stage2.Where(c => full.ContainsKey(c.Id)).GroupBy(c => (c.Size, full[c.Id])).Where(g => g.Count() > 1)
                    .Select(g => new { size = g.Key.Size, wasted = g.Key.Size * (g.Count() - 1), items = g.Select(c => new { id = c.Id, path = c.Path(), mod = c.Mod }).ToList() })
                    .OrderByDescending(g => g.wasted).ToList();
                lock (ScanLock) { _dupeGroups = groups.Cast<object>().ToList(); _dupeWasted = groups.Sum(g => g.wasted); }
            }
            catch { }
            finally { lock (ScanLock) _dupeRunning = false; }
        });
        return null;
    }

    static object DupesStatus()
    {
        lock (ScanLock) return new { running = _dupeRunning, phase = _dupePhase, done = _dupeDone, total = _dupeTotal, groups = _dupeGroups, wasted = _dupeWasted, cancelled = _dupeCancel };
    }
}
