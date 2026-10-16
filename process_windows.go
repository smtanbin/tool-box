// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/binary"
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

// Process monitor: process list with CPU / memory, open TCP / UDP ports with their owning process,
// and kill (single process, whole tree, or whatever owns a port). Plain Win32 calls, no cgo.

var (
	procGetExtendedTcpTable   = iphlpapi.NewProc("GetExtendedTcpTable")
	procGetExtendedUdpTable   = iphlpapi.NewProc("GetExtendedUdpTable")
	procOpenProcess           = kernel32.NewProc("OpenProcess")
	procTerminateProcess      = kernel32.NewProc("TerminateProcess")
	procGetProcessTimes       = kernel32.NewProc("GetProcessTimes")
	procQueryFullProcessImage = kernel32.NewProc("QueryFullProcessImageNameW")
	procGetProcessMemoryInfo  = kernel32.NewProc("K32GetProcessMemoryInfo")
	procGlobalMemoryStatusEx  = kernel32.NewProc("GlobalMemoryStatusEx")
	procGetProcessHandleCount = kernel32.NewProc("GetProcessHandleCount")
)

const (
	processTerminate        = 0x0001
	processQueryLimitedInfo = 0x1000
	processQueryInfo        = 0x0400
)

type ProcInfo struct {
	PID     uint32  `json:"pid"`
	PPID    uint32  `json:"ppid"`
	Name    string  `json:"name"`
	Path    string  `json:"path"`
	CPU     float64 `json:"cpu"` // percent of the whole machine since the previous sample
	Mem     int64   `json:"mem"` // working set, bytes
	Private int64   `json:"priv"`
	Threads uint32  `json:"threads"`
	Handles uint32  `json:"handles"`
	Ports   []int   `json:"ports,omitempty"` // listening TCP ports owned by this process
	Locked  bool    `json:"locked"`          // protected: the kill button is disabled
}

type PortInfo struct {
	Proto  string `json:"proto"` // TCP | UDP | TCP6 | UDP6
	Local  string `json:"local"`
	Port   int    `json:"port"`
	Remote string `json:"remote"`
	RPort  int    `json:"rport"`
	State  string `json:"state"`
	PID    uint32 `json:"pid"`
	Name   string `json:"name"`
	Locked bool   `json:"locked"`
}

type cpuSample struct {
	busy uint64 // kernel + user, 100 ns units
	at   time.Time
}

var (
	procMu  sync.Mutex
	prevCPU = map[uint32]cpuSample{}
)

// protectedNames are never terminated: killing them blue-screens or logs the user out.
var protectedNames = map[string]bool{
	"system": true, "registry": true, "smss.exe": true, "csrss.exe": true, "wininit.exe": true, "winlogon.exe": true,
	"services.exe": true, "lsass.exe": true, "lsaiso.exe": true, "memory compression": true, "secure system": true,
	"dwm.exe": true,
}

func isProtected(pid uint32, name string) bool {
	return pid == 0 || pid == 4 || pid == uint32(syscall.Getpid()) || protectedNames[strings.ToLower(name)]
}

func ft(a, b uint32) uint64 { return uint64(a)<<32 | uint64(b) }

// snapshot walks the process list with the Toolhelp API.
func snapshot() ([]syscall.ProcessEntry32, error) {
	h, err := syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return nil, err
	}
	defer syscall.CloseHandle(h)
	var out []syscall.ProcessEntry32
	e := syscall.ProcessEntry32{}
	e.Size = uint32(unsafe.Sizeof(e))
	if err := syscall.Process32First(h, &e); err != nil {
		return nil, err
	}
	for {
		out = append(out, e)
		e.Size = uint32(unsafe.Sizeof(e))
		if syscall.Process32Next(h, &e) != nil {
			break
		}
	}
	return out, nil
}

