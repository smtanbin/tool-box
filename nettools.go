// Copyright 2026 Tanbin (magestica.com). Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/http"
	"sort"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

func registerNet(mux *http.ServeMux) {
	mux.HandleFunc("/api/net/ping", hPing)
	mux.HandleFunc("/api/net/ports", hPorts)
	mux.HandleFunc("/api/net/scan/start", hIPScanStart)
	mux.HandleFunc("/api/net/scan/status", hIPScanStatus)
	mux.HandleFunc("/api/net/scan/cancel", func(w http.ResponseWriter, r *http.Request) {
		ipscan.cancel.Store(true)
		w.WriteHeader(204)
	})
	mux.HandleFunc("/api/net/local", hLocalNets)
	mux.HandleFunc("/api/net/tcp/open", hTCPOpen)
	mux.HandleFunc("/api/net/tcp/send", hTCPSend)
	mux.HandleFunc("/api/net/tcp/read", hTCPRead)
	mux.HandleFunc("/api/net/tcp/close", hTCPClose)
}

func qInt(r *http.Request, k string, def, lo, hi int) int {
	v, err := strconv.Atoi(r.URL.Query().Get(k))
	if err != nil {
		return def
	}
	return max(lo, min(hi, v))
}

// resolve4 returns the first IPv4 address of host.
func resolve4(host string) (net.IP, error) {
	host = strings.Trim(strings.TrimSpace(host), "[]")
	if host == "" {
		return nil, errors.New("host required")
	}
	if ip := net.ParseIP(host); ip != nil {
		if ip.To4() == nil {
			return nil, errors.New("only IPv4 addresses are supported for ping")
		}
		return ip.To4(), nil
	}
	ctx, cancel := context.WithTimeout(context.Background(), 4*time.Second)
	defer cancel()
	ips, err := net.DefaultResolver.LookupIP(ctx, "ip4", host)
	if err != nil || len(ips) == 0 {
		return nil, fmt.Errorf("could not resolve %q", host)
	}
	return ips[0].To4(), nil
}

func reverseDNS(ip net.IP) string {
	ctx, cancel := context.WithTimeout(context.Background(), 1500*time.Millisecond)
	defer cancel()
	names, err := net.DefaultResolver.LookupAddr(ctx, ip.String())
	if err != nil || len(names) == 0 {
		return ""
	}
	return strings.TrimSuffix(names[0], ".")
}

func hPing(w http.ResponseWriter, r *http.Request) {
	ip, err := resolve4(r.URL.Query().Get("host"))
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	timeout := time.Duration(qInt(r, "timeout", 1000, 50, 10000)) * time.Millisecond
	writeJSON(w, icmpPing(ip, timeout, qInt(r, "size", 32, 0, 65500), qInt(r, "ttl", 128, 1, 255)))
}

// parsePorts accepts "22,80,443,8000-8010".
func parsePorts(s string) ([]int, error) {
	var out []int
	seen := map[int]bool{}
	for _, part := range strings.FieldsFunc(s, func(c rune) bool { return c == ',' || c == ' ' || c == ';' }) {
		a, b, rng := strings.Cut(part, "-")
		lo, err := strconv.Atoi(strings.TrimSpace(a))
		hi := lo
		if err == nil && rng {
			hi, err = strconv.Atoi(strings.TrimSpace(b))
		}
		if err != nil || lo < 1 || hi > 65535 || lo > hi {
			return nil, fmt.Errorf("bad port %q", part)
		}
		for p := lo; p <= hi; p++ {
			if !seen[p] {
				seen[p] = true
				out = append(out, p)
			}
		}
		if len(out) > 4096 {
			return nil, errors.New("too many ports (max 4096)")
		}
	}
	return out, nil
}

type PortResult struct {
	Port int     `json:"port"`
	Open bool    `json:"open"`
	Ms   float64 `json:"ms"`
	Err  string  `json:"err,omitempty"`
}

