// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"net/http"
	"regexp"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

// Live GPU / NPU load, video memory and network throughput from Windows' performance counters (PDH), the same
// source Task Manager uses. Wildcard counters only cover instances that exist when they are added, so each sample
// opens a fresh query, takes two readings 250 ms apart and closes it.

var (
	pdh                = syscall.NewLazyDLL("pdh.dll")
	pdhOpenQuery       = pdh.NewProc("PdhOpenQueryW")
	pdhAddCounter      = pdh.NewProc("PdhAddEnglishCounterW")
	pdhCollect         = pdh.NewProc("PdhCollectQueryData")
	pdhGetFormattedArr = pdh.NewProc("PdhGetFormattedCounterArrayW")
	pdhCloseQuery      = pdh.NewProc("PdhCloseQuery")
)

const pdhMoreData = 0x800007D2

type pdhItem struct {
	Name   *uint16
	Status uint32
	Value  float64
}

// pdhSample reads one wildcard counter twice (rates need two points) and returns instance -> value.
func pdhSamples(paths []string, gap time.Duration) []map[string]float64 {
	var q uintptr
	if r, _, _ := pdhOpenQuery.Call(0, 0, uintptr(unsafe.Pointer(&q))); r != 0 {
		return make([]map[string]float64, len(paths))
	}
	defer pdhCloseQuery.Call(q)
	counters := make([]uintptr, len(paths))
	for i, p := range paths {
		pp, _ := syscall.UTF16PtrFromString(p)
		if r, _, _ := pdhAddCounter.Call(q, uintptr(unsafe.Pointer(pp)), 0, uintptr(unsafe.Pointer(&counters[i]))); r != 0 {
			counters[i] = 0
		}
	}
	pdhCollect.Call(q)
	time.Sleep(gap)
	pdhCollect.Call(q)
	out := make([]map[string]float64, len(paths))
	for i, c := range counters {
		out[i] = map[string]float64{}
		if c == 0 {
			continue
		}
		var size, count uint32
		r, _, _ := pdhGetFormattedArr.Call(c, 0x200, uintptr(unsafe.Pointer(&size)), uintptr(unsafe.Pointer(&count)), 0) // PDH_FMT_DOUBLE
		if uint32(r) != pdhMoreData || size == 0 {
			continue
		}
		buf := make([]byte, size)
		if r, _, _ := pdhGetFormattedArr.Call(c, 0x200, uintptr(unsafe.Pointer(&size)), uintptr(unsafe.Pointer(&count)), uintptr(unsafe.Pointer(&buf[0]))); r != 0 {
			continue
		}
		items := unsafe.Slice((*pdhItem)(unsafe.Pointer(&buf[0])), int(count))
		for _, it := range items {
			name := syscall.UTF16ToString((*[1 << 12]uint16)(unsafe.Pointer(it.Name))[:])
			out[i][name] += it.Value
		}
	}
	return out
}

var (
	luidRe    = regexp.MustCompile(`luid_(0x[0-9a-fA-F]+_0x[0-9a-fA-F]+)`)
	engTypeRe = regexp.MustCompile(`engtype_([A-Za-z0-9_ ]+)$`)
)

type GPULoad struct {
	LUID      string             `json:"luid"`
	Util      float64            `json:"util"`    // busiest engine type, like Task Manager
	Engines   map[string]float64 `json:"engines"` // per engine type
	Dedicated float64            `json:"dedicated"`
	Shared    float64            `json:"shared"`
}

type NetLoad struct {
	Name string  `json:"name"`
	Rx   float64 `json:"rx"` // bytes per second
	Tx   float64 `json:"tx"`
	Bps  float64 `json:"bps"` // link speed, bits per second
}

type perfSnapshot struct {
	GPU []GPULoad `json:"gpu"`
	Net []NetLoad `json:"net"`
}

var (
	perfMu   sync.Mutex
	perfLast perfSnapshot
	perfAt   time.Time
)

func readPerf() perfSnapshot {
	perfMu.Lock()
	defer perfMu.Unlock()
	if time.Since(perfAt) < 700*time.Millisecond {
		return perfLast
	}
	s := pdhSamples([]string{
		`\GPU Engine(*)\Utilization Percentage`,
		`\GPU Adapter Memory(*)\Dedicated Usage`,
		`\GPU Adapter Memory(*)\Shared Usage`,
		`\Network Interface(*)\Bytes Received/sec`,
		`\Network Interface(*)\Bytes Sent/sec`,
		`\Network Interface(*)\Current Bandwidth`,
	}, 250*time.Millisecond)
	byLUID := map[string]*GPULoad{}
	get := func(l string) *GPULoad {
		g := byLUID[l]
		if g == nil {
			g = &GPULoad{LUID: l, Engines: map[string]float64{}}
			byLUID[l] = g
		}
		return g
	}
	for name, v := range s[0] {
		m := luidRe.FindStringSubmatch(name)
		if m == nil {
			continue
		}
		g := get(canonLUID(m[1]))
		et := "Other"
		if e := engTypeRe.FindStringSubmatch(name); e != nil {
			et = e[1]
		}
		g.Engines[et] += v
	}
	for name, v := range s[1] {
		if m := luidRe.FindStringSubmatch(name); m != nil {
			get(canonLUID(m[1])).Dedicated += v
		}
	}
	for name, v := range s[2] {
		if m := luidRe.FindStringSubmatch(name); m != nil {
			get(canonLUID(m[1])).Shared += v
		}
	}
	snap := perfSnapshot{GPU: []GPULoad{}, Net: []NetLoad{}}
	for _, g := range byLUID {
		for k, v := range g.Engines {
			if v > 100 {
				g.Engines[k] = 100
				v = 100
			}
			if v > g.Util {
				g.Util = v
			}
		}
		snap.GPU = append(snap.GPU, *g)
	}
	for name, rx := range s[3] {
		snap.Net = append(snap.Net, NetLoad{Name: name, Rx: rx, Tx: s[4][name], Bps: s[5][name]})
	}
	perfLast, perfAt = snap, time.Now()
	return snap
}

// canonLUID spells a luid like the DXGI side does: 0xHHHHHHHH_0xLLLLLLLL with upper-case digits.
func canonLUID(s string) string {
	parts := strings.Split(strings.ToUpper(s), "_")
	for i, p := range parts {
		parts[i] = "0x" + strings.TrimPrefix(p, "0X")
	}
	return strings.Join(parts, "_")
}

func registerPerf(mux *http.ServeMux) {
	mux.HandleFunc("/api/hw/perf", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, readPerf())
	})
	mux.HandleFunc("/api/hw/gpus", func(w http.ResponseWriter, r *http.Request) {
		a := dxgiAdapters()
		if a == nil {
			a = []GPUAdapter{}
		}
		writeJSON(w, map[string]any{"adapters": a})
	})
}
