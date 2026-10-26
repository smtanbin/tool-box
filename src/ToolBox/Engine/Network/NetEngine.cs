using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ToolBox.Engine;

/// <summary>Network tools: adapters, DNS, ping, traceroute, port test, IP scan and the raw TCP console. Replaces nettools.go / netinfo_windows.go / icmp_windows.go.</summary>
public static class NetEngine
{
    [DllImport("iphlpapi.dll")] static extern int SendARP(uint destIp, uint srcIp, byte[] mac, ref int len);
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)] static extern int DnsQuery_W(string name, ushort type, uint options, IntPtr extra, out IntPtr records, IntPtr reserved);
    [DllImport("dnsapi.dll")] static extern void DnsRecordListFree(IntPtr list, int freeType);

    public static void Register()
    {
        Router.Map("/api/net/adapters", _ => new { adapters = Adapters() });
        Router.Map("/api/net/local", _ => LocalNets().Select(n => new { name = n.Name, ip = n.Ip, cidr = n.Cidr }).ToList());
        Router.Map("/api/net/ping", async r => await PingOne(Resolve4(r.S("host")), r.I("timeout", 1000, 50, 10000), r.I("size", 32, 0, 65500), r.I("ttl", 128, 1, 255)));
        Router.Map("/api/net/hop", async r => await Hop(r));
        Router.Map("/api/net/ports", async r => await Ports(r));
        Router.Map("/api/net/dns", r => Dns(r));
        Router.Map("/api/net/scan/start", ScanStart);
        Router.Map("/api/net/scan/status", _ => ScanStatus());
        Router.MapVoid("/api/net/scan/cancel", _ => { _scan.Cancel = true; });
        Router.Map("/api/net/tcp/open", async r => await TcpOpen(r));
        Router.Map("/api/net/tcp/read", async r => await TcpRead(r));
        Router.Map("/api/net/tcp/send", async r => await TcpSend(r));
        Router.MapVoid("/api/net/tcp/close", r => { Sess(r)?.Close(); });
    }

    /* ---------- adapters ---------- */

    static object Adapters()
    {
        var list = new List<(int rank, object item)>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ip = n.GetIPProperties();
            var addrs = ip.UnicastAddresses.Select(a => new { ip = a.Address.ToString(), prefix = a.PrefixLength, v6 = a.Address.AddressFamily == AddressFamily.InterNetworkV6 }).ToList();
            var gws = ip.GatewayAddresses.Select(g => g.Address.ToString()).Where(g => g != "0.0.0.0" && g != "::").Distinct().ToList();
            var type = n.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
                NetworkInterfaceType.Wireless80211 => "Wi-Fi", NetworkInterfaceType.Loopback => "Loopback", NetworkInterfaceType.Tunnel => "Tunnel",
                NetworkInterfaceType.Ppp => "PPP", var t => t.ToString()
            };
            var status = n.OperationalStatus switch { OperationalStatus.Up => "Up", OperationalStatus.Down => "Down", OperationalStatus.NotPresent => "Not present", OperationalStatus.LowerLayerDown => "Lower layer down", var s => s.ToString() };
            var mac = string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            var rank = type == "Loopback" ? 3 : status == "Up" && gws.Count > 0 ? 0 : status == "Up" ? 1 : 2;
            System.Net.NetworkInformation.IPv4InterfaceProperties? v4 = null;
            try { v4 = ip.GetIPv4Properties(); } catch { }
            list.Add((rank, new
            {
                name = n.Name, description = n.Description, guid = n.Id, type, status, mac, mtu = v4?.Mtu ?? 0, speed = n.Speed > 0 ? n.Speed : 0,
                dhcp = v4?.IsDhcpEnabled ?? false, dhcpServer = ip.DhcpServerAddresses.FirstOrDefault()?.ToString() ?? "", dnsSuffix = ip.DnsSuffix,
                addrs, gateways = gws, dns = ip.DnsAddresses.Select(d => d.ToString()).Distinct().ToList(), index = v4?.Index ?? 0, metric = 0
            }));
        }
        return list.OrderBy(x => x.rank).Select(x => x.item).ToList();
    }

    sealed record LocalNet(string Name, string Ip, string Cidr, uint Net, uint Mask);

    static List<LocalNet> LocalNets()
    {
        var res = new List<LocalNet>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in n.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.Address.ToString().StartsWith("169.254.")) continue;
                var mask = a.PrefixLength == 0 ? 0 : uint.MaxValue << (32 - a.PrefixLength);
                var net = ToU(a.Address) & mask;
                res.Add(new LocalNet(n.Name, a.Address.ToString(), $"{FromU(net)}/{a.PrefixLength}", net, mask));
            }
        }
        return res;
    }

    static uint ToU(IPAddress a) { var b = a.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    static IPAddress FromU(uint u) => new(new[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u });

    /* ---------- ping / traceroute ---------- */

    /// <summary>Accepts what people paste: strips http:// or https://, user info, path, query, fragment and a trailing :port.</summary>
    public static string CleanHost(string s)
    {
        s = s.Trim();
        var i = s.IndexOf("://", StringComparison.Ordinal);
        if (i >= 0) s = s[(i + 3)..];
        s = s.Split('/', '?', '#')[0];
        var at = s.LastIndexOf('@'); if (at >= 0) s = s[(at + 1)..];
        if (s.StartsWith('[')) { var e = s.IndexOf(']'); if (e > 0) s = s[..(e + 1)]; }
        else if (s.Count(c => c == ':') == 1) s = s[..s.IndexOf(':')];
        return s.Trim('[', ']').TrimEnd('.');
    }

    static IPAddress Resolve4(string host)
    {
        host = CleanHost(host);
        if (host.Length == 0) throw new InvalidOperationException("host required");
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) return ip;
        try
        {
            var v4 = System.Net.Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return v4 ?? throw new InvalidOperationException($"{host} has no IPv4 address");
        }
        catch (SocketException) { throw new InvalidOperationException($"could not resolve \"{host}\""); }
    }

    static string StatusText(IPStatus s) => s switch
    {
        IPStatus.Success => "Reply", IPStatus.TtlExpired => "TTL expired in transit", IPStatus.TimedOut => "Request timed out",
        IPStatus.DestinationNetworkUnreachable => "Destination net unreachable", IPStatus.DestinationHostUnreachable => "Destination host unreachable",
        IPStatus.DestinationProtocolUnreachable => "Destination protocol unreachable", IPStatus.DestinationPortUnreachable => "Destination port unreachable",
        IPStatus.PacketTooBig => "Packet too big", _ => s.ToString()
    };

    sealed record PingRes(string ip, bool ok, double ms, int ttl, string from, string status);

    static async Task<object> PingOne(IPAddress ip, int timeoutMs, int size, int ttl) => await PingCore(ip, timeoutMs, size, ttl);

    static async Task<PingRes> PingCore(IPAddress ip, int timeoutMs, int size, int ttl)
    {
        try
        {
            using var p = new Ping();
            var data = new byte[size];
            for (var i = 0; i < data.Length; i++) data[i] = (byte)('a' + i % 23);
            var rep = await p.SendPingAsync(ip, timeoutMs, data, new PingOptions(ttl, true));
            return new PingRes(ip.ToString(), rep.Status == IPStatus.Success, rep.RoundtripTime, rep.Options?.Ttl ?? 0,
                rep.Address?.Equals(IPAddress.Any) == false && rep.Address != null && rep.Status is IPStatus.Success or IPStatus.TtlExpired ? rep.Address.ToString() : "", StatusText(rep.Status));
        }
        catch (PingException e) { return new PingRes(ip.ToString(), false, 0, 0, "", e.InnerException?.Message ?? e.Message); }
    }

    static async Task<object> Hop(Req r)
    {
        var ip = Resolve4(r.S("host"));
        var ttl = r.I("ttl", 1, 1, 255); var timeout = r.I("timeout", 1500, 100, 10000); var n = r.I("probes", 3, 1, 5);
        var probes = await Task.WhenAll(Enumerable.Range(0, n).Select(_ => PingCore(ip, timeout, 32, ttl)));
        var shaped = probes.Select(p => new { from = p.status is "Reply" or "TTL expired in transit" ? p.from : "", ms = p.ms, status = p.status }).ToList();
        var hop = shaped.FirstOrDefault(p => p.from.Length > 0)?.from ?? "";
        var name = "";
        if (hop.Length > 0 && r.S("names") != "0")
        {
            try
            {
                var t = System.Net.Dns.GetHostEntryAsync(hop);
                if (await Task.WhenAny(t, Task.Delay(1500)) == t) name = t.Result.HostName.TrimEnd('.');
            }
            catch { }
        }
        return new { ttl, ip = ip.ToString(), hop, name, probes = shaped, done = hop == ip.ToString() };
    }

    /* ---------- port test ---------- */

    static List<int> ParsePorts(string s)
    {
        var res = new List<int>(); var seen = new HashSet<int>();
        foreach (var part in s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ab = part.Split('-', 2);
            if (!int.TryParse(ab[0].Trim(), out var lo) || lo < 1) throw new InvalidOperationException($"bad port \"{part}\"");
            var hi = lo;
            if (ab.Length == 2 && (!int.TryParse(ab[1].Trim(), out hi) || hi > 65535 || lo > hi)) throw new InvalidOperationException($"bad port \"{part}\"");
            if (lo > 65535) throw new InvalidOperationException($"bad port \"{part}\"");
            for (var p = lo; p <= hi; p++) if (seen.Add(p)) res.Add(p);
            if (res.Count > 4096) throw new InvalidOperationException("too many ports (max 4096)");
        }
        return res;
    }

    sealed record PortRes(int port, bool open, double ms, string err);

    static async Task<PortRes> Probe(string host, int port, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var c = new TcpClient();
        try
        {
            var t = c.ConnectAsync(host, port);
            if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) return new PortRes(port, false, sw.Elapsed.TotalMilliseconds, "timed out (filtered)");
            await t;
            return new PortRes(port, true, sw.Elapsed.TotalMilliseconds, "");
        }
        catch (SocketException e) { return new PortRes(port, false, sw.Elapsed.TotalMilliseconds, e.SocketErrorCode == SocketError.ConnectionRefused ? "refused (closed)" : e.Message); }
        catch (Exception e) { return new PortRes(port, false, sw.Elapsed.TotalMilliseconds, e.Message); }
    }

    static async Task<object> Ports(Req r)
    {
        var host = CleanHost(r.S("host"));
        if (host.Length == 0) throw new InvalidOperationException("host required");
        List<int> ports;
        try { ports = ParsePorts(r.S("ports")); } catch { ports = new(); }
        if (ports.Count == 0) throw new InvalidOperationException("enter one or more ports, e.g. 22,80,443 or 8000-8010");
        var timeout = r.I("timeout", 2000, 100, 20000);
        var addr = Resolve4(host).ToString();
        var sem = new SemaphoreSlim(200);
        var results = await Task.WhenAll(ports.Select(async p => { await sem.WaitAsync(); try { return await Probe(addr, p, timeout); } finally { sem.Release(); } }));
        return new { ip = addr, results };
    }

    /* ---------- DNS ---------- */

    static object Dns(Req r)
    {
        var name = r.S("name").Contains("://") ? CleanHost(r.S("name")) : r.S("name").Trim();
        var typ = r.S("type").Trim().ToUpperInvariant();
        if (name.Length == 0) throw new InvalidOperationException("name required");
        var server = r.S("server").Trim();
        IPAddress? srv = null;
        if (server.Length > 0 && !IPAddress.TryParse(server, out srv)) throw new InvalidOperationException("DNS server must be an IP address");
        var types = typ is "" or "ALL" ? (IPAddress.TryParse(name, out _) ? new[] { "PTR" } : new[] { "A", "AAAA", "CNAME", "MX", "NS", "TXT" }) : new[] { typ };
        var results = types.AsParallel().AsOrdered().Select(t =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var recs = Query(name, t, srv);
                return new { type = t, records = recs, error = recs.Count == 0 ? "No records" : "", ms = sw.ElapsedMilliseconds };
            }
            catch (Exception e) { return new { type = t, records = new List<object>(), error = e.Message, ms = sw.ElapsedMilliseconds }; }
        }).ToList();
        return new { name, server, results };
    }

    static List<object> Query(string name, string type, IPAddress? server)
    {
        var res = new List<object>();
        if (type == "PTR")
        {
            if (!IPAddress.TryParse(name, out var ip)) throw new InvalidOperationException("PTR needs an IP address");
            var b = ip.GetAddressBytes();
            name = string.Join(".", b.Reverse()) + ".in-addr.arpa";
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                name = string.Join(".", b.SelectMany(x => new[] { x & 15, x >> 4 }).Reverse().Select(x => x.ToString("x"))) + ".ip6.arpa";
        }
        ushort wtype = type switch { "A" => 1, "NS" => 2, "CNAME" => 5, "PTR" => 12, "MX" => 15, "TXT" => 16, "AAAA" => 28, "SRV" => 33, _ => throw new InvalidOperationException($"unknown record type \"{type}\"") };
        var extra = IntPtr.Zero;
        if (server != null && server.AddressFamily == AddressFamily.InterNetwork)
        {
            extra = Marshal.AllocHGlobal(8);
            Marshal.WriteInt32(extra, 0, 1);
            Marshal.WriteInt32(extra, 4, BitConverter.ToInt32(server.GetAddressBytes(), 0));
        }
        try
        {
            var rc = DnsQuery_W(name, wtype, 0x8 /* BYPASS_CACHE */, extra, out var list, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException(rc is 9003 or 9501 ? "No records" : rc == 9002 ? "Timed out" : new System.ComponentModel.Win32Exception(rc).Message);
            try
            {
                var dataOff = 2 * IntPtr.Size + 16;
                for (var p = list; p != IntPtr.Zero; p = Marshal.ReadIntPtr(p))
                {
                    if ((ushort)Marshal.ReadInt16(p, 2 * IntPtr.Size) != wtype || Marshal.ReadInt32(p, 2 * IntPtr.Size + 4) is var section && (section & 3) != 1) continue;
                    var d = p + dataOff;
                    switch (wtype)
                    {
                        case 1: res.Add(new { type, value = new IPAddress((uint)Marshal.ReadInt32(d)).ToString() }); break;
                        case 28: { var b = new byte[16]; Marshal.Copy(d, b, 0, 16); res.Add(new { type, value = new IPAddress(b).ToString() }); break; }
                        case 2 or 5 or 12: res.Add(new { type, value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(d))!.TrimEnd('.') }); break;
                        case 15: res.Add(new { type, value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(d))!.TrimEnd('.'), extra = $"priority {(ushort)Marshal.ReadInt16(d, IntPtr.Size)}" }); break;
                        case 33: res.Add(new { type, value = $"{Marshal.PtrToStringUni(Marshal.ReadIntPtr(d))!.TrimEnd('.')}:{(ushort)Marshal.ReadInt16(d, IntPtr.Size + 4)}", extra = $"priority {(ushort)Marshal.ReadInt16(d, IntPtr.Size)}, weight {(ushort)Marshal.ReadInt16(d, IntPtr.Size + 2)}" }); break;
                        case 16:
                        {
                            var n = Marshal.ReadInt32(d); var sb = new StringBuilder();
                            for (var i = 0; i < n; i++) sb.Append(Marshal.PtrToStringUni(Marshal.ReadIntPtr(d, 4 + (IntPtr.Size == 8 ? 4 : 0) + i * IntPtr.Size)));
                            res.Add(new { type, value = sb.ToString() });
                            break;
                        }
                    }
                }
            }
            finally { DnsRecordListFree(list, 1); }
        }
        finally { if (extra != IntPtr.Zero) Marshal.FreeHGlobal(extra); }
        return res;
    }

    /* ---------- IP scanner ---------- */

    sealed class Scan
    {
        public readonly object Lock = new();
        public bool Running, Cancel;
        public List<object> Results = new(); public List<(uint key, object item)> Keyed = new();
        public int Total; public long Done; public DateTime Start, End; public string Range = "";
    }

    static readonly Scan _scan = new();

    static (uint lo, uint hi) ParseRange(string s)
    {
        s = s.Trim();
        uint lo, hi;
        var m = Regex.Match(s, @"^(\d+\.\d+\.\d+\.\d+)/(\d+)$");
        if (m.Success && IPAddress.TryParse(m.Groups[1].Value, out var ip) && int.Parse(m.Groups[2].Value) is >= 0 and <= 32 and var ones)
        {
            var mask = ones == 0 ? 0 : uint.MaxValue << (32 - ones);
            lo = ToU(ip) & mask; hi = lo | ~mask;
            if (ones <= 30) { lo++; hi--; }
        }
        else if (s.Contains('-'))
        {
            var ab = s.Split('-', 2);
            if (!IPAddress.TryParse(ab[0].Trim(), out var a)) throw new InvalidOperationException($"bad start address \"{ab[0]}\"");
            lo = ToU(a);
            if (IPAddress.TryParse(ab[1].Trim(), out var b)) hi = ToU(b);
            else if (int.TryParse(ab[1].Trim(), out var last) && last is >= 0 and <= 255) hi = (lo & ~0xFFu) | (uint)last;
            else throw new InvalidOperationException($"bad end address \"{ab[1]}\"");
        }
        else if (IPAddress.TryParse(s, out var one) && one.AddressFamily == AddressFamily.InterNetwork) lo = hi = ToU(one);
        else throw new InvalidOperationException("enter a range like 192.168.1.0/24 or 192.168.1.1-254");
        if (hi < lo) throw new InvalidOperationException("range end is before its start");
        if (hi - lo >= 65536) throw new InvalidOperationException("range too large (max 65,536 addresses, a /16)");
        return (lo, hi);
    }

    static object? ScanStart(Req r)
    {
        var (lo, hi) = ParseRange(r.S("range"));
        var ports = r.S("ports").Trim().Length > 0 ? ParsePorts(r.S("ports")) : new List<int>();
        if (ports.Count > 64) throw new InvalidOperationException("scan at most 64 ports per host");
        var timeout = r.I("timeout", 800, 100, 5000); var useArp = r.S("arp") != "0"; var doDns = r.S("dns") != "0";
        lock (_scan.Lock)
        {
            if (_scan.Running) throw new InvalidOperationException("a scan is already running");
            _scan.Running = true; _scan.Cancel = false; _scan.Keyed = new(); _scan.Total = (int)(hi - lo + 1); _scan.Done = 0; _scan.Start = DateTime.UtcNow; _scan.End = default; _scan.Range = r.S("range");
        }
        var locals = LocalNets();
        _ = Task.Run(async () =>
        {
            var sem = new SemaphoreSlim(128);
            var tasks = new List<Task>();
            for (var u = lo; ; u++)
            {
                if (_scan.Cancel) break;
                await sem.WaitAsync();
                var addr = u;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (_scan.Cancel) return;
                        var ip = FromU(addr);
                        var h = await ScanHost(ip, ports, timeout, useArp && locals.Any(l => (addr & l.Mask) == l.Net), doDns);
                        if (h != null) lock (_scan.Lock) _scan.Keyed.Add((addr, h));
                    }
                    finally { Interlocked.Increment(ref _scan.Done); sem.Release(); }
                }));
                if (u == hi) break;
            }
            await Task.WhenAll(tasks);
            lock (_scan.Lock) { _scan.Running = false; _scan.End = DateTime.UtcNow; }
        });
        return null;
    }

    static async Task<object?> ScanHost(IPAddress ip, List<int> ports, int timeout, bool arp, bool dns)
    {
        string via = ""; double ms = 0; int ttl = 0; string mac = ""; string host = "";
        var p = await PingCore(ip, timeout, 32, 128);
        if (p.ok) { via = "ping"; ms = p.ms; ttl = p.ttl; }
        var open = new List<int>();
        if (ports.Count > 0)
        {
            var res = await Task.WhenAll(ports.Select(x => Probe(ip.ToString(), x, Math.Min(timeout, 600))));
            foreach (var x in res.Where(x => x.open)) { open.Add(x.port); if (via == "") { via = "port"; ms = x.ms; } }
            open.Sort();
        }
        if (arp)
        {
            var b = new byte[8]; var len = 8;
            if (SendARP(BitConverter.ToUInt32(ip.GetAddressBytes(), 0), 0, b, ref len) == 0 && len >= 6)
            {
                mac = string.Join(":", b.Take(6).Select(x => x.ToString("x2")));
                if (via == "") via = "arp";
            }
        }
        if (via == "") return null;
        if (dns)
        {
            try { var t = System.Net.Dns.GetHostEntryAsync(ip); if (await Task.WhenAny(t, Task.Delay(1000)) == t) host = t.Result.HostName; } catch { }
        }
        return new { ip = ip.ToString(), host, ms, ttl, mac, ports = open, via };
    }

    static object ScanStatus()
    {
        lock (_scan.Lock)
        {
            var end = _scan.Running || _scan.End == default ? DateTime.UtcNow : _scan.End;
            var el = _scan.Start == default ? 0 : (end - _scan.Start).TotalSeconds;
            return new { running = _scan.Running, done = Interlocked.Read(ref _scan.Done), total = _scan.Total, results = _scan.Keyed.OrderBy(k => k.key).Select(k => k.item).ToList(), elapsed = el, cancelled = _scan.Cancel, range = _scan.Range };
        }
    }

    /* ---------- raw TCP / telnet console ---------- */

    sealed class Session
    {
        public TcpClient Client = new(); public NetworkStream? Stream;
        public readonly StringBuilder Buf = new(); public bool Closed, Telnet; public string Error = "";
        public readonly SemaphoreSlim Signal = new(0);
        public DateTime LastUse = DateTime.UtcNow;
        public void Close() { try { Stream?.Dispose(); } catch { } try { Client.Dispose(); } catch { } }
    }

    static readonly ConcurrentDictionary<int, Session> Sessions = new();
    static Timer? _reaper;

    /// <summary>A page that went away without closing its connection must not keep a socket open forever.</summary>
    static void ReapIdleSessions()
    {
        foreach (var (id, s) in Sessions)
            if (DateTime.UtcNow - s.LastUse > TimeSpan.FromMinutes(2)) { s.Close(); if (s.Closed) Sessions.TryRemove(id, out _); }
    }
    static int _nextId;

    static Session? Sess(Req r) => int.TryParse(r.S("id"), out var id) && Sessions.TryGetValue(id, out var s) ? s : null;

    static async Task<object> TcpOpen(Req r)
    {
        var host = CleanHost(r.S("host")); var port = r.I("port", 0, 0, 65535);
        if (host.Length == 0 || port == 0) throw new InvalidOperationException("host and port required");
        var timeout = r.I("timeout", 5000, 100, 30000);
        var s = new Session { Telnet = r.S("telnet") == "1" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var t = s.Client.ConnectAsync(host, port);
            if (await Task.WhenAny(t, Task.Delay(timeout)) != t) { s.Close(); return new { ok = false, ms = sw.Elapsed.TotalMilliseconds, error = "connection timed out" }; }
            await t;
        }
        catch (Exception e) { s.Close(); return new { ok = false, ms = sw.Elapsed.TotalMilliseconds, error = e.InnerException?.Message ?? e.Message }; }
        s.Stream = s.Client.GetStream();
        _reaper ??= new Timer(_ => ReapIdleSessions(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        var id = Interlocked.Increment(ref _nextId);
        Sessions[id] = s;
        _ = Task.Run(() => Reader(id, s));
        return new { ok = true, id, ms = sw.Elapsed.TotalMilliseconds, remote = s.Client.Client.RemoteEndPoint?.ToString(), local = s.Client.Client.LocalEndPoint?.ToString() };
    }

    static async Task Reader(int id, Session s)
    {
        var b = new byte[16384];
        try
        {
            while (true)
            {
                var n = await s.Stream!.ReadAsync(b);
                if (n == 0) break;
                var data = b[..n];
                if (s.Telnet) data = Negotiate(s, data);
                lock (s.Buf) { s.Buf.Append(Encoding.UTF8.GetString(data)); if (s.Buf.Length > 1 << 20) s.Buf.Remove(0, s.Buf.Length - (1 << 20)); }
                s.Signal.Release();
            }
        }
        catch (Exception e) { if (!s.Closed && e is not ObjectDisposedException) s.Error = e.InnerException?.Message ?? e.Message; }
        s.Closed = true; s.Signal.Release(); s.Close();
        _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(t => Sessions.TryRemove(id, out _));
    }

    static byte[] Negotiate(Session s, byte[] input)
    {
        const byte IAC = 255, DONT = 254, DO = 253, WONT = 252, WILL = 251, SB = 250, SE = 240;
        var o = new List<byte>(); var reply = new List<byte>();
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != IAC || i + 1 >= input.Length) { o.Add(input[i]); continue; }
            var cmd = input[i + 1];
            if (cmd == IAC) { o.Add(IAC); i++; }
            else if ((cmd is DO or DONT or WILL or WONT) && i + 2 < input.Length)
            {
                if (cmd == DO) reply.AddRange(new[] { IAC, WONT, input[i + 2] }); else if (cmd == WILL) reply.AddRange(new[] { IAC, DONT, input[i + 2] });
                i += 2;
            }
            else if (cmd == SB) { var j = i + 2; while (j + 1 < input.Length && !(input[j] == IAC && input[j + 1] == SE)) j++; i = j + 1; }
            else i++;
        }
        if (reply.Count > 0) try { s.Stream!.Write(reply.ToArray()); } catch { }
        return o.ToArray();
    }

    static async Task<object> TcpRead(Req r)
    {
        var s = Sess(r) ?? throw new InvalidOperationException("no such connection");
        s.LastUse = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            lock (s.Buf)
                if (s.Buf.Length > 0 || s.Closed)
                {
                    var data = s.Buf.ToString(); var closed = s.Closed && s.Buf.Length == 0; s.Buf.Clear();
                    return new { data, closed, error = s.Error };
                }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !await s.Signal.WaitAsync(left)) return new { data = "", closed = false, error = "" };
        }
    }

    static async Task<object?> TcpSend(Req r)
    {
        var s = Sess(r) ?? throw new InvalidOperationException("no such connection");
        s.LastUse = DateTime.UtcNow;
        var bytes = Encoding.UTF8.GetBytes(r.B("data"));
        await s.Stream!.WriteAsync(bytes);
        return null;
    }
}