func probe(host string, port int, timeout time.Duration) PortResult {
	start := time.Now()
	c, err := net.DialTimeout("tcp", net.JoinHostPort(host, strconv.Itoa(port)), timeout)
	res := PortResult{Port: port, Ms: float64(time.Since(start).Microseconds()) / 1000}
	if err != nil {
		var ne net.Error
		switch {
		case errors.As(err, &ne) && ne.Timeout():
			res.Err = "timed out (filtered)"
		case strings.Contains(err.Error(), "refused"):
			res.Err = "refused (closed)"
		default:
			res.Err = err.Error()
		}
		return res
	}
	c.Close()
	res.Open = true
	return res
}

func hPorts(w http.ResponseWriter, r *http.Request) {
	host := strings.Trim(strings.TrimSpace(r.URL.Query().Get("host")), "[]")
	if host == "" {
		fail(w, 400, "host required")
		return
	}
	ports, err := parsePorts(r.URL.Query().Get("ports"))
	if err != nil || len(ports) == 0 {
		fail(w, 400, "enter one or more ports, e.g. 22,80,443 or 8000-8010")
		return
	}
	timeout := time.Duration(qInt(r, "timeout", 2000, 100, 20000)) * time.Millisecond
	addr := host
	if ips, err := net.LookupIP(host); err == nil && len(ips) > 0 {
		addr = ips[0].String()
	} else if net.ParseIP(host) == nil {
		fail(w, 400, fmt.Sprintf("could not resolve %q", host))
		return
	}
	out := make([]PortResult, len(ports))
	sem := make(chan struct{}, 200)
	var wg sync.WaitGroup
	for i, p := range ports {
		wg.Add(1)
		sem <- struct{}{}
		go func(i, p int) {
			defer func() { <-sem; wg.Done() }()
			out[i] = probe(addr, p, timeout)
		}(i, p)
	}
	wg.Wait()
	writeJSON(w, map[string]any{"ip": addr, "results": out})
}

/* ---------- IP scanner ---------- */

type HostResult struct {
	IP    string  `json:"ip"`
	Host  string  `json:"host"`
	Ms    float64 `json:"ms"`
	TTL   int     `json:"ttl"`
	MAC   string  `json:"mac"`
	Ports []int   `json:"ports"`
	Via   string  `json:"via"`
	key   uint32
}

type IPScan struct {
	mu       sync.Mutex
	running  bool
	results  []HostResult
	total    int
	done     atomic.Int64
	cancel   atomic.Bool
	start    time.Time
	end      time.Time
	rangeStr string
}

var ipscan = &IPScan{}

func ipToU(ip net.IP) uint32 { return binary.BigEndian.Uint32(ip.To4()) }
func uToIP(u uint32) net.IP {
	b := make(net.IP, 4)
	binary.BigEndian.PutUint32(b, u)
	return b
}

// parseRange accepts CIDR, a.b.c.d-e, a.b.c.d-w.x.y.z, or a single address.
func parseRange(s string) (lo, hi uint32, err error) {
	s = strings.TrimSpace(s)
	if _, n, e := net.ParseCIDR(s); e == nil {
		if n.IP.To4() == nil {
			return 0, 0, errors.New("only IPv4 ranges are supported")
		}
		ones, bits := n.Mask.Size()
		lo = ipToU(n.IP)
		hi = lo | (1<<uint(bits-ones) - 1)
		if ones <= 30 { // skip network and broadcast addresses
			lo, hi = lo+1, hi-1
		}
	} else if a, b, ok := strings.Cut(s, "-"); ok {
		ia := net.ParseIP(strings.TrimSpace(a)).To4()
		if ia == nil {
			return 0, 0, fmt.Errorf("bad start address %q", a)
		}
		b = strings.TrimSpace(b)
		if ib := net.ParseIP(b).To4(); ib != nil {
			lo, hi = ipToU(ia), ipToU(ib)
		} else if last, e := strconv.Atoi(b); e == nil && last >= 0 && last <= 255 {
			lo = ipToU(ia)
			hi = lo&^0xff | uint32(last)
		} else {
			return 0, 0, fmt.Errorf("bad end address %q", b)
		}
	} else if ip := net.ParseIP(s).To4(); ip != nil {
		lo, hi = ipToU(ip), ipToU(ip)
	} else {
		return 0, 0, errors.New("enter a range like 192.168.1.0/24 or 192.168.1.1-254")
	}
	if hi < lo {
		return 0, 0, errors.New("range end is before its start")
	}
	if hi-lo >= 65536 {
		return 0, 0, errors.New("range too large (max 65,536 addresses, a /16)")
	}
	return lo, hi, nil
}

