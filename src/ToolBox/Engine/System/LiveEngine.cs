using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ToolBox.Engine;

/// <summary>Live numbers: CPU load and clocks, GPU / network counters (PDH), temperatures. Replaces pdh_windows.go, temps_windows.go and the live part of hardware_windows.go.</summary>
public static class LiveEngine
{
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
    [DllImport("powrprof.dll")] static extern int CallNtPowerInformation(int level, IntPtr inBuf, int inLen, IntPtr outBuf, int outLen);
    [DllImport("kernel32.dll")] static extern bool K32GetPerformanceInfo(ref PerfInfo p, int cb);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQueryW(string? src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, ref uint count, IntPtr buf);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);

    [StructLayout(LayoutKind.Sequential)]
    struct PerfInfo { public uint Cb; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysTotal, PhysAvail, SysCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize; public uint Handles, Processes, Threads; }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcPerf { public long Idle, Kernel, User, Dpc, Interrupt; public uint IntCount, Pad; }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerInfo { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdle, CurIdle; }

    [StructLayout(LayoutKind.Sequential)]
    struct PdhItem { public IntPtr Name; public uint Status; public double Value; }

    public static void Register()
    {
        Router.Map("/api/hw/cpu", _ => Cpu());
        Router.Map("/api/hw/perf", _ => Perf());
        Router.Map("/api/hw/temps", _ => new { temps = Temps() });
    }

    /* ---------- PDH ---------- */

    static List<Dictionary<string, double>> Pdh(string[] paths, int gapMs)
    {
        var res = paths.Select(_ => new Dictionary<string, double>()).ToList();
        if (PdhOpenQueryW(null, IntPtr.Zero, out var q) != 0) return res;
        try
        {
            var cs = new IntPtr[paths.Length];
            for (var i = 0; i < paths.Length; i++) if (PdhAddEnglishCounterW(q, paths[i], IntPtr.Zero, out cs[i]) != 0) cs[i] = IntPtr.Zero;
            PdhCollectQueryData(q); Thread.Sleep(gapMs); PdhCollectQueryData(q);
            for (var i = 0; i < cs.Length; i++)
            {
                if (cs[i] == IntPtr.Zero) continue;
                uint size = 0, count = 0;
                if ((uint)PdhGetFormattedCounterArrayW(cs[i], 0x200, ref size, ref count, IntPtr.Zero) != 0x800007D2 || size == 0) continue;
                var buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArrayW(cs[i], 0x200, ref size, ref count, buf) != 0) continue;
                    var sz = Marshal.SizeOf<PdhItem>();
                    for (var k = 0; k < count; k++)
                    {
                        var it = Marshal.PtrToStructure<PdhItem>(buf + k * sz);
                        var name = Marshal.PtrToStringUni(it.Name) ?? "";
                        res[i][name] = res[i].GetValueOrDefault(name) + it.Value;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { PdhCloseQuery(q); }
        return res;
    }

    /* ---------- CPU ---------- */

    static readonly object CpuLock = new();
    static (long idle, long busy)[]? _prev;

    static List<double> CoreLoad()
    {
        var n = Environment.ProcessorCount;
        var sz = Marshal.SizeOf<ProcPerf>();
        var buf = Marshal.AllocHGlobal(n * sz);
        try
        {
            if (NtQuerySystemInformation(8, buf, n * sz, out var got) != 0) return new();
            n = got / sz;
            var cur = new (long idle, long busy)[n]; var load = new List<double>();
            for (var i = 0; i < n; i++) { var p = Marshal.PtrToStructure<ProcPerf>(buf + i * sz); cur[i] = (p.Idle, p.Kernel + p.User); }
            lock (CpuLock)
            {
                for (var i = 0; i < n; i++)
                {
                    double v = 0;
                    if (_prev?.Length == n) { var dt = cur[i].busy - _prev[i].busy; var di = cur[i].idle - _prev[i].idle; if (dt > 0) v = Math.Clamp((1 - (double)di / dt) * 100, 0, 100); }
                    load.Add(v);
                }
                _prev = cur;
            }
            return load;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static (uint cur, uint curMax, uint max) Clocks()
    {
        var n = Environment.ProcessorCount; var sz = Marshal.SizeOf<PowerInfo>();
        var buf = Marshal.AllocHGlobal(n * sz);
        try
        {
            if (CallNtPowerInformation(11, IntPtr.Zero, 0, buf, n * sz) != 0) return (0, 0, 0);
            uint cur = 0, curMax = 0, max = 0;
            for (var i = 0; i < n; i++) { var p = Marshal.PtrToStructure<PowerInfo>(buf + i * sz); if (i == 0) cur = p.CurrentMhz; curMax = Math.Max(curMax, p.CurrentMhz); max = Math.Max(max, p.MaxMhz); }
            return (cur, curMax, max);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static object Cpu()
    {
        var cores = CoreLoad();
        var total = cores.Count > 0 ? cores.Average() : 0;
        var pi = new PerfInfo { Cb = (uint)Marshal.SizeOf<PerfInfo>() };
        K32GetPerformanceInfo(ref pi, (int)pi.Cb);
        var (cur, curMax, max) = Clocks(); var m = Memory.Status();
        return new
        {
            memTotal = m.total, memUsed = m.total - m.avail, commitUsed = m.totalPage - m.availPage, commitTotal = m.totalPage, cores, total, speed = curMax, core0 = cur, max,
            processes = pi.Processes, threads = pi.Threads, handles = pi.Handles, uptime = (ulong)Environment.TickCount64 / 1000
        };
    }

    /* ---------- GPU and network counters ---------- */

    static readonly Regex Luid = new(@"luid_(0x[0-9a-fA-F]+_0x[0-9a-fA-F]+)", RegexOptions.Compiled);
    static readonly Regex EngType = new(@"engtype_([A-Za-z0-9_ ]+)$", RegexOptions.Compiled);
    static readonly Cached<object> PerfCache = new(ReadPerf, TimeSpan.FromMilliseconds(700));

    static string Canon(string s) => string.Join("_", s.ToUpperInvariant().Split('_').Select(p => "0x" + p.Replace("0X", "")));

    static object Perf() => PerfCache.GetRequired(TimeSpan.FromSeconds(5));

    static object ReadPerf()
    {
        {
            var s = Pdh(new[]
            {
                @"\GPU Engine(*)\Utilization Percentage", @"\GPU Adapter Memory(*)\Dedicated Usage", @"\GPU Adapter Memory(*)\Shared Usage",
                @"\Network Interface(*)\Bytes Received/sec", @"\Network Interface(*)\Bytes Sent/sec", @"\Network Interface(*)\Current Bandwidth"
            }, 250);
            var gpus = new Dictionary<string, (Dictionary<string, double> eng, double ded, double shared)>();
            (Dictionary<string, double> eng, double ded, double shared) Get(string l) { if (!gpus.TryGetValue(l, out var g)) gpus[l] = g = (new(), 0, 0); return g; }
            foreach (var (name, v) in s[0]) { var m = Luid.Match(name); if (!m.Success) continue; var g = Get(Canon(m.Groups[1].Value)); var e = EngType.Match(name); var et = e.Success ? e.Groups[1].Value : "Other"; g.eng[et] = g.eng.GetValueOrDefault(et) + v; }
            foreach (var (name, v) in s[1]) { var m = Luid.Match(name); if (m.Success) { var l = Canon(m.Groups[1].Value); var g = Get(l); gpus[l] = (g.eng, g.ded + v, g.shared); } }
            foreach (var (name, v) in s[2]) { var m = Luid.Match(name); if (m.Success) { var l = Canon(m.Groups[1].Value); var g = Get(l); gpus[l] = (g.eng, g.ded, g.shared + v); } }
            var gpuOut = gpus.Select(kv =>
            {
                var eng = kv.Value.eng.ToDictionary(e => e.Key, e => Math.Min(100, e.Value));
                return new { luid = kv.Key, util = eng.Count > 0 ? eng.Values.Max() : 0, engines = eng, dedicated = kv.Value.ded, shared = kv.Value.shared };
            }).ToList();
            var net = s[3].Select(kv => new { name = kv.Key, rx = kv.Value, tx = s[4].GetValueOrDefault(kv.Key), bps = s[5].GetValueOrDefault(kv.Key) }).ToList();
            return new { gpu = gpuOut, net };
        }
    }

    /* ---------- temperatures ---------- */

    sealed record Temp(string group, string name, double c, double warn, double crit);

    static readonly Cached<List<Temp>> TempCache = new(ReadTemps, TimeSpan.FromMilliseconds(1500));
    static readonly Cached<List<Temp>> DiskTempCache = new(DiskTemps, TimeSpan.FromSeconds(15));      // opening drives can block, so it refreshes on its own
    static readonly Cached<List<Temp>> NvidiaCache = new(NvidiaTemps, TimeSpan.FromSeconds(5));

    static List<object> Temps() =>
        (TempCache.Get(TimeSpan.FromSeconds(4)) ?? new List<Temp>()).Select(t => (object)new { group = t.group, name = t.name, c = t.c, warn = t.warn, crit = t.crit }).ToList();

    static List<Temp> ReadTemps()
    {
        var list = new List<Temp>();
        var s = Pdh(new[] { @"\Thermal Zone Information(*)\Temperature" }, 50);
        foreach (var kv in s[0].OrderBy(k => k.Key))
        {
            var c = kv.Value - 273.15;
            if (c is > -50 and < 200) list.Add(new Temp("Thermal zone", kv.Key.Replace(@"\_TZ.", "").TrimStart('\\'), c, 0, 0));
        }
        list.AddRange(DiskTempCache.Get(TimeSpan.FromSeconds(2)) ?? new List<Temp>());
        list.AddRange(NvidiaCache.Get(TimeSpan.FromSeconds(2)) ?? new List<Temp>());
        return list;
    }

    static List<Temp> NvidiaTemps()
    {
        var res = new List<Temp>();
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=name,temperature.gpu --format=csv,noheader,nounits") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            var task = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000)) { try { p.Kill(true); } catch { } return res; }
            foreach (var line in task.Result.Split('\n'))
            {
                var f = line.Trim().Split(',');
                if (f.Length >= 2 && double.TryParse(f[1].Trim(), out var t)) res.Add(new Temp("GPU", f[0].Trim(), t, 0, 0));
            }
        }
        catch { }
        return res;
    }

    static byte[]? Query(SafeFileHandle h, uint propId)
    {
        var q = new byte[12]; BitConverter.GetBytes(propId).CopyTo(q, 0); // PropertyId, QueryType = standard (0)
        var o = new byte[1024];
        return DeviceIoControl(h, 0x2D1400, q, q.Length, o, o.Length, out var ret, IntPtr.Zero) ? o[..ret] : null;
    }

    static string Str(byte[] b, int off) { if (off <= 0 || off >= b.Length) return ""; var e = Array.IndexOf(b, (byte)0, off); return Encoding.ASCII.GetString(b, off, (e < 0 ? b.Length : e) - off).Trim(); }

    static List<Temp> DiskTemps()
    {
        var res = new List<Temp>();
        for (var i = 0; i < 32; i++)
        {
            using var h = CreateFileW($@"\\.\PhysicalDrive{i}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var temp = Query(h, 52);
            if (temp is not { Length: >= 44 } || BitConverter.ToUInt16(temp, 12) == 0) continue;
            var t = BitConverter.ToInt16(temp, 26);
            if (t is <= -50 or >= 200 or 0) continue;
            var d = Query(h, 0); var name = d == null ? $"Disk {i}" : $"{Str(d, (int)BitConverter.ToUInt32(d, 12))} {Str(d, (int)BitConverter.ToUInt32(d, 16))}".Trim();
            res.Add(new Temp("Disk", name, t, BitConverter.ToInt16(temp, 10), BitConverter.ToInt16(temp, 8)));
        }
        return res;
    }
}
