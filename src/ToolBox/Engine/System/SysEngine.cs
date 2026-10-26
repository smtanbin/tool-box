using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Win32;

namespace ToolBox.Engine;

/// <summary>Hosts file, environment variables, drives, folder scan and processes. Replaces hosts_windows.go, env_windows.go, scan.go, folder_api.go, process_windows.go.</summary>
public static partial class SysEngine
{
    [DllImport("kernel32.dll")] static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetDiskFreeSpaceEx(string dir, out ulong free, out ulong total, out ulong totalFree);
    [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(byte[]? table, ref int size, bool sort, int af, int cls, int reserved);
    [DllImport("iphlpapi.dll")] static extern int GetExtendedUdpTable(byte[]? table, ref int size, bool sort, int af, int cls, int reserved);
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref ProcEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref ProcEntry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr res);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcEntry
    {
        public uint Size, Usage, Pid; public UIntPtr DefaultHeap; public uint ModuleId, Threads, Ppid; public int PriClass; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }

    public static void Register()
    {
        Router.Map("/api/hosts", _ => HostsRead());
        Router.Map("/api/hosts/save", HostsSave);
        Router.MapVoid("/api/hosts/flush", _ => { Run("ipconfig", "/flushdns"); });
        Router.Map("/api/env", _ => new { user = ReadEnv("user"), system = ReadEnv("system"), admin = IsAdmin() });
        Router.Map("/api/env/set", EnvSet);
        Router.Map("/api/env/delete", r => { var b = BackupEnv(); DeleteEnv(r.B("scope"), r.B("name")); return new { backup = b }; });
        Router.Map("/api/env/pathcheck", PathCheck);
        Router.Map("/api/drives", _ => Drives());
        Router.MapVoid("/api/scan", r => { StartScan(r.S("path")); });
        Router.Map("/api/status", _ => ScanStatus());
        Router.Map("/api/node", NodeInfo);
        Router.Map("/api/proc/list", _ => ProcList());
        Router.Map("/api/proc/ports", _ => new { ports = Ports(Names()), admin = IsAdmin() });
        Router.Map("/api/proc/kill", ProcKill);
        RegisterFolder();
        Router.MapVoid("/api/cancel", _ => { _scanCancel = true; });
        Router.MapVoid("/api/reset", _ => { ResetScan(); });
    }

    public static bool IsAdmin() { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }

    static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(o.Trim());
    }

    /* ---------- hosts ---------- */

    static string HostsPath => Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "System32", "drivers", "etc", "hosts");

    static object HostsRead()
    {
        var p = HostsPath; var exists = File.Exists(p);
        return new { path = p, text = exists ? File.ReadAllText(p).TrimStart('\uFEFF') : "", admin = IsAdmin(), exists };
    }

    static object? HostsSave(Req r)
    {
        var p = HostsPath; var backup = "";
        try
        {
            if (File.Exists(p)) { backup = p + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); File.Copy(p, backup, true); }
            var text = r.B("text").Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n') + "\n";
            File.WriteAllText(p, text.Replace("\n", "\r\n"), new UTF8Encoding(false));
        }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("Access denied: restart My Tool Box as administrator to change the hosts file"); }
        return new { backup };
    }

    /* ---------- environment ---------- */

    const string SysEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    static RegistryKey Open(string scope, bool write)
    {
        try
        {
            return scope switch
            {
                "user" => Registry.CurrentUser.OpenSubKey("Environment", write)!,
                "system" => Registry.LocalMachine.OpenSubKey(SysEnvKey, write)!,
                _ => throw new InvalidOperationException("scope must be user or system")
            };
        }
        catch (System.Security.SecurityException) { throw new InvalidOperationException("Access denied: restart My Tool Box as administrator to change system variables"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("Access denied: restart My Tool Box as administrator to change system variables"); }
    }

    static List<object> ReadEnv(string scope)
    {
        using var k = Open(scope, false);
        var res = new List<(string n, object o)>();
        foreach (var name in k.GetValueNames())
        {
            var kind = k.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
            var v = (string)k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)!;
            res.Add((name, new { name, value = v, expand = kind == RegistryValueKind.ExpandString }));
        }
        return res.OrderBy(x => x.n, StringComparer.OrdinalIgnoreCase).Select(x => x.o).ToList();
    }

    static bool ValidName(string n) => n.Length is > 0 and <= 255 && !n.Contains('=') && !n.Contains('\0') && n == n.Trim();

    static void Broadcast() => SendMessageTimeoutW((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "Environment", 2, 3000, out _);

    static string BackupEnv()
    {
        try
        {
            var dir = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData") ?? "", "MyToolBox", "env-backups");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "env-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(p, JsonSerializer.Serialize(new { saved = DateTime.Now, user = ReadEnv("user"), system = ReadEnv("system") }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x).SkipLast(30)) File.Delete(f);
            return p;
        }
        catch { return ""; }
    }

    static object? EnvSet(Req r)
    {
        var scope = r.B("scope"); var name = r.B("name"); var rename = r.B("rename");
        if (!ValidName(name)) throw new InvalidOperationException("invalid variable name");
        var backup = BackupEnv();
        using (var k = Open(scope, true)) k.SetValue(name, r.B("value"), r.BBool("expand") ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        if (rename.Length > 0 && rename != name) DeleteEnv(scope, rename);
        Broadcast();
        return new { backup };
    }

    static void DeleteEnv(string scope, string name)
    {
        if (!ValidName(name)) throw new InvalidOperationException("scope and name required");
        using var k = Open(scope, true);
        k.DeleteValue(name, false);
        Broadcast();
    }

    static object PathCheck(Req r)
    {
        var paths = r.Body.TryGetProperty("paths", out var p) ? p.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new();
        return paths.Select(x =>
        {
            var e = Regex.Replace(x.Trim().Trim('"'), "%([^%]+)%", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
            var dir = Directory.Exists(e);
            return new { expanded = e, exists = dir || File.Exists(e), dir };
        }).ToList();
    }

    /* ---------- drives and folder scan ---------- */

    static object Drives()
    {
        var res = new List<object>();
        var mask = GetLogicalDrives();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            var root = (char)('A' + i) + @":\";
            var t = GetDriveType(root);
            if (t is not (2 or 3 or 4)) continue;
            if (GetDiskFreeSpaceEx(root, out var free, out var total, out _)) res.Add(new { path = root, total = (long)total, free = (long)free });
        }
        return res;
    }

    sealed class Node
    {
        public int Id; public string Name = ""; public long Size, Files, Dirs, Mod; public bool Dir, Err; public Node? Parent; public List<Node> Kids = new();
        public string Path() => Parent == null ? Name : System.IO.Path.Combine(Parent.Path(), Name);
        public object Dto() => new { id = Id, name = Name, size = Size, files = Files, dirs = Dirs, mod = Mod, dir = Dir, err = Err ? true : (bool?)null, ext = Dir ? "" : System.IO.Path.GetExtension(Name).ToLowerInvariant() };
    }

    static readonly object ScanLock = new();
    static List<Node> _nodes = new();
    static Node? _root;
    static bool _scanning; static long _files, _dirs, _bytes, _errs; static DateTime _t0, _t1; static string _scanRoot = "", _current = ""; static bool _scanCancel;

    static void StartScan(string path)
    {
        path = path.Trim();
        if (path.Length == 0) throw new InvalidOperationException("path required");
        if (!Directory.Exists(path)) throw new InvalidOperationException("That folder does not exist");
        lock (ScanLock)
        {
            if (_scanning) throw new InvalidOperationException("a scan is already running");
            _scanning = true; _files = _dirs = _bytes = _errs = 0; _t0 = DateTime.UtcNow; _t1 = default; _scanRoot = path; _root = null; _nodes = new(); _scanCancel = false;
        }
        _ = Task.Run(() =>
        {
            var nodes = new List<Node>();
            Node Add(Node n) { n.Id = nodes.Count; nodes.Add(n); return n; }
            var root = Add(new Node { Name = path.TrimEnd('\\') == "" ? path : path.Length == 3 && path[1] == ':' ? path : path.TrimEnd('\\'), Dir = true });
            Walk(root, path, Add);
            lock (ScanLock) { if (!_scanCancel) { _nodes = nodes; _root = root; } _scanning = false; _t1 = DateTime.UtcNow; }
        });
    }

    static void Walk(Node dir, string path, Func<Node, Node> add)
    {
        if (_scanCancel) return;
        _current = path; Interlocked.Increment(ref _dirs);
        try
        {
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };
            foreach (var d in new DirectoryInfo(path).EnumerateDirectories("*", opt))
            {
                var n = add(new Node { Name = d.Name, Dir = true, Parent = dir, Mod = new DateTimeOffset(d.LastWriteTimeUtc).ToUnixTimeSeconds() });
                dir.Kids.Add(n);
                Walk(n, d.FullName, add);
                dir.Size += n.Size; dir.Files += n.Files; dir.Dirs += n.Dirs + 1;
            }
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opt))
            {
                var n = add(new Node { Name = f.Name, Size = f.Length, Files = 1, Parent = dir, Mod = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeSeconds() });
                dir.Kids.Add(n); dir.Size += n.Size; dir.Files++;
                Interlocked.Increment(ref _files); Interlocked.Add(ref _bytes, n.Size);
            }
        }
        catch { dir.Err = true; Interlocked.Increment(ref _errs); }
        dir.Kids.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Frees the scanned tree and stops a scan or duplicate search that is still running.</summary>
    static void ResetScan()
    {
        lock (ScanLock)
        {
            if (_scanning) _scanCancel = true;
            _nodes = new(); _root = null;
            _dupeCancel = true; _dupeGroups = new(); _dupeWasted = 0;
        }
    }

    static object ScanStatus()
    {
        var end = _t1 == default ? DateTime.UtcNow : _t1;
        return new { scanning = _scanning, hasRoot = _root != null, root = _root?.Name ?? "", files = _files, dirs = _dirs, bytes = _bytes, errors = _errs, current = _current, elapsed = (end - _t0).TotalSeconds, scanRoot = _scanRoot, cancelled = _scanCancel };
    }

    static object NodeInfo(Req r)
    {
        Node? n;
        lock (ScanLock) n = int.TryParse(r.S("id", "0"), out var id) && id >= 0 && id < _nodes.Count ? _nodes[id] : null;
        if (n == null) throw new InvalidOperationException("no such node");
        var limit = r.I("limit", 1500, 1, 100000);
        var crumbs = new List<object>();
        for (var a = n; a != null; a = a.Parent) crumbs.Insert(0, new { id = a.Id, name = a.Name });
        return new { node = n.Dto(), path = n.Path(), crumbs, children = n.Dir ? n.Kids.Take(limit).Select(k => k.Dto()).ToList() : new List<object>(), more = Math.Max(0, n.Kids.Count - limit) };
    }

    /* ---------- processes ---------- */

    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "system", "registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "lsaiso.exe", "memory compression", "secure system", "dwm.exe" };
    static bool IsProtected(uint pid, string name) => pid == 0 || pid == 4 || pid == Environment.ProcessId || Protected.Contains(name);

    static List<(uint pid, uint ppid, string name, uint threads)> Snapshot()
    {
        var res = new List<(uint, uint, string, uint)>();
        var h = CreateToolhelp32Snapshot(2, 0);
        if (h == (IntPtr)(-1)) return res;
        var e = new ProcEntry { Size = (uint)Marshal.SizeOf<ProcEntry>() };
        if (Process32FirstW(h, ref e))
            do { res.Add((e.Pid, e.Ppid, e.Exe, e.Threads)); e.Size = (uint)Marshal.SizeOf<ProcEntry>(); } while (Process32NextW(h, ref e));
        CloseHandle(h);
        return res;
    }

    static Dictionary<uint, string> Names() => Snapshot().GroupBy(p => p.pid).ToDictionary(g => g.Key, g => g.First().name);

    static readonly object ProcLock = new();
    static readonly Dictionary<int, (TimeSpan busy, DateTime at)> PrevCpu = new();

    static object ProcList()
    {
        lock (ProcLock)
        {
            var now = DateTime.UtcNow; var listening = new Dictionary<uint, List<int>>();
            foreach (var p in Ports(null).Where(p => p.proto is "TCP" or "TCP6" && p.state == "LISTEN"))
            { if (!listening.TryGetValue(p.pid, out var l)) listening[p.pid] = l = new(); if (!l.Contains(p.port)) l.Add(p.port); }
            var live = Process.GetProcesses().ToDictionary(p => p.Id);
            var res = new List<object>(); var seen = new HashSet<int>(); var ncpu = Environment.ProcessorCount;
            foreach (var (pid, ppid, name, threads) in Snapshot())
            {
                double cpu = 0; long mem = 0, priv = 0; uint handles = 0; var path = "";
                if (live.TryGetValue((int)pid, out var p))
                {
                    try
                    {
                        var busy = p.TotalProcessorTime;
                        if (PrevCpu.TryGetValue(p.Id, out var prev) && now > prev.at && busy >= prev.busy) cpu = Math.Min(100, (busy - prev.busy).TotalSeconds / (now - prev.at).TotalSeconds / ncpu * 100);
                        PrevCpu[p.Id] = (busy, now);
                    }
                    catch { }
                    try { mem = p.WorkingSet64; priv = p.PrivateMemorySize64; handles = (uint)p.HandleCount; } catch { }
                    try { path = p.MainModule?.FileName ?? ""; } catch { }
                }
                seen.Add((int)pid);
                res.Add(new { pid, ppid, name, path, cpu, mem, priv, threads, handles, ports = listening.GetValueOrDefault(pid), locked = IsProtected(pid, name) });
            }
            foreach (var k in PrevCpu.Keys.Where(k => !seen.Contains(k)).ToList()) PrevCpu.Remove(k);
            foreach (var p in live.Values) p.Dispose();
            var cpuTotal = Math.Min(100, res.Sum(x => (double)x.GetType().GetProperty("cpu")!.GetValue(x)!));
            var ms = Memory.Status();
            return new { procs = res, cpu = cpuTotal, memTotal = ms.total, memUsed = ms.total - ms.avail, cpus = ncpu, admin = IsAdmin(), self = Environment.ProcessId, time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        }
    }

    public sealed record PortRow(string proto, string local, int port, string remote, int rport, string state, uint pid, string name, bool locked);

    static readonly string[] TcpStates = { "", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB" };

    static byte[]? Table(bool tcp, int af, int cls)
    {
        var size = 4096;
        for (var i = 0; i < 4; i++)
        {
            var b = new byte[size];
            var r = tcp ? GetExtendedTcpTable(b, ref size, true, af, cls, 0) : GetExtendedUdpTable(b, ref size, true, af, cls, 0);
            if (r == 0) return b;
            if (r != 122) return null;
        }
        return null;
    }

    static int NetPort(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    public static List<PortRow> Ports(Dictionary<uint, string>? names)
    {
        names ??= new();
        var res = new List<PortRow>();
        void Add(string proto, IPAddress local, int port, IPAddress? remote, int rport, string state, uint pid)
        {
            var nm = names.GetValueOrDefault(pid, "");
            res.Add(new PortRow(proto, local.ToString(), port, remote?.ToString() ?? "", remote == null ? 0 : rport, state, pid, nm, IsProtected(pid, nm)));
        }
        if (Table(true, 2, 5) is { } t4)
            for (int i = 0, n = BitConverter.ToInt32(t4, 0); i < n && 4 + (i + 1) * 24 <= t4.Length; i++)
            { var o = 4 + i * 24; Add("TCP", new IPAddress(t4[(o + 4)..(o + 8)]), NetPort(t4, o + 8), new IPAddress(t4[(o + 12)..(o + 16)]), NetPort(t4, o + 16), TcpStates[Math.Clamp(BitConverter.ToInt32(t4, o), 0, 12)], BitConverter.ToUInt32(t4, o + 20)); }
        if (Table(true, 23, 5) is { } t6)
            for (int i = 0, n = BitConverter.ToInt32(t6, 0); i < n && 4 + (i + 1) * 56 <= t6.Length; i++)
            { var o = 4 + i * 56; Add("TCP6", new IPAddress(t6[o..(o + 16)]), NetPort(t6, o + 20), new IPAddress(t6[(o + 24)..(o + 40)]), NetPort(t6, o + 44), TcpStates[Math.Clamp(BitConverter.ToInt32(t6, o + 48), 0, 12)], BitConverter.ToUInt32(t6, o + 52)); }
        if (Table(false, 2, 1) is { } u4)
            for (int i = 0, n = BitConverter.ToInt32(u4, 0); i < n && 4 + (i + 1) * 12 <= u4.Length; i++)
            { var o = 4 + i * 12; Add("UDP", new IPAddress(u4[o..(o + 4)]), NetPort(u4, o + 4), null, 0, "", BitConverter.ToUInt32(u4, o + 8)); }
        if (Table(false, 23, 1) is { } u6)
            for (int i = 0, n = BitConverter.ToInt32(u6, 0); i < n && 4 + (i + 1) * 28 <= u6.Length; i++)
            { var o = 4 + i * 28; Add("UDP6", new IPAddress(u6[o..(o + 16)]), NetPort(u6, o + 20), null, 0, "", BitConverter.ToUInt32(u6, o + 24)); }
        return res.OrderBy(p => p.port).ThenBy(p => p.proto).ToList();
    }

    static object ProcKill(Req r)
    {
        var pids = r.Body.TryGetProperty("pids", out var a) ? a.EnumerateArray().Select(x => x.GetUInt32()).ToList() : new();
        var tree = r.BBool("tree");
        if (pids.Count == 0) throw new InvalidOperationException("pids required");
        var snap = Snapshot(); var names = snap.ToDictionary(p => p.pid, p => p.name);
        var done = new HashSet<uint>(); var res = new List<object>();
        foreach (var pid in pids)
        {
            var list = new List<uint> { pid };
            if (tree)
            {
                var kids = snap.Where(p => p.ppid != p.pid).GroupBy(p => p.ppid).ToDictionary(g => g.Key, g => g.Select(x => x.pid).ToList());
                list.Clear(); var seen = new HashSet<uint> { pid };
                void W(uint p) { foreach (var c in kids.GetValueOrDefault(p, new())) if (seen.Add(c)) W(c); list.Add(p); }
                W(pid);
            }
            foreach (var p in list)
            {
                if (!done.Add(p)) continue;
                var name = names.GetValueOrDefault(p, ""); string error = ""; var ok = false;
                if (IsProtected(p, name)) error = "protected system process or My Tool Box itself";
                else
                {
                    try { using var pr = Process.GetProcessById((int)p); pr.Kill(); ok = true; }
                    catch (ArgumentException) { error = $"process {p} is no longer running"; }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { error = "access denied (restart My Tool Box as administrator to end this process)"; }
                }
                res.Add(new { pid = p, name, ok, error });
            }
        }
        return res;
    }
}

public static class Memory
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemStatus { public uint Len, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalV, AvailV, Ext; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);

    public static (ulong total, ulong avail, ulong totalPage, ulong availPage, uint load) Status()
    {
        var m = new MemStatus { Len = (uint)Marshal.SizeOf<MemStatus>() };
        GlobalMemoryStatusEx(ref m);
        return (m.Total, m.Avail, m.TotalPage, m.AvailPage, m.Load);
    }
    public static ulong Available() => Status().avail;
}
