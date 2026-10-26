using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ToolBox.Engine;

/// <summary>Disk health in the style of CrystalDiskInfo: descriptor, layout, temperature, ATA SMART and the NVMe health log, read with DeviceIoControl.
/// Replaces diskinfo_windows.go and diskmeta_windows.go.</summary>
public static class DiskEngine
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[]? inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);
    [DllImport("kernel32.dll")] static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint QueryDosDevice(string name, StringBuilder target, uint max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetVolumeInformation(string root, StringBuilder label, int labelLen, IntPtr a, IntPtr b, IntPtr c, StringBuilder fs, int fsLen);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetDiskFreeSpaceEx(string dir, out ulong free, out ulong total, out ulong totalFree);

    const uint IoctlStorageQuery = 0x2D1400, IoctlGeometryEx = 0x700A0, IoctlLayoutEx = 0x70050, IoctlVolumeExtents = 0x560000, SmartRcv = 0x7C088, SmartVersion = 0x74080;

    public sealed class Attr { public int id { get; set; } public string name { get; set; } = ""; public int cur { get; set; } public int worst { get; set; } public int thr { get; set; } public string raw { get; set; } = ""; public string rawHex { get; set; } = ""; public string status { get; set; } = "ok"; }
    public sealed class Part { public int num { get; set; } public long offset { get; set; } public long size { get; set; } public string type { get; set; } = ""; public string name { get; set; } = ""; public string letter { get; set; } = ""; public string label { get; set; } = ""; public string fs { get; set; } = ""; public long free { get; set; } }

    public sealed class Disk
    {
        public int index { get; set; } public string model { get; set; } = ""; public string vendor { get; set; } = ""; public string serial { get; set; } = ""; public string firmware { get; set; } = "";
        public string bus { get; set; } = ""; public string media { get; set; } = ""; public bool removable { get; set; } public long size { get; set; } public string style { get; set; } = "";
        public List<Part> parts { get; set; } = new(); public List<string> letters { get; set; } = new(); public string source { get; set; } = ""; public List<Attr> attrs { get; set; } = new();
        public string health { get; set; } = "Unknown"; public int healthPct { get; set; } public List<string> reasons { get; set; } = new();
        public int temp { get; set; } public int tempWarn { get; set; } public int tempCrit { get; set; } public long powerHours { get; set; } public long powerCount { get; set; }
        public long hostReads { get; set; } public long hostWrites { get; set; } public bool needAdmin { get; set; } public List<string> notes { get; set; } = new();
        public string maker { get; set; } = ""; public string controller { get; set; } = ""; public string controllerVendor { get; set; } = ""; public string dram { get; set; } = "n/a"; public string dramNote { get; set; } = "";
        public long hmbBytes { get; set; } public int pciVendor { get; set; } public int subVendor { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public bool HasIdentify { get; set; }
    }

    public static void Register() => Router.Map("/api/disks", _ => Disks());

    static readonly Dictionary<uint, string> BusNames = new() { [1] = "SCSI", [2] = "ATAPI", [3] = "ATA", [4] = "IEEE 1394", [5] = "SSA", [6] = "Fibre Channel", [7] = "USB", [8] = "RAID", [9] = "iSCSI", [10] = "SAS", [11] = "SATA", [12] = "SD", [13] = "MMC", [14] = "Virtual", [15] = "File-backed virtual", [16] = "Storage Spaces", [17] = "NVMe", [18] = "SCM", [19] = "UFS" };

    static SafeFileHandle Open(string path, uint access) => CreateFileW(path, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

    static byte[]? Ioctl(SafeFileHandle h, uint code, byte[]? input, int outSize)
    {
        var o = new byte[outSize];
        return DeviceIoControl(h, code, input, input?.Length ?? 0, o, o.Length, out var n, IntPtr.Zero) ? o[..n] : null;
    }

    static byte[]? QueryProp(SafeFileHandle h, uint id, int outSize, byte[]? extra = null)
    {
        var inp = new byte[8 + (extra?.Length ?? 0) + 4];
        BitConverter.GetBytes(id).CopyTo(inp, 0);
        extra?.CopyTo(inp, 8);
        return Ioctl(h, IoctlStorageQuery, inp, outSize);
    }

    static string CStr(byte[] b, uint off)
    {
        if (off == 0 || off >= b.Length) return "";
        var e = (int)off; while (e < b.Length && b[e] != 0) e++;
        return Encoding.ASCII.GetString(b, (int)off, e - (int)off).Trim();
    }

    static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    static ulong U64(byte[] b, int o) => BitConverter.ToUInt64(b, o);

    /* ---------- listing ---------- */

    static readonly HashSet<int> Busy = new();
    static readonly object BusyLock = new();

    static object Disks()
    {
        var t0 = DateTime.UtcNow; var deadline = t0.AddSeconds(6);
        var tasks = new Dictionary<int, Task<Disk?>>(); var stuck = new HashSet<int>();
        for (var i = 0; i < 32; i++)
        {
            lock (BusyLock) { if (!Busy.Add(i)) { stuck.Add(i); continue; } }
            var idx = i;
            tasks[i] = Task.Factory.StartNew(() => { try { return ReadOne(idx); } finally { lock (BusyLock) Busy.Remove(idx); lock (OpenedIndexes) OpenedIndexes.Remove(idx); } }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        Task.WaitAll(tasks.Values.ToArray<Task>(), Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
        var disks = new List<Disk>();
        for (var i = 0; i < 32; i++)
        {
            if (tasks.TryGetValue(i, out var t) && t.IsCompletedSuccessfully) { if (t.Result != null) disks.Add(t.Result); }
            else if (stuck.Contains(i) || (t != null && !t.IsCompleted && IsOpened(i))) disks.Add(Unresponsive(i));
        }
        MapLetters(disks, deadline);
        var t1 = DateTime.UtcNow;
        if (disks.Any(d => d.source == "")) ApplyStorageApi(disks, DateTime.UtcNow.AddSeconds(4));
        foreach (var d in disks) Classify(d);
        return new { admin = SysEngine.IsAdmin(), disks, time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ms = new { drives = (long)(t1 - t0).TotalMilliseconds, health = (long)(DateTime.UtcNow - t1).TotalMilliseconds } };
    }

    static readonly HashSet<int> OpenedIndexes = new();
    static bool IsOpened(int i) { lock (OpenedIndexes) return OpenedIndexes.Contains(i); }

    static Disk Unresponsive(int i)
    {
        var d = new Disk { index = i };
        d.notes.Add("Windows did not answer for this drive within a few seconds. It may be asleep, failing or behind a stalled USB adapter. Press Refresh to try again.");
        return d;
    }

    static Disk? ReadOne(int i)
    {
        using (var h = Open($@"\\.\PhysicalDrive{i}", 0))
        {
            if (h.IsInvalid) return null;
            lock (OpenedIndexes) OpenedIndexes.Add(i);
            var d = new Disk { index = i };
            ReadDescriptor(h, d);
            var g = Ioctl(h, IoctlGeometryEx, null, 256);
            if (g is { Length: >= 32 }) d.size = (long)U64(g, 24);
            ReadLayout(h, d);
            ReadTemperature(h, d);
            if (d.bus == "NVMe") ReadNvmeIdentify(h, d);
            ReadSmartLater(d);
            return d;
        }
    }

    static void ReadSmartLater(Disk d) => ReadSmart(d);

    static void ReadDescriptor(SafeFileHandle h, Disk d)
    {
        var b = QueryProp(h, 0, 1024);
        if (b is not { Length: >= 36 }) return;
        d.removable = b[10] != 0;
        d.vendor = CStr(b, U32(b, 12)); d.model = CStr(b, U32(b, 16)); d.firmware = CStr(b, U32(b, 20)); d.serial = CStr(b, U32(b, 24)).TrimEnd('.', ' ');
        if (d.vendor != "" && !d.model.StartsWith(d.vendor) && d.vendor != "ATA" && d.vendor != "NVMe") d.model = d.vendor + " " + d.model;
        d.bus = BusNames.GetValueOrDefault(U32(b, 28), $"Type {U32(b, 28)}");
        var sp = QueryProp(h, 7, 64);
        if (sp is { Length: >= 9 }) d.media = sp[8] != 0 ? "HDD" : "SSD";
    }

    static readonly Dictionary<string, string> GptTypes = new()
    {
        ["C12A7328-F81F-11D2-BA4B-00A0C93EC93B"] = "EFI system", ["E3C9E316-0B5C-4DB8-817D-F92DF00215AE"] = "Microsoft reserved", ["EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"] = "Basic data",
        ["DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"] = "Recovery", ["5808C8AA-7E8F-42E0-85D2-E1E90434CFB3"] = "LDM metadata", ["AF9B60A0-1431-4F62-BC68-3311714A69AD"] = "LDM data",
        ["E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D"] = "Storage Spaces", ["0FC63DAF-8483-4772-8E79-3D69D8477DE4"] = "Linux filesystem", ["0657FD6D-A4AB-43C4-84E5-0933C84B4F4F"] = "Linux swap"
    };

    static void ReadLayout(SafeFileHandle h, Disk d)
    {
        var b = Ioctl(h, IoctlLayoutEx, null, 48 + 144 * 128);
        if (b is not { Length: >= 48 }) return;
        var style = U32(b, 0); var count = (int)U32(b, 4);
        d.style = style switch { 0 => "MBR", 1 => "GPT", 2 => "RAW", _ => "" };
        for (var i = 0; i < count && 48 + (i + 1) * 144 <= b.Length; i++)
        {
            var o = 48 + i * 144;
            var pd = new Part { num = (int)U32(b, o + 24), offset = (long)U64(b, o + 8), size = (long)U64(b, o + 16) };
            if (pd.size == 0) continue;
            if (U32(b, o) == 1)
            {
                var g = new Guid(b[(o + 32)..(o + 48)]).ToString().ToUpperInvariant();
                pd.type = GptTypes.GetValueOrDefault(g, "GPT " + g);
                pd.name = Encoding.Unicode.GetString(b, o + 72, 72).Split('\0')[0];
            }
            else
            {
                var t = b[o + 32];
                if (t == 0) continue;
                pd.type = t switch { 7 => "NTFS / exFAT", 0x0B => "FAT32", 0x0C => "FAT32 (LBA)", 0x27 => "Recovery", 0x05 => "Extended", 0x0F => "Extended (LBA)", 0x83 => "Linux", 0xEE => "GPT protective", _ => $"Type 0x{t:X2}" };
            }
            d.parts.Add(pd);
        }
        d.parts = d.parts.OrderBy(p => p.offset).ToList();
    }

    static void ReadTemperature(SafeFileHandle h, Disk d)
    {
        var b = QueryProp(h, 52, 512);
        if (b is not { Length: >= 24 }) return;
        d.tempCrit = BitConverter.ToInt16(b, 8); d.tempWarn = BitConverter.ToInt16(b, 10);
        if (U16(b, 12) > 0 && b.Length >= 44)
        {
            var t = BitConverter.ToInt16(b, 26);
            if (t is > -50 and < 200 and not 0) d.temp = t;
        }
    }

    /* ---------- drive letters ---------- */

    sealed record VolHit(string Letter, string Label, string Fs, long Free, int Disk, long Offset);

    static void MapLetters(List<Disk> disks, DateTime deadline)
    {
        var mask = GetLogicalDrives(); var letters = new List<string>();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            var letter = (char)('A' + i) + ":";
            var t = GetDriveType(letter + @"\");
            if (t is not (2 or 3)) continue;
            var target = new StringBuilder(512);
            if (QueryDosDevice(letter, target, 512) > 0 && target.ToString().StartsWith(@"\??\")) continue;
            letters.Add(letter);
        }
        var probes = letters.Select(l => Task.Factory.StartNew(() => Probe(l), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(probes.ToArray<Task>(), Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
        foreach (var p in probes.Where(p => p.IsCompletedSuccessfully))
            foreach (var v in p.Result)
            {
                var d = disks.FirstOrDefault(x => x.index == v.Disk);
                var part = d?.parts.FirstOrDefault(x => x.offset == v.Offset);
                if (d == null || part == null) continue;
                d.letters.Add(v.Letter); part.letter = v.Letter; part.label = v.Label; part.fs = v.Fs; part.free = v.Free;
            }
        foreach (var d in disks) d.letters.Sort(StringComparer.Ordinal);
    }

    static List<VolHit> Probe(string letter)
    {
        var res = new List<VolHit>();
        using var h = Open(@"\\.\" + letter, 0);
        if (h.IsInvalid) return res;
        var b = Ioctl(h, IoctlVolumeExtents, null, 8 + 24 * 8);
        if (b is not { Length: >= 32 }) return res;
        var label = new StringBuilder(261); var fs = new StringBuilder(261);
        GetVolumeInformation(letter + @"\", label, 261, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fs, 261);
        GetDiskFreeSpaceEx(letter + @"\", out var free, out _, out _);
        for (var e = 0; e < (int)U32(b, 0) && 8 + (e + 1) * 24 <= b.Length; e++)
            res.Add(new VolHit(letter, label.ToString(), fs.ToString(), (long)free, (int)U32(b, 8 + e * 24), (long)U64(b, 16 + e * 24)));
        return res;
    }

    /* ---------- storage health through WMI (works without admin) ---------- */

    sealed record StorageInfo(int Media, int Health);

    // The WMI query to the storage provider can block indefinitely on a misbehaving disk, so it runs on its own
    // task with a deadline. A query still blocked from an earlier refresh is reused, not started again.
    static Task<Dictionary<int, StorageInfo>>? storageQuery;

    static void ApplyStorageApi(List<Disk> disks, DateTime deadline)
    {
        Task<Dictionary<int, StorageInfo>> q;
        lock (BusyLock)
        {
            storageQuery ??= Task.Run(() => { try { return QueryStorageApi(); } finally { lock (BusyLock) storageQuery = null; } });
            q = storageQuery;
        }
        if (!Task.WaitAll(new Task[] { q }, Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)) || !q.IsCompletedSuccessfully) return;
        foreach (var d in disks)
        {
            if (!q.Result.TryGetValue(d.index, out var s)) continue;
            if (d.media == "" && s.Media is 3 or 4) d.media = s.Media == 3 ? "HDD" : "SSD";
            if (d.source != "") continue;
            d.source = "Windows storage health (Get-PhysicalDisk)";
            d.health = s.Health switch { 0 => "Good", 1 => "Caution", 2 => "Bad", _ => d.health };
        }
    }

    static Dictionary<int, StorageInfo> QueryStorageApi()
    {
        var res = new Dictionary<int, StorageInfo>();
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage"), new ObjectQuery("SELECT DeviceId, HealthStatus, MediaType FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject o in s.Get())
            {
                if (!int.TryParse(o["DeviceId"]?.ToString(), out var id)) continue;
                res[id] = new StorageInfo(Convert.ToInt32(o["MediaType"] ?? 0), Convert.ToInt32(o["HealthStatus"] ?? 5));
            }
        }
        catch { }
        return res;
    }

    /* ---------- SMART / NVMe ---------- */

    static void ReadSmart(Disk d)
    {
        var h = Open($@"\\.\PhysicalDrive{d.index}", 0xC0000000);
        if (h.IsInvalid)
        {
            d.needAdmin = true;
            if (d.bus != "NVMe") return;
            h = Open($@"\\.\PhysicalDrive{d.index}", 0);
            if (h.IsInvalid) return;
        }
        using (h)
        {
            if (d.bus == "NVMe") ReadNvme(h, d); else ReadAta(h, d);
        }
    }

    static byte[] NvmeQuery(uint dataType, uint cns, int len)
    {
        var extra = new byte[40];
        BitConverter.GetBytes(3u).CopyTo(extra, 0); BitConverter.GetBytes(dataType).CopyTo(extra, 4); BitConverter.GetBytes(cns).CopyTo(extra, 8);
        BitConverter.GetBytes(40u).CopyTo(extra, 16); BitConverter.GetBytes((uint)len).CopyTo(extra, 20);
        var inp = new byte[8 + 40 + len];
        BitConverter.GetBytes(50u).CopyTo(inp, 0); extra.CopyTo(inp, 8);
        return inp;
    }

    static void ReadNvmeIdentify(SafeFileHandle h, Disk d)
    {
        var inp = NvmeQuery(1, 1, 4096);
        var o = Ioctl(h, IoctlStorageQuery, inp, inp.Length);
        if (o is not { Length: >= 48 + 300 }) return;
        d.pciVendor = U16(o, 48); d.subVendor = U16(o, 50); d.hmbBytes = (long)U32(o, 48 + 272) * 4096; d.HasIdentify = true;
    }

    static long U128(byte[] b, int o) => U64(b, o + 8) != 0 ? long.MaxValue : (long)U64(b, o);

    static void ReadNvme(SafeFileHandle h, Disk d)
    {
        var inp = NvmeQuery(2, 2, 512);
        var o = Ioctl(h, IoctlStorageQuery, inp, inp.Length);
        if (o is not { Length: >= 48 + 512 }) { d.notes.Add("The NVMe health log could not be read: " + (o == null ? "no data returned (run as administrator)" : "short reply")); return; }
        var l = o[48..(48 + 512)];
        d.source = "NVMe SMART / health log";
        int tempK = U16(l, 1); if (tempK > 0) d.temp = tempK - 273;
        int spare = l[3], spareThr = l[4], used = l[5];
        d.hostReads = U128(l, 32) * 512000; d.hostWrites = U128(l, 48) * 512000; d.powerCount = U128(l, 112); d.powerHours = U128(l, 128);
        var media = U128(l, 160);
        var rows = new (string name, long v, string st)[]
        {
            ("Critical Warning", l[0], l[0] != 0 ? "bad" : "ok"), ("Composite Temperature", Math.Max(0, d.temp), "ok"), ("Available Spare", spare, spare < spareThr ? "bad" : "ok"),
            ("Available Spare Threshold", spareThr, "ok"), ("Percentage Used", used, used >= 90 ? "caution" : "ok"), ("Data Units Read", U128(l, 32), "ok"), ("Data Units Written", U128(l, 48), "ok"),
            ("Host Read Commands", U128(l, 64), "ok"), ("Host Write Commands", U128(l, 80), "ok"), ("Controller Busy Time", U128(l, 96), "ok"), ("Power Cycles", d.powerCount, "ok"),
            ("Power On Hours", d.powerHours, "ok"), ("Unsafe Shutdowns", U128(l, 144), "ok"), ("Media and Data Integrity Errors", media, media > 0 ? "caution" : "ok"),
            ("Number of Error Information Log Entries", U128(l, 176), "ok")
        };
        for (var i = 0; i < rows.Length; i++) d.attrs.Add(new Attr { id = i + 1, name = rows[i].name, raw = rows[i].v.ToString(), rawHex = rows[i].v.ToString("X12"), status = rows[i].st, cur = -1, worst = -1, thr = -1 });
        d.healthPct = Math.Max(0, 100 - used);
        if (l[0] != 0)
        {
            d.health = "Bad";
            var names = new[] { "Available spare below threshold", "Temperature out of range", "Reliability degraded", "Media is read-only", "Volatile memory backup failed" };
            for (var bit = 0; bit < names.Length; bit++) if ((l[0] & (1 << bit)) != 0) d.reasons.Add(names[bit]);
        }
        else if (spare < spareThr) { d.health = "Bad"; d.reasons.Add("Available spare is below its threshold"); }
        else if (used >= 90 || media > 0)
        {
            d.health = "Caution";
            if (used >= 90) d.reasons.Add($"{used}% of rated endurance used");
            if (media > 0) d.reasons.Add($"{media} media / data integrity errors");
        }
        else d.health = "Good";
    }

    static readonly Dictionary<int, string> AtaNames = new()
    {
        [1] = "Read Error Rate", [2] = "Throughput Performance", [3] = "Spin-Up Time", [4] = "Start/Stop Count", [5] = "Reallocated Sectors Count", [6] = "Read Channel Margin", [7] = "Seek Error Rate",
        [8] = "Seek Time Performance", [9] = "Power-On Hours", [10] = "Spin Retry Count", [11] = "Recalibration Retries", [12] = "Power Cycle Count", [13] = "Soft Read Error Rate", [22] = "Current Helium Level",
        [160] = "Uncorrectable Sector Count", [161] = "Valid Spare Blocks", [163] = "Initial Bad Blocks", [164] = "Total Erase Count", [165] = "Max Erase Count", [166] = "Min Erase Count", [167] = "Average Erase Count",
        [168] = "Max Erase Count of Spec", [169] = "Remaining Life", [170] = "Available Reserved Space", [171] = "Program Fail Count", [172] = "Erase Fail Count", [173] = "Wear Leveling Count",
        [174] = "Unexpected Power Loss Count", [175] = "Power Loss Protection Failure", [176] = "Erase Fail Count (chip)", [177] = "Wear Range Delta", [179] = "Used Reserved Block Count (total)",
        [180] = "Unused Reserved Block Count (total)", [181] = "Program Fail Count (total)", [182] = "Erase Fail Count (total)", [183] = "Runtime Bad Block / SATA Downshift Errors", [184] = "End-to-End Error",
        [187] = "Reported Uncorrectable Errors", [188] = "Command Timeout", [189] = "High Fly Writes", [190] = "Airflow Temperature", [191] = "G-Sense Error Rate", [192] = "Power-off Retract Count",
        [193] = "Load/Unload Cycle Count", [194] = "Temperature", [195] = "Hardware ECC Recovered", [196] = "Reallocation Event Count", [197] = "Current Pending Sector Count", [198] = "Uncorrectable Sector Count",
        [199] = "UltraDMA CRC Error Count", [200] = "Write Error Rate", [201] = "Soft Read Error Rate", [202] = "Data Address Mark Errors", [206] = "Flying Height", [220] = "Disk Shift", [222] = "Loaded Hours",
        [223] = "Load Retry Count", [224] = "Load Friction", [225] = "Load/Unload Cycle Count", [226] = "Load-in Time", [230] = "GMR Head Amplitude / Drive Life Protection",
        [231] = "SSD Life Left / Temperature", [232] = "Endurance Remaining / Available Reserved Space", [233] = "Media Wearout Indicator / Power-On Hours", [234] = "Average / Maximum Erase Count",
        [235] = "Good Block Count", [240] = "Head Flying Hours", [241] = "Total LBAs Written", [242] = "Total LBAs Read", [243] = "Total LBAs Written (expanded)", [244] = "Total LBAs Read (expanded)",
        [249] = "NAND Writes (1 GiB)", [250] = "Read Error Retry Rate", [251] = "Minimum Spares Remaining", [252] = "Newly Added Bad Flash Block", [254] = "Free Fall Protection"
    };

    static byte[]? SmartCmd(SafeFileHandle h, byte feature, byte drive)
    {
        var inp = new byte[33];
        BitConverter.GetBytes(512u).CopyTo(inp, 0);
        inp[4] = feature; inp[5] = 1; inp[6] = 1; inp[7] = 0x4F; inp[8] = 0xC2; inp[9] = (byte)(0xA0 | ((drive & 1) << 4)); inp[10] = 0xB0; inp[12] = drive;
        var o = Ioctl(h, SmartRcv, inp, 16 + 512);
        return o is { Length: >= 16 + 362 } ? o[16..] : null;
    }

    static void ReadAta(SafeFileHandle h, Disk d)
    {
        byte drive = 0;
        var v = Ioctl(h, SmartVersion, null, 24);
        if (v is { Length: >= 5 }) { drive = v[4]; if (drive > 3) drive = 0; }
        var data = SmartCmd(h, 0xD0, drive);
        if (data == null)
        {
            d.notes.Add(d.bus == "USB" ? "This USB enclosure does not pass SMART commands through to Windows." : "SMART data could not be read: access denied or not supported (run as administrator)");
            return;
        }
        var thr = new Dictionary<int, int>();
        var t = SmartCmd(h, 0xD1, drive);
        if (t != null) for (var i = 0; i < 30; i++) { var o = 2 + i * 12; if (t[o] != 0) thr[t[o]] = t[o + 1]; }
        d.source = "ATA SMART";
        bool bad = false, caution = false;
        for (var i = 0; i < 30; i++)
        {
            var o = 2 + i * 12; int id = data[o];
            if (id == 0) continue;
            long raw = 0;
            for (var k = 5; k >= 0; k--) raw = raw << 8 | data[o + 5 + k];
            var a = new Attr { id = id, name = AtaNames.GetValueOrDefault(id, "Vendor specific"), cur = data[o + 3], worst = data[o + 4], thr = thr.GetValueOrDefault(id), raw = raw.ToString(), rawHex = raw.ToString("X12"), status = "ok" };
            switch (id)
            {
                case 9: d.powerHours = raw & 0xFFFFFFFF; break;
                case 12: d.powerCount = raw & 0xFFFFFFFF; break;
                case 194 or 190: { var tt = (int)(raw & 0xFF); if (tt is > 0 and < 120 && (id == 194 || d.temp == 0)) d.temp = tt; break; }
                case 241: d.hostWrites = raw * 512; break;
                case 242: d.hostReads = raw * 512; break;
                case 169 or 231 or 233: if (d.media == "SSD" && a.cur is > 0 and <= 100) d.healthPct = a.cur; break;
            }
            if (a.thr > 0 && a.thr < 0xFE && a.cur <= a.thr) { a.status = "bad"; bad = true; d.reasons.Add($"{a.name} ({id:X2}) is at or below its threshold"); }
            else if (id is 5 or 196 or 197 or 198 && (raw & 0xFFFFFFFF) > 0) { a.status = "caution"; caution = true; d.reasons.Add($"{a.name}: {raw & 0xFFFFFFFF}"); }
            d.attrs.Add(a);
        }
        d.health = bad ? "Bad" : caution ? "Caution" : "Good";
        if (d.healthPct == 0 && d.media == "SSD") d.healthPct = -1;
    }

    /* ---------- maker, controller, DRAM ---------- */

    static void Classify(Disk d)
    {
        foreach (var (re, name) in DiskData.Makers)
            if (re.IsMatch(d.model.Trim()) || re.IsMatch(d.vendor.Trim())) { d.maker = name; break; }
        if (d.HasIdentify)
        {
            d.controllerVendor = DiskData.PciVendors.GetValueOrDefault((ushort)d.pciVendor, $"Vendor ID 0x{d.pciVendor:X4}");
            if (d.maker == "") d.maker = DiskData.PciVendors.GetValueOrDefault((ushort)d.subVendor, "");
        }
        if ((d.maker == "" || d.maker.StartsWith("Vendor ID")) && d.model != "") d.maker = d.model.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (d.maker == "") d.maker = d.controllerVendor;

        var known = DiskData.KnownDrives.FirstOrDefault(k => k.Re.IsMatch(d.model));
        if (known != null)
        {
            d.controller = known.Controller;
            if (d.controllerVendor == "") d.controllerVendor = new[] { "Silicon Motion", "Phison", "Marvell", "SanDisk", "Samsung", "Micron", "SK hynix", "MAXIO", "InnoGrit", "Realtek" }.FirstOrDefault(v => known.Controller.StartsWith(v)) ?? "";
        }
        d.dram = "n/a"; d.dramNote = "";
        if (d.media != "SSD")
        {
            if (d.media == "HDD") d.dramNote = "Spinning disk: it has a small DRAM buffer, but there is no SSD cache to judge.";
            return;
        }
        var hmb = d.hmbBytes > 0;
        if (known is { Dram: false } && hmb) { d.dram = "dramless"; d.dramNote = $"DRAM-less. Matches {known.Controller} and asks Windows for a {d.hmbBytes >> 20} MB Host Memory Buffer instead of onboard DRAM."; }
        else if (known is { Dram: false }) { d.dram = "dramless"; d.dramNote = $"DRAM-less according to the model database ({known.Controller})."; }
        else if (known is { Dram: true } && hmb) { d.dram = "dram"; d.dramNote = $"Has a DRAM cache according to the model database ({known.Controller}); it also supports a Host Memory Buffer."; }
        else if (known != null) { d.dram = "dram"; d.dramNote = $"Has a DRAM cache according to the model database ({known.Controller})."; }
        else if (hmb) { d.dram = "dramless-likely"; d.dramNote = $"Probably DRAM-less: the drive requests a {d.hmbBytes >> 20} MB Host Memory Buffer from the PC, which DRAM-less NVMe drives do."; }
        else if (d.HasIdentify) { d.dram = "dram-likely"; d.dramNote = "Probably has a DRAM cache: the drive does not use a Host Memory Buffer. Not certain; some low-end drives have neither."; }
        else { d.dram = "unknown"; d.dramNote = "Windows does not report DRAM for SATA SSDs and this model is not in the database. Check the controller on the manufacturer's spec sheet."; }
    }
}
