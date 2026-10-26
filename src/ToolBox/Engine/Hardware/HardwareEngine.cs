using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ToolBox.Engine;

/// <summary>Hardware details: WMI (processor, board, BIOS, memory, graphics, OS), CPUID and DXGI. Replaces hardware_windows.go, cpuinfo.go, cpuid_*.go and dxgi_windows.go.</summary>
public static unsafe class HardwareEngine
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    public static void Register()
    {
        Router.Map("/api/hw", r => Hw(r.S("fresh") == "1"));
        Router.Map("/api/hw/gpus", _ => new { adapters = Dxgi() });
    }

    /* ---------- WMI ---------- */

    static List<Dictionary<string, object?>> Cim(string cls, string[] props, string? where = null)
    {
        var res = new List<Dictionary<string, object?>>();
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}" + (where != null ? " WHERE " + where : ""));
            foreach (ManagementObject o in s.Get())
            {
                using var disposeMe = o;
                var d = new Dictionary<string, object?>();
                foreach (var p in props)
                {
                    object? v = null;
                    try { v = o[p]; } catch { }
                    d[p] = v is string[] a ? string.Join(",", a) : v is ushort[] u ? string.Join(",", u) : v;
                }
                res.Add(d);
            }
        }
        catch { }
        return res;
    }

    static string Date(object? v)
    {
        if (v is not string s || s.Length < 8) return "";
        try { return ManagementDateTimeConverter.ToDateTime(s).ToString("yyyy-MM-dd"); } catch { return s; }
    }

    static string DateTimeText(object? v)
    {
        if (v is not string s || s.Length < 8) return "";
        try { return ManagementDateTimeConverter.ToDateTime(s).ToString("yyyy-MM-dd HH:mm"); } catch { return s; }
    }

    static readonly Cached<object> WmiCache = new(ReadWmi, TimeSpan.FromMinutes(1));

    static object Hw(bool fresh)
    {
        if (fresh) WmiCache.Invalidate();
        var wmi = WmiCache.GetRequired(TimeSpan.FromSeconds(30));
        dynamic w = wmi;
        var ms = Memory.Status();
        var (cur, curMax, max) = LiveEngine.Clocks();
        string ident = w.ident, socketRaw = w.socket, board = w.boardProduct, ucodeHex = w.ucode;
        return new
        {
            wmi = w.data, cpuid = ReadCpu(ident),
            clock = new { current = cur, currentMax = curMax, max },
            socket = SocketText(socketRaw), chipset = Chipset(board), microcode = Microcode(ucodeHex),
            live = new { memTotal = ms.total, memUsed = ms.total - ms.avail, memLoad = ms.load, pageTotal = ms.totalPage, pageUsed = ms.totalPage - ms.availPage, goarch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), logical = Environment.ProcessorCount }
        };
    }

    static object ReadWmi()
    {
        var cpu = Cim("Win32_Processor", new[] { "Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "CurrentClockSpeed", "L2CacheSize", "L3CacheSize", "SocketDesignation", "Architecture", "ProcessorId", "Revision", "Description", "VirtualizationFirmwareEnabled" });
        var bb = Cim("Win32_BaseBoard", new[] { "Manufacturer", "Product", "Version", "SerialNumber" });
        var bios = Cim("Win32_BIOS", new[] { "Manufacturer", "SMBIOSBIOSVersion", "Version", "ReleaseDate" });
        foreach (var b in bios) b["ReleaseDate"] = Date(b["ReleaseDate"]);
        var cs = Cim("Win32_ComputerSystem", new[] { "Manufacturer", "Model", "SystemType", "TotalPhysicalMemory" });
        var mem = Cim("Win32_PhysicalMemory", new[] { "BankLabel", "DeviceLocator", "Capacity", "Speed", "ConfiguredClockSpeed", "Manufacturer", "PartNumber", "SerialNumber", "SMBIOSMemoryType", "FormFactor", "ConfiguredVoltage" });
        var gpu = Cim("Win32_VideoController", new[] { "Name", "AdapterRAM", "DriverVersion", "VideoProcessor", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate", "DriverDate" });
        foreach (var g in gpu) g["DriverDate"] = Date(g["DriverDate"]);
        var os = Cim("Win32_OperatingSystem", new[] { "Caption", "Version", "BuildNumber", "OSArchitecture", "InstallDate", "LastBootUpTime" });
        foreach (var o in os) { o["InstallDate"] = Date(o["InstallDate"]); o["LastBoot"] = DateTimeText(o["LastBootUpTime"]); o.Remove("LastBootUpTime"); }
        var npu = Cim("Win32_PnPEntity", new[] { "Name", "Manufacturer", "Status" }, "Name LIKE '%NPU%' OR Name LIKE '%AI Boost%' OR Name LIKE '%Neural%' OR Name LIKE '%Hexagon%' OR Name LIKE '%AMD IPU%' OR PNPClass='ComputeAccelerator'");

        var vram = new Dictionary<string, long>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var n in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!int.TryParse(n, out _)) continue;
                using var k = cls!.OpenSubKey(n);
                var desc = k?.GetValue("DriverDesc") as string; var size = k?.GetValue("HardwareInformation.qwMemorySize");
                if (desc != null && size != null) vram[desc] = Convert.ToInt64(size is byte[] b ? BitConverter.ToInt64(b.Concat(new byte[8]).Take(8).ToArray()) : size);
            }
        }
        catch { }

        string ident = "", ucode = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            ident = k?.GetValue("Identifier") as string ?? "";
            if (k?.GetValue("Update Revision") is byte[] u) ucode = string.Concat(u.Select(x => x.ToString("X2")));
        }
        catch { }

        var data = new { cpu, board = bb, bios, system = cs, memory = mem, gpu, npu, os, vram };
        return new { data, ident, ucode, socket = cpu.FirstOrDefault()?["SocketDesignation"] as string ?? "", boardProduct = bb.FirstOrDefault()?["Product"] as string ?? "" };
    }

    /* ---------- CPUID ---------- */

    static string RegStr(params uint[] regs) { var b = regs.SelectMany(BitConverter.GetBytes).ToArray(); return Encoding.ASCII.GetString(b).TrimEnd('\0', ' ').Trim(); }
    static bool Bit(uint v, int n) => (v >> n & 1) == 1;

    static readonly Dictionary<string, (string name, string tech)> Codenames = new()
    {
        ["AMD:23:1"] = ("Summit Ridge", "14 nm"), ["AMD:23:8"] = ("Pinnacle Ridge", "12 nm"), ["AMD:23:17"] = ("Raven Ridge", "14 nm"), ["AMD:23:24"] = ("Picasso", "12 nm"),
        ["AMD:23:49"] = ("Rome", "7 nm"), ["AMD:23:96"] = ("Renoir", "7 nm"), ["AMD:23:113"] = ("Matisse", "7 nm"), ["AMD:23:104"] = ("Lucienne", "7 nm"),
        ["AMD:25:1"] = ("Milan", "7 nm"), ["AMD:25:33"] = ("Vermeer", "7 nm"), ["AMD:25:80"] = ("Cezanne", "7 nm"), ["AMD:25:68"] = ("Rembrandt", "6 nm"),
        ["AMD:25:97"] = ("Raphael", "5 nm"), ["AMD:25:116"] = ("Phoenix", "4 nm"), ["AMD:25:17"] = ("Genoa", "5 nm"), ["AMD:26:68"] = ("Granite Ridge", "4 nm"),
        ["Intel:6:42"] = ("Sandy Bridge", "32 nm"), ["Intel:6:58"] = ("Ivy Bridge", "22 nm"), ["Intel:6:60"] = ("Haswell", "22 nm"), ["Intel:6:69"] = ("Haswell ULT", "22 nm"),
        ["Intel:6:61"] = ("Broadwell", "14 nm"), ["Intel:6:78"] = ("Skylake-U", "14 nm"), ["Intel:6:94"] = ("Skylake", "14 nm"), ["Intel:6:142"] = ("Kaby Lake", "14 nm"),
        ["Intel:6:158"] = ("Coffee Lake", "14 nm"), ["Intel:6:165"] = ("Comet Lake", "14 nm"), ["Intel:6:167"] = ("Rocket Lake", "14 nm"), ["Intel:6:126"] = ("Ice Lake", "10 nm"),
        ["Intel:6:140"] = ("Tiger Lake", "10 nm"), ["Intel:6:141"] = ("Tiger Lake-H", "10 nm"), ["Intel:6:151"] = ("Alder Lake", "Intel 7"), ["Intel:6:154"] = ("Alder Lake-P", "Intel 7"),
        ["Intel:6:183"] = ("Raptor Lake", "Intel 7"), ["Intel:6:191"] = ("Raptor Lake", "Intel 7"), ["Intel:6:170"] = ("Meteor Lake", "Intel 4"), ["Intel:6:198"] = ("Arrow Lake", "3 nm")
    };

    static object ReadCpu(string identifier)
    {
        var logical = Environment.ProcessorCount;
        if (!X86Base.IsSupported) return new { supported = false, logical, caches = new List<object>() };
        var (maxLeaf, vb, vc, vd) = Id(0, 0);
        var vendor = RegStr(vb, vd, vc);
        var (a1, _, c1, d1) = Id(1, 0);
        var stepping = (int)(a1 & 0xf);
        var sm = Regex.Match(identifier, @"Stepping (\d+)");
        if (sm.Success) stepping = int.Parse(sm.Groups[1].Value);
        int fam = (int)(a1 >> 8 & 0xf), mod = (int)(a1 >> 4 & 0xf), extMod = (int)(a1 >> 16 & 0xf), extFam = (int)(a1 >> 20 & 0xff);
        int famDec = fam == 0xf ? fam + extFam : fam, modDec = fam is 0xf or 0x6 ? extMod << 4 | mod : mod;
        uint l7b = 0;
        if (maxLeaf >= 7) l7b = Id(7, 0).b;
        var maxExt = Id(0x80000000, 0).a;
        uint e1c = 0, e1d = 0;
        if (maxExt >= 0x80000001) { var e = Id(0x80000001, 0); e1c = e.c; e1d = e.d; }
        var instr = InstructionList(vendor, c1, d1, l7b, e1c, e1d, Bit(c1, 27));
        var brand = "";
        if (maxExt >= 0x80000004)
        {
            var b = new List<byte>();
            for (uint l = 0x80000002; l <= 0x80000004; l++) { var r = Id(l, 0); foreach (var x in new[] { r.a, r.b, r.c, r.d }) b.AddRange(BitConverter.GetBytes(x)); }
            brand = string.Join(" ", Encoding.ASCII.GetString(b.ToArray()).TrimEnd('\0').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        var key = $"{(vendor.Contains("Intel") ? "Intel" : "AMD")}:{famDec}:{modDec}";
        var (code, tech) = Codenames.GetValueOrDefault(key);
        var leaf = vendor.Contains("AMD") && maxExt >= 0x8000001D ? 0x8000001Du : 4u;
        var caches = new List<object>();
        if (leaf == 0x8000001D || maxLeaf >= 4)
            for (uint i = 0; i < 16; i++)
            {
                var (a, b, c, _) = Id(leaf, i);
                uint typ = a & 0x1f, level = a >> 5 & 7;
                if (typ == 0) break;
                int sharing = (int)(a >> 14 & 0xfff) + 1, line = (int)(b & 0xfff) + 1, parts = (int)(b >> 12 & 0x3ff) + 1, ways = (int)(b >> 22) + 1, sets = (int)c + 1;
                var name = level == 1 && typ == 1 ? "L1 Data" : level == 1 && typ == 2 ? "L1 Inst." : level == 2 ? "Level 2" : level == 3 ? "Level 3" : $"Level {level}";
                caches.Add(new { name, size = ways * parts * line * sets, count = Math.Max(1, (logical + sharing - 1) / sharing), ways, line });
            }
        return new { vendor, brand, family = fam.ToString("X"), model = mod.ToString("X"), extFamily = famDec.ToString("X"), extModel = modDec.ToString("X"), stepping, familyDec = famDec, modelDec = modDec, codename = code ?? "", tech = tech ?? "", instructions = instr, caches, logical, supported = true };
    }

    static (uint a, uint b, uint c, uint d) Id(uint leaf, uint sub)
    {
        var (a, b, c, d) = X86Base.CpuId((int)leaf, (int)sub);
        return ((uint)a, (uint)b, (uint)c, (uint)d);
    }

    static string InstructionList(string vendor, uint l1c, uint l1d, uint l7b, uint e1c, uint e1d, bool avxOk)
    {
        var parts = new List<string>();
        void Add(bool ok, string s) { if (ok) parts.Add(s); }
        Add(Bit(l1d, 23), "MMX");
        var sse = new List<string>();
        foreach (var (ok, s) in new[] { (Bit(l1d, 25), "1"), (Bit(l1d, 26), "2"), (Bit(l1c, 0), "3"), (Bit(l1c, 19), "4.1"), (Bit(l1c, 20), "4.2"), (Bit(e1c, 6), "4A") }) if (ok) sse.Add(s);
        if (sse.Count > 0) parts.Add("SSE (" + string.Join(", ", sse) + ")");
        Add(Bit(l1c, 9), "SSSE3"); Add(Bit(e1d, 29), "x86-64");
        Add(vendor.Contains("AMD") && Bit(e1c, 2), "AMD-V"); Add(vendor.Contains("Intel") && Bit(l1c, 5), "VT-x");
        Add(Bit(l1c, 25), "AES"); Add(avxOk && Bit(l1c, 28), "AVX"); Add(avxOk && Bit(l7b, 5), "AVX2"); Add(avxOk && Bit(l7b, 16), "AVX-512");
        Add(Bit(l1c, 12), "FMA3"); Add(Bit(l7b, 3), "BMI1"); Add(Bit(l7b, 8), "BMI2"); Add(Bit(l1c, 23), "POPCNT"); Add(Bit(l7b, 29), "SHA"); Add(Bit(l1c, 30), "RDRAND"); Add(Bit(l7b, 18), "RDSEED");
        return string.Join(", ", parts);
    }

    static readonly Dictionary<string, int> SocketPins = new() { ["AM4"] = 1331, ["AM5"] = 1718, ["AM3+"] = 942, ["AM3"] = 941, ["TR4"] = 4094, ["sTRX4"] = 4094, ["SP3"] = 4094, ["SP5"] = 6096, ["FM2+"] = 906, ["FM2"] = 904, ["FM1"] = 905 };

    static string SocketText(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return "";
        if (SocketPins.TryGetValue(s, out var n)) return $"Socket {s} ({n})";
        var m = Regex.Match(s, @"(\d{3,4})");
        if (m.Success && (s.StartsWith("LGA", StringComparison.OrdinalIgnoreCase) || s.StartsWith("SOCKET", StringComparison.OrdinalIgnoreCase))) return $"{s} ({m.Value})";
        return s;
    }

    static string Chipset(string model)
    {
        var m = Regex.Match(model.ToUpperInvariant(), @"\b([ABXZHQWC]\d{2,3}[A-Z]?|TRX40|WRX80|X670E?)\b");
        return m.Success ? m.Groups[1].Value.TrimEnd('M', 'E', 'I', 'D', 'F', 'H') : "";
    }

    static string Microcode(string hex)
    {
        if (hex.Length != 8 && hex.Length != 16) return "";
        var h = hex.Length == 16 ? hex[8..] : hex;
        var b = Convert.FromHexString(h);
        return "0x" + BitConverter.ToUInt32(b, 0).ToString("X");
    }

    /* ---------- DXGI ---------- */

    static readonly Dictionary<uint, string> Vendors = new() { [0x10DE] = "NVIDIA", [0x1002] = "AMD", [0x1022] = "AMD", [0x8086] = "Intel", [0x5143] = "Qualcomm", [0x1414] = "Microsoft", [0x1AE0] = "Google", [0x15AD] = "VMware", [0x1B36] = "Red Hat" };
    static readonly Regex NpuName = new(@"\bNPU\b|AI Boost|Neural|Hexagon|\bIPU\b|Ryzen AI|Movidius|VPU", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static List<object> Dxgi()
    {
        var res = new List<object>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) != 0 || factory == IntPtr.Zero) return res;
        try
        {
            var p = IntPtr.Size;
            for (var i = 0; i < 16; i++)
            {
                IntPtr ad;
                var enumFn = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(factory, 12);
                if (enumFn(factory, (uint)i, &ad) != 0 || ad == IntPtr.Zero) break;
                try
                {
                    var desc = new byte[320];
                    int hr;
                    fixed (byte* d = desc) hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)VTable(ad, 10))(ad, d);
                    if (hr != 0) continue;
                    var name = Encoding.Unicode.GetString(desc, 0, 256); name = name[..(name.IndexOf('\0') is var z && z >= 0 ? z : name.Length)];
                    uint vid = BitConverter.ToUInt32(desc, 256), did = BitConverter.ToUInt32(desc, 260);
                    var bse = 272;
                    ulong Rd(int o) => p == 8 ? BitConverter.ToUInt64(desc, o) : BitConverter.ToUInt32(desc, o);
                    ulong ded = Rd(bse), shared = Rd(bse + 2 * p);
                    var luidAt = bse + 3 * p;
                    var luid = $"0x{BitConverter.ToUInt32(desc, luidAt + 4):X8}_0x{BitConverter.ToUInt32(desc, luidAt):X8}";
                    var flags = BitConverter.ToUInt32(desc, luidAt + 8);
                    var kind = (flags & 2) != 0 || vid == 0x1414 ? "software" : NpuName.IsMatch(name) ? "npu"
                        : ded < 512UL << 20 && shared > 0 && vid is 0x8086 or 0x1002 or 0x1022 or 0x5143 ? "igpu" : "gpu";
                    res.Add(new { name, vendor = Vendors.GetValueOrDefault(vid, ""), vendorId = vid, deviceId = did, dedicated = ded, shared, luid, kind });
                }
                finally { ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(ad, 2))(ad); }
            }
        }
        finally { ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(factory, 2))(factory); }
        return res;
    }

    static IntPtr VTable(IntPtr obj, int slot) => ((IntPtr*)*(IntPtr*)obj)[slot];
}
