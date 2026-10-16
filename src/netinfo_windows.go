// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"context"
	"fmt"
	"net"
	"net/http"
	"runtime"
	"sort"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

// DNS lookup, traceroute (one hop per request) and network adapter details.

/* ---------- DNS lookup ---------- */

type DNSRecord struct {
	Type  string `json:"type"`
	Value string `json:"value"`
	Extra string `json:"extra,omitempty"` // MX priority, SRV port, ...
}

// dnsResolver talks to one chosen server, or to the system's resolver when server is empty.
func dnsResolver(server string) (*net.Resolver, error) {
	if server == "" {
		return net.DefaultResolver, nil
	}
	host, port, err := net.SplitHostPort(server)
	if err != nil {
		host, port = strings.Trim(server, "[]"), "53"
	}
	ip := net.ParseIP(host)
	if ip == nil {
		return nil, fmt.Errorf("DNS server must be an IP address such as 1.1.1.1 (got %q)", server)
	}
	addr := net.JoinHostPort(ip.String(), port)
	return &net.Resolver{PreferGo: true, Dial: func(ctx context.Context, network, _ string) (net.Conn, error) {
		return (&net.Dialer{Timeout: 3 * time.Second}).DialContext(ctx, network, addr)
	}}, nil
}

var dnsTypes = []string{"A", "AAAA", "MX", "NS", "TXT", "CNAME", "PTR", "SRV"}

func dnsQuery(res *net.Resolver, name, typ string) ([]DNSRecord, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	var out []DNSRecord
	switch typ {
	case "A", "AAAA":
		network := "ip4"
		if typ == "AAAA" {
			network = "ip6"
		}
		ips, err := res.LookupIP(ctx, network, name)
		if err != nil {
			return nil, err
		}
		for _, ip := range ips {
			out = append(out, DNSRecord{Type: typ, Value: ip.String()})
		}
	case "MX":
		mx, err := res.LookupMX(ctx, name)
		if err != nil {
			return nil, err
		}
		for _, m := range mx {
			out = append(out, DNSRecord{Type: typ, Value: strings.TrimSuffix(m.Host, "."), Extra: fmt.Sprintf("priority %d", m.Pref)})
		}
	case "NS":
		ns, err := res.LookupNS(ctx, name)
		if err != nil {
			return nil, err
		}
		for _, n := range ns {
			out = append(out, DNSRecord{Type: typ, Value: strings.TrimSuffix(n.Host, ".")})
		}
	case "TXT":
		txt, err := res.LookupTXT(ctx, name)
		if err != nil {
			return nil, err
		}
		for _, t := range txt {
			out = append(out, DNSRecord{Type: typ, Value: t})
		}
	case "CNAME":
		c, err := res.LookupCNAME(ctx, name)
		if err != nil {
			return nil, err
		}
		if strings.TrimSuffix(c, ".") != strings.TrimSuffix(name, ".") {
			out = append(out, DNSRecord{Type: typ, Value: strings.TrimSuffix(c, ".")})
		}
	case "PTR":
		ip := net.ParseIP(strings.TrimSpace(name))
		if ip == nil {
			return nil, fmt.Errorf("PTR needs an IP address")
		}
		names, err := res.LookupAddr(ctx, ip.String())
		if err != nil {
			return nil, err
		}
		for _, n := range names {
			out = append(out, DNSRecord{Type: typ, Value: strings.TrimSuffix(n, ".")})
		}
	case "SRV":
		_, srv, err := res.LookupSRV(ctx, "", "", name) // name is the full _service._proto.domain
		if err != nil {
			return nil, err
		}
		for _, s := range srv {
			out = append(out, DNSRecord{Type: typ, Value: fmt.Sprintf("%s:%d", strings.TrimSuffix(s.Target, "."), s.Port), Extra: fmt.Sprintf("priority %d, weight %d", s.Priority, s.Weight)})
		}
	default:
		return nil, fmt.Errorf("unknown record type %q", typ)
	}
	return out, nil
}