type localNet struct {
	Name string `json:"name"`
	IP   string `json:"ip"`
	CIDR string `json:"cidr"`
	n    *net.IPNet
}

func localNets() []localNet {
	var out []localNet
	ifs, _ := net.Interfaces()
	for _, it := range ifs {
		if it.Flags&net.FlagUp == 0 || it.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, _ := it.Addrs()
		for _, a := range addrs {
			ipn, ok := a.(*net.IPNet)
			if !ok || ipn.IP.To4() == nil || ipn.IP.IsLinkLocalUnicast() {
				continue
			}
			ones, _ := ipn.Mask.Size()
			nw := &net.IPNet{IP: ipn.IP.Mask(ipn.Mask), Mask: ipn.Mask}
			out = append(out, localNet{it.Name, ipn.IP.String(), fmt.Sprintf("%s/%d", nw.IP, ones), nw})
		}
	}
	return out
}

func hLocalNets(w http.ResponseWriter, r *http.Request) {
	ns := localNets()
	if ns == nil {
		ns = []localNet{}
	}
	writeJSON(w, ns)
}

func hIPScanStart(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	lo, hi, err := parseRange(q.Get("range"))
	if err != nil {
		fail(w, 400, err.Error())
		return
	}
	var ports []int
	if ps := strings.TrimSpace(q.Get("ports")); ps != "" {
		if ports, err = parsePorts(ps); err != nil {
			fail(w, 400, err.Error())
			return
		}
		if len(ports) > 64 {
			fail(w, 400, "scan at most 64 ports per host")
			return
		}
	}
	timeout := time.Duration(qInt(r, "timeout", 800, 100, 5000)) * time.Millisecond
	useARP, doDNS := q.Get("arp") != "0", q.Get("dns") != "0"
	threads := qInt(r, "threads", 128, 1, 512)

	s := ipscan
	s.mu.Lock()
	if s.running {
		s.mu.Unlock()
		fail(w, 409, "a scan is already running")
		return
	}
	s.running, s.results, s.total, s.start, s.end, s.rangeStr = true, nil, int(hi-lo+1), time.Now(), time.Time{}, q.Get("range")
	s.done.Store(0)
	s.cancel.Store(false)
	s.mu.Unlock()

	locals := localNets()
	isLocal := func(ip net.IP) bool {
		for _, l := range locals {
			if l.n.Contains(ip) {
				return true
			}
		}
		return false
	}
	go func() {
		jobs := make(chan uint32)
		var wg sync.WaitGroup
		for i := 0; i < threads; i++ {
			wg.Add(1)
			go func() {
				defer wg.Done()
				for u := range jobs {
					if !s.cancel.Load() {
						if h, ok := scanHost(uToIP(u), ports, timeout, useARP && isLocal(uToIP(u)), doDNS); ok {
							h.key = u
							s.mu.Lock()
							s.results = append(s.results, h)
							s.mu.Unlock()
						}
					}
					s.done.Add(1)
				}
			}()
		}
		for u := lo; ; u++ {
			if s.cancel.Load() {
				break
			}
			jobs <- u
			if u == hi {
				break
			}
		}
		close(jobs)
		wg.Wait()
		s.mu.Lock()
		s.running, s.end = false, time.Now()
		s.mu.Unlock()
	}()
	w.WriteHeader(204)
}