func listProcesses() []ProcInfo {
	procMu.Lock()
	defer procMu.Unlock()
	entries, err := snapshot()
	if err != nil {
		return []ProcInfo{}
	}
	now := time.Now()
	listening := map[uint32][]int{}
	for _, p := range listPorts(nil) {
		if p.State == "LISTEN" || p.Proto == "UDP" || p.Proto == "UDP6" {
			if p.Proto == "TCP" || p.Proto == "TCP6" {
				listening[p.PID] = appendUnique(listening[p.PID], p.Port)
			}
		}
	}

	out := make([]ProcInfo, 0, len(entries))
	seen := make(map[uint32]bool, len(entries))
	ncpu := float64(runtime.NumCPU())
	for _, e := range entries {
		p := ProcInfo{PID: e.ProcessID, PPID: e.ParentProcessID, Name: syscall.UTF16ToString(e.ExeFile[:]), Threads: e.Threads, Ports: listening[e.ProcessID]}
		p.Locked = isProtected(p.PID, p.Name)
		seen[p.PID] = true
		if h, _, _ := procOpenProcess.Call(processQueryLimitedInfo, 0, uintptr(e.ProcessID)); h != 0 {
			var c, ex, k, u struct{ lo, hi uint32 }
			if r, _, _ := procGetProcessTimes.Call(h, uintptr(unsafe.Pointer(&c)), uintptr(unsafe.Pointer(&ex)), uintptr(unsafe.Pointer(&k)), uintptr(unsafe.Pointer(&u))); r != 0 {
				busy := ft(k.hi, k.lo) + ft(u.hi, u.lo)
				if prev, ok := prevCPU[p.PID]; ok && now.After(prev.at) && busy >= prev.busy {
					wall := float64(now.Sub(prev.at).Nanoseconds()) / 100 // 100 ns units
					p.CPU = min(100, float64(busy-prev.busy)/wall/ncpu*100)
				}
				prevCPU[p.PID] = cpuSample{busy, now}
			}
			var m struct {
				cb, faults                                 uint32
				peakWS, ws, qpp, qp, qnpp, qnp, pf, peakPF uintptr
				priv                                       uintptr
			}
			m.cb = uint32(unsafe.Sizeof(m))
			if r, _, _ := procGetProcessMemoryInfo.Call(h, uintptr(unsafe.Pointer(&m)), uintptr(m.cb)); r != 0 {
				p.Mem, p.Private = int64(m.ws), int64(m.priv)
			}
			var hc uint32
			procGetProcessHandleCount.Call(h, uintptr(unsafe.Pointer(&hc)))
			p.Handles = hc
			var buf [1024]uint16
			n := uint32(len(buf))
			if r, _, _ := procQueryFullProcessImage.Call(h, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&n))); r != 0 {
				p.Path = syscall.UTF16ToString(buf[:n])
			}
			syscall.CloseHandle(syscall.Handle(h))
		}
		out = append(out, p)
	}
	for pid := range prevCPU { // forget processes that exited
		if !seen[pid] {
			delete(prevCPU, pid)
		}
	}
	return out
}

func appendUnique(s []int, v int) []int {
	for _, x := range s {
		if x == v {
			return s
		}
	}
	return append(s, v)
}

/* ---------- ports ---------- */

var tcpStates = map[uint32]string{1: "CLOSED", 2: "LISTEN", 3: "SYN_SENT", 4: "SYN_RCVD", 5: "ESTABLISHED", 6: "FIN_WAIT1", 7: "FIN_WAIT2",
	8: "CLOSE_WAIT", 9: "CLOSING", 10: "LAST_ACK", 11: "TIME_WAIT", 12: "DELETE_TCB"}

func extTable(proc *syscall.LazyProc, family, class uintptr) []byte {
	var size uint32
	for i := 0; i < 4; i++ {
		buf := make([]byte, size+4096)
		size = uint32(len(buf))
		r, _, _ := proc.Call(uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)), 1, family, class, 0)
		if r == 0 {
			return buf[:size]
		}
		if r != 122 { // ERROR_INSUFFICIENT_BUFFER: grow and retry
			return nil
		}
	}
	return nil
}

func netPort(b []byte, o int) int { return int(binary.BigEndian.Uint16(b[o:])) } // stored in network order

