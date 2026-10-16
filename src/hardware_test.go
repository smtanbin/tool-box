// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"runtime"
	"testing"
	"time"
)

func TestHardwareReport(t *testing.T) {
	mux := http.NewServeMux()
	registerHardware(mux)
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, httptest.NewRequest("GET", "/api/hw", nil))
	if w.Code != 200 {
		t.Skipf("WMI not available here: %d %s", w.Code, w.Body.String())
	}
	var r struct {
		WMI struct {
			CPU    []map[string]any `json:"cpu"`
			Board  []map[string]any `json:"board"`
			Memory []map[string]any `json:"memory"`
			GPU    []map[string]any `json:"gpu"`
			OS     []map[string]any `json:"os"`
		} `json:"wmi"`
		Live struct {
			MemTotal uint64 `json:"memTotal"`
			MemUsed  uint64 `json:"memUsed"`
		} `json:"live"`
	}
	if err := json.Unmarshal(w.Body.Bytes(), &r); err != nil {
		t.Fatal(err)
	}
	if len(r.WMI.CPU) == 0 || r.WMI.CPU[0]["Name"] == nil {
		t.Errorf("no processor: %+v", r.WMI.CPU)
	}
	if len(r.WMI.OS) == 0 || len(r.WMI.GPU) == 0 {
		t.Errorf("os/gpu missing: os=%d gpu=%d", len(r.WMI.OS), len(r.WMI.GPU))
	}
	if r.Live.MemTotal == 0 || r.Live.MemUsed == 0 || r.Live.MemUsed > r.Live.MemTotal {
		t.Errorf("live memory: %+v", r.Live)
	}
	t.Logf("CPU=%v board=%d modules=%d gpus=%d", r.WMI.CPU[0]["Name"], len(r.WMI.Board), len(r.WMI.Memory), len(r.WMI.GPU))
}

func TestCPUDetailAndHelpers(t *testing.T) {
	if got := microcodeText("0E12200A"); got != "0xA20120E" {
		t.Errorf("microcode = %s", got)
	}
	if got := chipsetFromBoard("B550M AORUS ELITE AX"); got != "B550" {
		t.Errorf("chipset = %q", got)
	}
	if got := socketText("AM4"); got != "Socket AM4 (1331)" {
		t.Errorf("socket = %q", got)
	}
	if got := memMaker("029E"); got != "Corsair" {
		t.Errorf("maker = %q", got)
	}
	c, ok := decodeCache(0x00004121, 0x01C0003F, 0x0000003F, 16) // 32 KB, 8-way L1 data shared by 2 threads
	if !ok || c.Name != "L1 Data" || c.Size != 32*1024 || c.Ways != 8 || c.Count != 8 {
		t.Errorf("cache = %+v", c)
	}
	if name, tech := lookupCodename("AuthenticAMD", 25, 33); name != "Vermeer" || tech != "7 nm" {
		t.Errorf("codename = %s %s", name, tech)
	}
	d := readCPUDetail("AMD64 Family 25 Model 33 Stepping 2")
	if !d.Supported {
		t.Skip("CPUID is not available in this build")
	}
	if d.Vendor == "" || d.Brand == "" || d.Instruction == "" || len(d.Caches) < 3 {
		t.Errorf("cpuid detail incomplete: %+v", d)
	}
	t.Logf("%s | %s | fam %s model %s ext %s/%s step %d | %s %s | %s", d.Vendor, d.Brand, d.Family, d.Model, d.ExtFamily, d.ExtModel, d.Stepping, d.Codename, d.Tech, d.Instruction)
	for _, c := range d.Caches {
		t.Logf("  %s %d x %d KB %d-way", c.Name, c.Count, c.Size/1024, c.Ways)
	}
	cur, curMax, max := cpuClocks()
	t.Logf("clocks cur=%d curMax=%d max=%d", cur, curMax, max)
	if max == 0 {
		t.Error("power API returned no clock")
	}
}

func TestLiveCPU(t *testing.T) {
	mux := http.NewServeMux()
	registerHardwareLive(mux)
	get := func() (r struct {
		Cores     []float64 `json:"cores"`
		Total     float64   `json:"total"`
		Processes int       `json:"processes"`
		Threads   int       `json:"threads"`
		Handles   int       `json:"handles"`
		Uptime    int       `json:"uptime"`
	}) {
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, httptest.NewRequest("GET", "/api/hw/cpu", nil))
		if err := json.Unmarshal(w.Body.Bytes(), &r); err != nil {
			t.Fatal(err)
		}
		return
	}
	get()
	// burn a little CPU so the second sample has something to measure
	end := time.Now().Add(300 * time.Millisecond)
	for time.Now().Before(end) {
	}
	r := get()
	if len(r.Cores) != runtime.NumCPU() {
		t.Errorf("want %d cores, got %d", runtime.NumCPU(), len(r.Cores))
	}
	for _, c := range r.Cores {
		if c < 0 || c > 100 {
			t.Errorf("core load out of range: %v", c)
		}
	}
	if r.Processes < 10 || r.Threads < 50 || r.Handles < 500 || r.Uptime < 10 {
		t.Errorf("system counters look wrong: %+v", r)
	}
	t.Logf("total %.1f%% procs %d threads %d handles %d uptime %ds", r.Total, r.Processes, r.Threads, r.Handles, r.Uptime)
}

func TestDXGIAdapters(t *testing.T) {
	list := dxgiAdapters()
	if len(list) == 0 {
		t.Skip("no DXGI adapters here")
	}
	real := 0
	for _, a := range list {
		t.Logf("%s | vendor %s | dedicated %d MB shared %d MB | luid %s | %s", a.Name, a.Vendor, a.Dedicated>>20, a.Shared>>20, a.LUID, a.Kind)
		if a.Name == "" || a.LUID == "" {
			t.Errorf("incomplete adapter: %+v", a)
		}
		if a.Kind != "software" {
			real++
		}
	}
	if real == 0 {
		t.Error("only software adapters found")
	}
}

func TestPerfCounters(t *testing.T) {
	s := readPerf()
	t.Logf("gpu instances: %d, network interfaces: %d", len(s.GPU), len(s.Net))
	for _, g := range s.GPU {
		t.Logf("  luid %s util %.1f%% dedicated %.0f MB engines %v", g.LUID, g.Util, g.Dedicated/1048576, g.Engines)
		if g.LUID == "" || g.Util < 0 || g.Util > 100 {
			t.Errorf("bad gpu sample %+v", g)
		}
	}
	for _, n := range s.Net {
		t.Logf("  net %s rx %.0f tx %.0f B/s link %.0f", n.Name, n.Rx, n.Tx, n.Bps)
	}
	if len(s.Net) == 0 {
		t.Error("no network counters")
	}
	// the counters and DXGI must agree on how a LUID is spelled
	ids := map[string]bool{}
	for _, a := range dxgiAdapters() {
		ids[a.LUID] = true
	}
	for _, g := range s.GPU {
		if !ids[g.LUID] {
			t.Logf("counter luid %s has no DXGI adapter (likely a process-less or removed adapter)", g.LUID)
		}
	}
}
