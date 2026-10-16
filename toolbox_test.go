// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"syscall"
	"testing"
)

func TestClassifyDisk(t *testing.T) {
	cases := []struct {
		name           string
		d              DiskInfo
		dram, maker    string
		controllerHas  string
	}{
		{"BX500 is DRAM-less", DiskInfo{Model: "CT500BX500SSD1", Media: "SSD", Bus: "SATA"}, "dramless", "Crucial (Micron)", "SM2259XT"},
		{"MX500 has DRAM", DiskInfo{Model: "CT500MX500SSD1", Media: "SSD", Bus: "SATA"}, "dram", "Crucial (Micron)", "SM2258"},
		{"980 PRO has DRAM", DiskInfo{Model: "Samsung SSD 980 PRO 1TB", Media: "SSD", Bus: "NVMe"}, "dram", "Samsung", "Elpis"},
		{"980 is DRAM-less", DiskInfo{Model: "Samsung SSD 980 1TB", Media: "SSD", Bus: "NVMe"}, "dramless", "Samsung", "Pablo"},
		{"unknown NVMe with HMB", DiskInfo{Model: "Teutons Osmium M.2 NVMe SSD 256GB", Media: "SSD", Bus: "NVMe", hasIdentify: true, PCIVendor: 0x1ED0, HMBBytes: 32 << 20}, "dramless-likely", "Teutons", ""},
		{"unknown NVMe without HMB", DiskInfo{Model: "Foo NVMe", Media: "SSD", Bus: "NVMe", hasIdentify: true, PCIVendor: 0x1987}, "dram-likely", "Foo", ""},
		{"unknown SATA SSD", DiskInfo{Model: "Foo SATA SSD", Media: "SSD", Bus: "SATA"}, "unknown", "Foo", ""},
		{"HDD", DiskInfo{Model: "WDC WD10EZEX-00MFCA0", Media: "HDD", Bus: "SATA"}, "n/a", "Western Digital", ""},
		{"known DRAM-less plus HMB", DiskInfo{Model: "WD Blue SN570 500GB", Media: "SSD", Bus: "NVMe", hasIdentify: true, PCIVendor: 0x15B7, HMBBytes: 64 << 20}, "dramless", "Western Digital", "SanDisk"},
	}
	for _, c := range cases {
		d := c.d
		classifyDisk(&d)
		if d.DRAM != c.dram {
			t.Errorf("%s: DRAM = %q, want %q (%s)", c.name, d.DRAM, c.dram, d.DRAMNote)
		}
		if d.Maker != c.maker {
			t.Errorf("%s: Maker = %q, want %q", c.name, d.Maker, c.maker)
		}
		if c.controllerHas != "" && !strings.Contains(d.Controller, c.controllerHas) {
			t.Errorf("%s: Controller = %q, want it to contain %q", c.name, d.Controller, c.controllerHas)
		}
	}
}

func TestControllerVendorFromPCIID(t *testing.T) {
	d := DiskInfo{Model: "Some Drive", Media: "SSD", Bus: "NVMe", hasIdentify: true, PCIVendor: 0x126F}
	classifyDisk(&d)
	if d.ControllerVendor != "Silicon Motion" {
		t.Errorf("ControllerVendor = %q", d.ControllerVendor)
	}
}

func TestProtectedProcesses(t *testing.T) {
	for _, n := range []string{"csrss.exe", "LSASS.EXE", "System", "wininit.exe"} {
		if !isProtected(1234, n) {
			t.Errorf("%s should be protected", n)
		}
	}
	for _, pid := range []uint32{0, 4, uint32(syscall.Getpid())} {
		if !isProtected(pid, "anything.exe") {
			t.Errorf("pid %d should be protected", pid)
		}
	}
	if isProtected(99999, "notepad.exe") {
		t.Error("notepad.exe must not be protected")
	}
}

func TestDescendants(t *testing.T) {
	mk := func(pid, ppid uint32) syscall.ProcessEntry32 {
		return syscall.ProcessEntry32{ProcessID: pid, ParentProcessID: ppid}
	}
	got := descendants(10, []syscall.ProcessEntry32{mk(10, 1), mk(11, 10), mk(12, 11), mk(13, 10), mk(20, 1), mk(5, 5)})
	want := map[uint32]bool{10: true, 11: true, 12: true, 13: true}
	if len(got) != len(want) {
		t.Fatalf("got %v", got)
	}
	for _, p := range got {
		if !want[p] {
			t.Errorf("unexpected pid %d", p)
		}
	}
	if got[len(got)-1] != 10 {
		t.Errorf("parent must come last, got %v", got)
	}
}

func TestListProcessesAndPorts(t *testing.T) {
	procs := listProcesses()
	self := uint32(syscall.Getpid())
	found := false
	for _, p := range procs {
		if p.PID == self {
			found = true
			if !p.Locked {
				t.Error("own process must be locked")
			}
			if p.Mem <= 0 {
				t.Error("own working set should be reported")
			}
		}
	}
	if !found {
		t.Fatal("own process missing from list")
	}
	ports := listPorts(procNames())
	if ports == nil {
		t.Log("no ports reported (allowed)")
	}
}

func TestKillRefusesProtected(t *testing.T) {
	res := killTargets([]uint32{4, uint32(syscall.Getpid())}, false)
	for _, r := range res {
		if r.OK || r.Error == "" {
			t.Errorf("pid %d must be refused: %+v", r.PID, r)
		}
	}
}

func TestSameOrigin(t *testing.T) {
	mk := func(method, origin string) *http.Request {
		r := httptest.NewRequest(method, "http://127.0.0.1:8765/api/proc/kill", nil)
		if origin != "" {
			r.Header.Set("Origin", origin)
		}
		return r
	}
	if !sameOrigin(mk("POST", "")) || !sameOrigin(mk("POST", "http://127.0.0.1:8765")) {
		t.Error("same-origin POST should pass")
	}
	if sameOrigin(mk("GET", "")) || sameOrigin(mk("POST", "http://evil.example")) {
		t.Error("GET and cross-origin POST must be rejected")
	}
}

func TestNormalizeHosts(t *testing.T) {
	if got := normalizeHosts("127.0.0.1 a\n\n# x\r\n"); got != "127.0.0.1 a\r\n\r\n# x\r\n" {
		t.Errorf("got %q", got)
	}
	if got := normalizeHosts(""); got != "\r\n" {
		t.Errorf("empty = %q", got)
	}
}

func TestHostsPathUnderSystemRoot(t *testing.T) {
	if p := hostsPath(); !strings.HasSuffix(strings.ToLower(p), `drivers\etc\hosts`) {
		t.Errorf("path = %s", p)
	}
	if _, err := os.Stat(hostsPath()); err != nil {
		t.Logf("hosts file not readable: %v", err)
	}
}

func TestHostsSaveRejectsCrossOrigin(t *testing.T) {
	mux := http.NewServeMux()
	registerHosts(mux)
	r := httptest.NewRequest("POST", "http://127.0.0.1:1/api/hosts/save", strings.NewReader(`{"text":"x"}`))
	r.Header.Set("Origin", "http://evil.example")
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, r)
	if w.Code != 403 {
		t.Errorf("status = %d, want 403", w.Code)
	}
}

func TestEmbeddedWebFiles(t *testing.T) {
	for _, f := range []string{"index.html", "tools.js", "proc.js", "image.js", "hosts.js", "sysnet.js", "env.js", "boot.js"} {
		b, err := webFS.ReadFile("web/" + f)
		if err != nil || len(b) == 0 {
			t.Errorf("web/%s missing or empty: %v", f, err)
		}
	}
}