func hDNS(w http.ResponseWriter, r *http.Request) {
	name := strings.TrimSpace(r.URL.Query().Get("name"))
	typ := strings.ToUpper(strings.TrimSpace(r.URL.Query().Get("type")))
	if name == "" {
		fail(w, 400, "name required")
		return
	}
	res, err := dnsResolver(strings.TrimSpace(r.URL.Query().Get("server")))
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	types := []string{typ}
	if typ == "" || typ == "ALL" {
		types = []string{"A", "AAAA", "CNAME", "MX", "NS", "TXT"}
		if net.ParseIP(name) != nil {
			types = []string{"PTR"}
		}
	}
	type result struct {
		Type    string      `json:"type"`
		Records []DNSRecord `json:"records"`
		Error   string      `json:"error,omitempty"`
		Ms      int64       `json:"ms"`
	}
	out := make([]result, len(types))
	var wg sync.WaitGroup
	for i, t := range types {
		wg.Add(1)
		go func() {
			defer wg.Done()
			t0 := time.Now()
			recs, err := dnsQuery(res, name, t)
			out[i] = result{Type: t, Records: recs, Ms: time.Since(t0).Milliseconds()}
			if recs == nil {
				out[i].Records = []DNSRecord{}
			}
			if err != nil {
				out[i].Error = dnsErrText(err)
			}
		}()
	}
	wg.Wait()
	writeJSON(w, map[string]any{"name": name, "server": r.URL.Query().Get("server"), "results": out})
}

func dnsErrText(err error) string {
	var de *net.DNSError
	if e, ok := err.(*net.DNSError); ok {
		de = e
	}
	switch {
	case de != nil && de.IsNotFound:
		return "No records"
	case de != nil && de.IsTimeout:
		return "Timed out"
	}
	return err.Error()
}

/* ---------- traceroute: the page asks for one TTL at a time ---------- */

type HopProbe struct {
	From   string  `json:"from"`
	Ms     float64 `json:"ms"`
	Status string  `json:"status"`
}

func hHop(w http.ResponseWriter, r *http.Request) {
	ip, err := resolve4(r.URL.Query().Get("host"))
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	ttl := qInt(r, "ttl", 1, 1, 255)
	timeout := time.Duration(qInt(r, "timeout", 1500, 100, 10000)) * time.Millisecond
	probes := qInt(r, "probes", 3, 1, 5)
	out := make([]HopProbe, probes)
	var wg sync.WaitGroup
	for i := range out {
		wg.Add(1)
		go func() {
			defer wg.Done()
			p := icmpPing(ip, timeout, 32, ttl)
			out[i] = HopProbe{From: p.From, Ms: p.Ms, Status: p.Status}
			if p.From == "" || (p.Status != "Reply" && p.Status != "TTL expired in transit") {
				out[i].From = ""
			}
		}()
	}
	wg.Wait()
	hop := ""
	for _, p := range out {
		if p.From != "" {
			hop = p.From
			break
		}
	}
	name := ""
	if hop != "" && r.URL.Query().Get("names") != "0" {
		ctx, cancel := context.WithTimeout(context.Background(), 1500*time.Millisecond)
		if n, err := net.DefaultResolver.LookupAddr(ctx, hop); err == nil && len(n) > 0 {
			name = strings.TrimSuffix(n[0], ".")
		}
		cancel()
	}
	writeJSON(w, map[string]any{"ttl": ttl, "ip": ip.String(), "hop": hop, "name": name, "probes": out, "done": hop == ip.String()})
}

/* ---------- network adapters (GetAdaptersAddresses) ---------- */

var procGetAdaptersAddresses = iphlpapi.NewProc("GetAdaptersAddresses")

type AdapterAddr struct {
	IP     string `json:"ip"`
	Prefix int    `json:"prefix"`
	V6     bool   `json:"v6"`
}

