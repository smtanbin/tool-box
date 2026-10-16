// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"runtime"
	"syscall"
	"unsafe"
)

var procCallNtPowerInformation = syscall.NewLazyDLL("powrprof.dll").NewProc("CallNtPowerInformation")

type procPower struct{ Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdle, CurIdle uint32 }

// cpuClocks returns the clock of the first logical processor, the fastest current clock, and the rated maximum, in MHz,
// from Windows' power manager (no driver needed).
func cpuClocks() (cur, curMax, max uint32) {
	n := runtime.NumCPU()
	buf := make([]procPower, n)
	r, _, _ := procCallNtPowerInformation.Call(11, 0, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(uint32(n)*uint32(unsafe.Sizeof(buf[0]))))
	if r != 0 {
		return
	}
	cur = buf[0].CurrentMhz
	for _, p := range buf {
		if p.CurrentMhz > curMax {
			curMax = p.CurrentMhz
		}
		if p.MaxMhz > max {
			max = p.MaxMhz
		}
	}
	return
}