func listPorts(names map[uint32]string) []PortInfo {
	var out []PortInfo
	add := func(proto string, local net.IP, port int, remote net.IP, rport int, state string, pid uint32) {
		p := PortInfo{Proto: proto, Local: local.String(), Port: port, State: state, PID: pid, Name: names[pid]}
		if remote != nil {
			p.Remote, p.RPort = remote.String(), rport
		}
		p.Locked = isProtected(pid, p.Name)
		out = append(out, p)
	}
	if b := extTable(procGetExtendedTcpTable, 2, 5); len(b) >= 4 { // AF_INET, TCP_TABLE_OWNER_PID_ALL
		n := int(le32(b, 0))
		for i := 0; i < n && 4+(i+1)*24 <= len(b); i++ {
			r := b[4+i*24:]
			add("TCP", net.IP(r[4:8]), netPort(r, 8), net.IP(r[12:16]), netPort(r, 16), tcpStates[le32(r, 0)], le32(r, 20))
		}
	}
	if b := extTable(procGetExtendedTcpTable, 23, 5); len(b) >= 4 { // AF_INET6
		n := int(le32(b, 0))
		for i := 0; i < n && 4+(i+1)*56 <= len(b); i++ {
			r := b[4+i*56:]
			add("TCP6", net.IP(r[0:16]), netPort(r, 20), net.IP(r[24:40]), netPort(r, 44), tcpStates[le32(r, 48)], le32(r, 52))
		}
	}
	if b := extTable(procGetExtendedUdpTable, 2, 1); len(b) >= 4 { // UDP_TABLE_OWNER_PID
		n := int(le32(b, 0))
		for i := 0; i < n && 4+(i+1)*12 <= len(b); i++ {
			r := b[4+i*12:]
			add("UDP", net.IP(r[0:4]), netPort(r, 4), nil, 0, "", le32(r, 8))
		}
	}
	if b := extTable(procGetExtendedUdpTable, 23, 1); len(b) >= 4 {
		n := int(le32(b, 0))
		for i := 0; i < n && 4+(i+1)*28 <= len(b); i++ {
			r := b[4+i*28:]
			add("UDP6", net.IP(r[0:16]), netPort(r, 20), nil, 0, "", le32(r, 24))
		}
	}
	return out
}

/* ---------- kill ---------- */

func killPID(pid uint32) error {
	h, _, e := procOpenProcess.Call(processTerminate, 0, uintptr(pid))
	if h == 0 {
		if e == syscall.ERROR_ACCESS_DENIED {
			return fmt.Errorf("access denied (restart My Tool Box as administrator to end this process)")
		}
		if e == syscall.Errno(87) {
			return fmt.Errorf("process %d is no longer running", pid)
		}
		return e
	}
	defer syscall.CloseHandle(syscall.Handle(h))
	if r, _, e := procTerminateProcess.Call(h, 1); r == 0 {
		return e
	}
	return nil
}

// descendants returns pid plus every process below it, children first.
func descendants(pid uint32, entries []syscall.ProcessEntry32) []uint32 {
	kids := map[uint32][]uint32{}
	for _, e := range entries {
		if e.ParentProcessID != e.ProcessID {
			kids[e.ParentProcessID] = append(kids[e.ParentProcessID], e.ProcessID)
		}
	}
	var order []uint32
	seen := map[uint32]bool{pid: true}
	var walk func(p uint32)
	walk = func(p uint32) {
		for _, c := range kids[p] {
			if !seen[c] {
				seen[c] = true
				walk(c)
			}
		}
		order = append(order, p)
	}
	walk(pid)
	return order
}

func procNames() map[uint32]string {
	m := map[uint32]string{}
	entries, _ := snapshot()
	for _, e := range entries {
		m[e.ProcessID] = syscall.UTF16ToString(e.ExeFile[:])
	}
	return m
}

type killResult struct {
	PID   uint32 `json:"pid"`
	Name  string `json:"name"`
	OK    bool   `json:"ok"`
	Error string `json:"error,omitempty"`
}