type Adapter struct {
	Name        string        `json:"name"`
	Description string        `json:"description"`
	GUID        string        `json:"guid"`
	Type        string        `json:"type"`
	Status      string        `json:"status"`
	MAC         string        `json:"mac"`
	MTU         int           `json:"mtu"`
	SpeedBps    uint64        `json:"speed"`
	DHCP        bool          `json:"dhcp"`
	DHCPServer  string        `json:"dhcpServer,omitempty"`
	DNSSuffix   string        `json:"dnsSuffix,omitempty"`
	Addrs       []AdapterAddr `json:"addrs"`
	Gateways    []string      `json:"gateways"`
	DNS         []string      `json:"dns"`
	Index       uint32        `json:"index"`
	Metric      uint32        `json:"metric"`
}

var ifTypes = map[uint32]string{1: "Other", 6: "Ethernet", 9: "Token ring", 23: "PPP", 24: "Loopback", 53: "Virtual", 71: "Wi-Fi", 131: "Tunnel", 144: "FireWire", 243: "Mobile broadband", 244: "Mobile broadband"}
var operStatus = map[uint32]string{1: "Up", 2: "Down", 3: "Testing", 4: "Unknown", 5: "Dormant", 6: "Not present", 7: "Lower layer down"}

// at turns an address handed out by Windows into a pointer. Going through a variable's address keeps go vet's unsafeptr check quiet:
// the memory belongs to the OS buffer we are still holding, not to the Go heap.
func at(u uintptr) unsafe.Pointer { return *(*unsafe.Pointer)(unsafe.Pointer(&u)) }

// Field offsets of IP_ADAPTER_ADDRESSES for the running architecture (pointers are 4 or 8 bytes wide).
func ptrAt(base uintptr, off uintptr) uintptr { return *(*uintptr)(at(base + off)) }
func u32At(base uintptr, off uintptr) uint32  { return *(*uint32)(at(base + off)) }
func u64At(base uintptr, off uintptr) uint64  { return *(*uint64)(at(base + off)) }
func alignUp(v, a uintptr) uintptr            { return (v + a - 1) &^ (a - 1) }

func cString(p uintptr) string {
	if p == 0 {
		return ""
	}
	var b []byte
	for ; *(*byte)(at(p)) != 0; p++ {
		b = append(b, *(*byte)(at(p)))
	}
	return string(b)
}

func wString(p uintptr) string {
	if p == 0 {
		return ""
	}
	var u []uint16
	for ; *(*uint16)(at(p)) != 0; p += 2 {
		u = append(u, *(*uint16)(at(p)))
	}
	return syscall.UTF16ToString(u)
}

// sockaddrIP reads an IPv4 / IPv6 address out of a SOCKADDR.
func sockaddrIP(sa uintptr) net.IP {
	if sa == 0 {
		return nil
	}
	switch *(*uint16)(at(sa)) {
	case 2:
		return net.IP(append([]byte(nil), (*[4]byte)(at(sa + 4))[:]...))
	case 23:
		return net.IP(append([]byte(nil), (*[16]byte)(at(sa + 8))[:]...))
	}
	return nil
}

