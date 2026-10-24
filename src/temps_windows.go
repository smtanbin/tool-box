// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"fmt"
	"net/http"
	"os/exec"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
)

// Temperatures that Windows hands to normal apps: ACPI thermal zones (performance counters, usually the CPU / board
// area), disks (the storage temperature property) and NVIDIA GPUs (nvidia-smi, installed with the driver). Exact CPU
// core and AMD / Intel GPU sensors need a kernel driver, so they are not available here.

type TempReading struct {
	Group string  `json:"group"` // Thermal zone | Disk | GPU
	Name  string  `json:"name"`
	C     float64 `json:"c"`
	Warn  float64 `json:"warn,omitempty"`
	Crit  float64 `json:"crit,omitempty"`
}

var (
	tempMu   sync.Mutex
	tempLast []TempReading
	tempAt   time.Time
)

func nvidiaTemps() []TempReading {
	cmd := exec.Command("nvidia-smi", "--query-gpu=name,temperature.gpu,temperature.gpu.tlimit", "--format=csv,noheader,nounits")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	type res struct {
		b   []byte
		err error
	}
	ch := make(chan res, 1)
	go func() { b, err := cmd.Output(); ch <- res{b, err} }()
	var r res
	select {
	case r = <-ch:
	case <-time.After(3 * time.Second):
		if cmd.Process != nil {
			cmd.Process.Kill()
		}
		return nil
	}
	if r.err != nil {
		return nil
	}
	var out []TempReading
	for _, line := range strings.Split(string(r.b), "\n") {
		f := strings.Split(strings.TrimSpace(line), ",")
		if len(f) < 2 {
			continue
		}
		t, err := strconv.ParseFloat(strings.TrimSpace(f[1]), 64)
		if err != nil {
			continue
		}
		out = append(out, TempReading{Group: "GPU", Name: strings.TrimSpace(f[0]), C: t})
	}
	return out
}

func diskTemps() []TempReading {
	var out []TempReading
	for i := 0; i < 32; i++ {
		h, err := openDev(fmt.Sprintf(`\\.\PhysicalDrive%d`, i), 0)
		if err != nil {
			continue
		}
		d := newDisk(i)
		readDescriptor(h, d)
		readTemperature(h, d)
		syscall.CloseHandle(h)
		if d.Temp > 0 {
			out = append(out, TempReading{Group: "Disk", Name: strings.TrimSpace(d.Vendor + " " + d.Model), C: float64(d.Temp), Warn: float64(d.TempWarn), Crit: float64(d.TempCrit)})
		}
	}
	return out
}

func readTemps() []TempReading {
	tempMu.Lock()
	defer tempMu.Unlock()
	if tempLast != nil && time.Since(tempAt) < 1500*time.Millisecond {
		return tempLast
	}
	out := []TempReading{}
	s := pdhSamples([]string{`\Thermal Zone Information(*)\Temperature`}, 50*time.Millisecond)
	names := make([]string, 0, len(s[0]))
	for n := range s[0] {
		names = append(names, n)
	}
	sort.Strings(names)
	for _, n := range names {
		if k := s[0][n] - 273.15; k > -50 && k < 200 {
			label := strings.TrimPrefix(strings.ReplaceAll(n, `\_TZ.`, ""), `\`)
			out = append(out, TempReading{Group: "Thermal zone", Name: label, C: k})
		}
	}
	out = append(out, diskTemps()...)
	out = append(out, nvidiaTemps()...)
	tempLast, tempAt = out, time.Now()
	return out
}

func registerTemps(mux *http.ServeMux) {
	mux.HandleFunc("/api/hw/temps", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, map[string]any{"temps": readTemps()})
	})
}