// killTargets ends each pid (and its tree when asked), skipping protected processes. Re-checks the name at kill time.
func killTargets(pids []uint32, tree bool) []killResult {
	entries, _ := snapshot()
	names := map[uint32]string{}
	for _, e := range entries {
		names[e.ProcessID] = syscall.UTF16ToString(e.ExeFile[:])
	}
	var res []killResult
	done := map[uint32]bool{}
	for _, pid := range pids {
		list := []uint32{pid}
		if tree {
			list = descendants(pid, entries)
		}
		for _, p := range list {
			if done[p] {
				continue
			}
			done[p] = true
			r := killResult{PID: p, Name: names[p]}
			if isProtected(p, r.Name) {
				r.Error = "protected system process or My Tool Box itself"
			} else if err := killPID(p); err != nil {
				r.Error = err.Error()
			} else {
				r.OK = true
			}
			res = append(res, r)
		}
	}
	return res
}

/* ---------- HTTP ---------- */

// sameOrigin rejects cross-site requests: a web page must not be able to make this local server kill processes.
func sameOrigin(r *http.Request) bool {
	if r.Method != http.MethodPost {
		return false
	}
	o := r.Header.Get("Origin")
	return o == "" || o == "http://"+r.Host
}

func registerProc(mux *http.ServeMux) {
	mux.HandleFunc("/api/proc/list", func(w http.ResponseWriter, r *http.Request) {
		procs := listProcesses()
		var tot struct {
			Len                                       uint32
			Load                                      uint32
			Total, Avail, TotalPage, AvailPage, TotalV, AvailV uint64
			Ext                                       uint64
		}
		tot.Len = uint32(unsafe.Sizeof(tot))
		procGlobalMemoryStatusEx.Call(uintptr(unsafe.Pointer(&tot)))
		var cpu float64
		for _, p := range procs {
			cpu += p.CPU
		}
		writeJSON(w, map[string]any{"procs": procs, "cpu": min(100, cpu), "memTotal": tot.Total, "memUsed": tot.Total - tot.Avail,
			"cpus": runtime.NumCPU(), "admin": isAdmin(), "self": syscall.Getpid(), "time": time.Now().Unix()})
	})
	mux.HandleFunc("/api/proc/ports", func(w http.ResponseWriter, r *http.Request) {
		ports := listPorts(procNames())
		sort.SliceStable(ports, func(i, j int) bool {
			if ports[i].Port != ports[j].Port {
				return ports[i].Port < ports[j].Port
			}
			return ports[i].Proto < ports[j].Proto
		})
		if ports == nil {
			ports = []PortInfo{}
		}
		writeJSON(w, map[string]any{"ports": ports, "admin": isAdmin()})
	})
	mux.HandleFunc("/api/proc/kill", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var req struct {
			PIDs []uint32 `json:"pids"`
			Tree bool     `json:"tree"`
		}
		if err := decodeJSON(r, &req); err != nil || len(req.PIDs) == 0 {
			fail(w, 400, "pids required")
			return
		}
		writeJSON(w, killTargets(req.PIDs, req.Tree))
	})
	// Kill whatever owns a local port: the "free up port 3000" button.
	mux.HandleFunc("/api/proc/killport", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var req struct {
			Port int  `json:"port"`
			Tree bool `json:"tree"`
		}
		if err := decodeJSON(r, &req); err != nil || req.Port < 1 || req.Port > 65535 {
			fail(w, 400, "port 1-65535 required")
			return
		}
		var pids []uint32
		seen := map[uint32]bool{}
		for _, p := range listPorts(nil) {
			if p.Port == req.Port && p.PID != 0 && !seen[p.PID] && (p.State == "LISTEN" || p.State == "ESTABLISHED" || p.Proto == "UDP" || p.Proto == "UDP6") {
				seen[p.PID] = true
				pids = append(pids, p.PID)
			}
		}
		if len(pids) == 0 {
			fail(w, 404, fmt.Sprintf("nothing is using port %d", req.Port))
			return
		}
		writeJSON(w, killTargets(pids, req.Tree))
	})
}