func listAdapters() ([]Adapter, error) {
	var size uint32 = 16 << 10
	var buf []byte
	for i := 0; i < 4; i++ {
		buf = make([]byte, size)
		r, _, _ := procGetAdaptersAddresses.Call(0, 0x80|0x10, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size))) // INCLUDE_GATEWAYS | INCLUDE_PREFIX
		if r == 0 {
			break
		}
		if r != 111 { // ERROR_BUFFER_OVERFLOW: grow and retry
			return nil, syscall.Errno(r)
		}
		buf = nil
	}
	if buf == nil {
		return nil, fmt.Errorf("adapter list too large")
	}
	p := unsafe.Sizeof(uintptr(0))
	var out []Adapter
	for cur := uintptr(unsafe.Pointer(&buf[0])); cur != 0; cur = ptrAt(cur, 8) {
		phys := 8 + 9*p // PhysicalAddress[8] starts after the nine pointer fields
		a := Adapter{
			Index: u32At(cur, 4), Name: wString(ptrAt(cur, 8+8*p)), Description: wString(ptrAt(cur, 8+7*p)), GUID: cString(ptrAt(cur, 8+p)),
			DNSSuffix: wString(ptrAt(cur, 8+6*p)), Addrs: []AdapterAddr{}, Gateways: []string{}, DNS: []string{},
		}
		if n := int(u32At(cur, phys+8)); n > 0 && n <= 8 {
			a.MAC = net.HardwareAddr((*[8]byte)(at(cur + phys))[:n:n]).String()
			a.MAC = strings.ToUpper(strings.ReplaceAll(a.MAC, ":", "-"))
		}
		flags := u32At(cur, phys+12)
		a.DHCP = flags&0x4 != 0
		a.MTU = int(u32At(cur, phys+16))
		a.Type = ifTypes[u32At(cur, phys+20)]
		if a.Type == "" {
			a.Type = fmt.Sprintf("Type %d", u32At(cur, phys+20))
		}
		a.Status = operStatus[u32At(cur, phys+24)]
		zone := phys + 32 + 64 // ZoneIndices[16], then FirstPrefix
		speed := alignUp(zone+p, 8)
		a.SpeedBps = u64At(cur, speed)
		if a.SpeedBps == ^uint64(0) {
			a.SpeedBps = 0
		}
		wins := speed + 16
		gw := wins + p
		metric := gw + p
		a.Metric = u32At(cur, metric)
		dhcp4 := alignUp(metric+8, 8) + 8 // Ipv4Metric, Ipv6Metric, Luid, then Dhcpv4Server
		if sa := ptrAt(cur, dhcp4); a.DHCP && sa != 0 {
			a.DHCPServer = sockaddrIP(sa).String()
		}
		// unicast: Length, Flags, Next, Address{ptr,len}, PrefixOrigin, SuffixOrigin, DadState, Valid/Preferred/Lease, OnLinkPrefixLength
		for u := ptrAt(cur, 8+2*p); u != 0; u = ptrAt(u, 8) {
			if ip := sockaddrIP(ptrAt(u, 8+p)); ip != nil {
				a.Addrs = append(a.Addrs, AdapterAddr{IP: ip.String(), Prefix: int(*(*uint8)(at(u + 8 + 3*p + 24))), V6: ip.To4() == nil})
			}
		}
		for d := ptrAt(cur, 8+5*p); d != 0; d = ptrAt(d, 8) {
			if ip := sockaddrIP(ptrAt(d, 8+p)); ip != nil {
				a.DNS = append(a.DNS, ip.String())
			}
		}
		for g := ptrAt(cur, gw); g != 0; g = ptrAt(g, 8) {
			if ip := sockaddrIP(ptrAt(g, 8+p)); ip != nil {
				a.Gateways = append(a.Gateways, ip.String())
			}
		}
		a.DNS, a.Gateways = uniqStrings(a.DNS), uniqStrings(a.Gateways)
		out = append(out, a)
	}
	runtime.KeepAlive(buf)
	sort.SliceStable(out, func(i, j int) bool { // up adapters with a gateway first, loopback last
		rank := func(a Adapter) int {
			switch {
			case a.Type == "Loopback":
				return 3
			case a.Status == "Up" && len(a.Gateways) > 0:
				return 0
			case a.Status == "Up":
				return 1
			}
			return 2
		}
		return rank(out[i]) < rank(out[j])
	})
	return out, nil
}

func registerNetInfo(mux *http.ServeMux) {
	mux.HandleFunc("/api/net/dns", hDNS)
	mux.HandleFunc("/api/net/hop", hHop)
	mux.HandleFunc("/api/net/adapters", func(w http.ResponseWriter, r *http.Request) {
		a, err := listAdapters()
		if err != nil {
			fail(w, 500, err.Error())
			return
		}
		if a == nil {
			a = []Adapter{}
		}
		writeJSON(w, map[string]any{"adapters": a})
	})
}

func uniqStrings(in []string) []string {
	seen := map[string]bool{}
	out := in[:0]
	for _, s := range in {
		if !seen[s] {
			seen[s] = true
			out = append(out, s)
		}
	}
	return out
}