func scanHost(ip net.IP, ports []int, timeout time.Duration, arp, dns bool) (HostResult, bool) {
	h := HostResult{IP: ip.String(), Ports: []int{}}
	if p := icmpPing(ip, timeout, 32, 128); p.OK {
		h.Ms, h.TTL, h.Via = p.Ms, p.TTL, "ping"
	}
	if len(ports) > 0 {
		pt := min(timeout, 600*time.Millisecond)
		var mu sync.Mutex
		var wg sync.WaitGroup
		for _, p := range ports {
			wg.Add(1)
			go func(p int) {
				defer wg.Done()
				if res := probe(h.IP, p, pt); res.Open {
					mu.Lock()
					h.Ports = append(h.Ports, p)
					if h.Via == "" {
						h.Via, h.Ms = "port", res.Ms
					}
					mu.Unlock()
				}
			}(p)
		}
		wg.Wait()
		sort.Ints(h.Ports)
	}
	if arp {
		h.MAC = arpMAC(ip)
		if h.MAC != "" && h.Via == "" {
			h.Via = "arp"
		}
	}
	if h.Via == "" {
		return h, false
	}
	if dns {
		h.Host = reverseDNS(ip)
	}
	return h, true
}

func hIPScanStatus(w http.ResponseWriter, r *http.Request) {
	s := ipscan
	s.mu.Lock()
	defer s.mu.Unlock()
	res := append([]HostResult{}, s.results...)
	sort.Slice(res, func(i, j int) bool { return res[i].key < res[j].key })
	end := s.end
	if s.running || end.IsZero() {
		end = time.Now()
	}
	el := 0.0
	if !s.start.IsZero() {
		el = end.Sub(s.start).Seconds()
	}
	writeJSON(w, map[string]any{"running": s.running, "done": s.done.Load(), "total": s.total, "results": res,
		"elapsed": el, "cancelled": s.cancel.Load(), "range": s.rangeStr})
}

/* ---------- raw TCP / telnet console ---------- */

type tcpSess struct {
	conn    net.Conn
	mu      sync.Mutex
	buf     []byte
	closed  bool
	errMsg  string
	notify  chan struct{}
	telnet  bool
	lastUse atomic.Int64
}

var (
	tcpMu    sync.Mutex
	tcpSess_ = map[int]*tcpSess{}
	tcpNext  int
)

func getSess(r *http.Request) *tcpSess {
	id, _ := strconv.Atoi(r.URL.Query().Get("id"))
	tcpMu.Lock()
	defer tcpMu.Unlock()
	s := tcpSess_[id]
	if s != nil {
		s.lastUse.Store(time.Now().UnixNano())
	}
	return s
}

func hTCPOpen(w http.ResponseWriter, r *http.Request) {
	host := strings.Trim(strings.TrimSpace(r.URL.Query().Get("host")), "[]")
	port := qInt(r, "port", 0, 0, 65535)
	if host == "" || port == 0 {
		fail(w, 400, "host and port required")
		return
	}
	timeout := time.Duration(qInt(r, "timeout", 5000, 100, 30000)) * time.Millisecond
	start := time.Now()
	c, err := net.DialTimeout("tcp", net.JoinHostPort(host, strconv.Itoa(port)), timeout)
	ms := float64(time.Since(start).Microseconds()) / 1000
	if err != nil {
		writeJSON(w, map[string]any{"ok": false, "ms": ms, "error": err.Error()})
		return
	}
	s := &tcpSess{conn: c, notify: make(chan struct{}, 1), telnet: r.URL.Query().Get("telnet") == "1"}
	s.lastUse.Store(time.Now().UnixNano())
	tcpMu.Lock()
	tcpNext++
	id := tcpNext
	tcpSess_[id] = s
	tcpMu.Unlock()
	go s.reader(id)
	writeJSON(w, map[string]any{"ok": true, "id": id, "ms": ms, "remote": c.RemoteAddr().String(), "local": c.LocalAddr().String()})
}

func (s *tcpSess) reader(id int) {
	b := make([]byte, 16384)
	for {
		s.conn.SetReadDeadline(time.Now().Add(30 * time.Second))
		n, err := s.conn.Read(b)
		if n > 0 {
			data := b[:n]
			if s.telnet {
				data = s.negotiate(data)
			}
			s.mu.Lock()
			s.buf = append(s.buf, data...)
			if len(s.buf) > 1<<20 {
				s.buf = s.buf[len(s.buf)-1<<20:]
			}
			s.mu.Unlock()
			s.poke()
		}
		if err != nil {
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				// drop sessions the page has stopped polling for 2 minutes
				if time.Since(time.Unix(0, s.lastUse.Load())) < 2*time.Minute {
					continue
				}
			}
			s.mu.Lock()
			s.closed = true
			if !errors.Is(err, net.ErrClosed) && err.Error() != "EOF" {
				s.errMsg = err.Error()
			}
			s.mu.Unlock()
			s.conn.Close()
			s.poke()
			time.AfterFunc(time.Minute, func() { tcpMu.Lock(); delete(tcpSess_, id); tcpMu.Unlock() })
			return
		}
	}
}

func (s *tcpSess) poke() {
	select {
	case s.notify <- struct{}{}:
	default:
	}
}

// negotiate strips telnet IAC sequences and refuses every option the server proposes.
func (s *tcpSess) negotiate(in []byte) []byte {
	const iac, dont, do, wont, will, sb, se = 255, 254, 253, 252, 251, 250, 240
	out := make([]byte, 0, len(in))
	var reply []byte
	for i := 0; i < len(in); i++ {
		if in[i] != iac || i+1 >= len(in) {
			out = append(out, in[i])
			continue
		}
		cmd := in[i+1]
		switch {
		case cmd == iac:
			out = append(out, iac)
			i++
		case (cmd == do || cmd == dont || cmd == will || cmd == wont) && i+2 < len(in):
			if cmd == do {
				reply = append(reply, iac, wont, in[i+2])
			} else if cmd == will {
				reply = append(reply, iac, dont, in[i+2])
			}
			i += 2
		case cmd == sb:
			j := i + 2
			for j+1 < len(in) && !(in[j] == iac && in[j+1] == se) {
				j++
			}
			i = j + 1
		default:
			i++
		}
	}
	if len(reply) > 0 {
		s.conn.Write(reply)
	}
	return out
}

func hTCPSend(w http.ResponseWriter, r *http.Request) {
	s := getSess(r)
	if s == nil {
		fail(w, 404, "no such connection")
		return
	}
	var req struct {
		Data string `json:"data"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		fail(w, 400, err.Error())
		return
	}
	s.conn.SetWriteDeadline(time.Now().Add(10 * time.Second))
	if _, err := s.conn.Write([]byte(req.Data)); err != nil {
		fail(w, 500, err.Error())
		return
	}
	w.WriteHeader(204)
}

// hTCPRead long-polls for up to 15 s and returns whatever the server sent.
func hTCPRead(w http.ResponseWriter, r *http.Request) {
	s := getSess(r)
	if s == nil {
		fail(w, 404, "no such connection")
		return
	}
	deadline := time.After(15 * time.Second)
	for {
		s.mu.Lock()
		if len(s.buf) > 0 || s.closed {
			data, closed, msg := strings.ToValidUTF8(string(s.buf), "�"), s.closed && len(s.buf) == 0, s.errMsg
			s.buf = nil
			s.mu.Unlock()
			writeJSON(w, map[string]any{"data": data, "closed": closed, "error": msg})
			return
		}
		s.mu.Unlock()
		select {
		case <-s.notify:
		case <-deadline:
			writeJSON(w, map[string]any{"data": "", "closed": false})
			return
		case <-r.Context().Done():
			return
		}
	}
}

func hTCPClose(w http.ResponseWriter, r *http.Request) {
	if s := getSess(r); s != nil {
		s.conn.Close()
	}
	w.WriteHeader(204)
}
