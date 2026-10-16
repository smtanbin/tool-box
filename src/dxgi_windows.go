// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"fmt"
	"regexp"
	"runtime"
	"strings"
	"syscall"
	"unsafe"
)

// Every graphics adapter Windows knows about (discrete GPU, integrated GPU, NPU), from DXGI. WMI hides some of them
// and reports video memory wrongly above 4 GB; DXGI gives exact figures and the adapter LUID that the performance
// counters use to tell adapters apart.

var (
	dxgi                   = syscall.NewLazyDLL("dxgi.dll")
	procCreateDXGIFactory1 = dxgi.NewProc("CreateDXGIFactory1")
	iidDXGIFactory1        = guid{0x770aae78, 0xf26f, 0x4dba, [8]byte{0xa8, 0x29, 0x25, 0x3c, 0x83, 0xd1, 0xb3, 0x87}}
)

type GPUAdapter struct {
	Name      string `json:"name"`
	Vendor    string `json:"vendor"`
	VendorID  uint32 `json:"vendorId"`
	DeviceID  uint32 `json:"deviceId"`
	Dedicated uint64 `json:"dedicated"`
	Shared    uint64 `json:"shared"`
	LUID      string `json:"luid"` // 0xHHHHHHHH_0xLLLLLLLL, the same spelling the performance counters use
	Kind      string `json:"kind"` // gpu | igpu | npu | software
}

var npuName = regexp.MustCompile(`(?i)\bNPU\b|AI Boost|Neural|Hexagon|\bIPU\b|Ryzen AI|Movidius|VPU`)

var gpuVendors = map[uint32]string{0x10DE: "NVIDIA", 0x1002: "AMD", 0x1022: "AMD", 0x8086: "Intel", 0x5143: "Qualcomm", 0x1414: "Microsoft", 0x1AE0: "Google", 0x15AD: "VMware", 0x1B36: "Red Hat"}

func utf16Chars(b []byte) string {
	u := make([]uint16, 0, len(b)/2)
	for i := 0; i+1 < len(b); i += 2 {
		c := uint16(b[i]) | uint16(b[i+1])<<8
		if c == 0 {
			break
		}
		u = append(u, c)
	}
	return syscall.UTF16ToString(u)
}

// dxgiAdapters lists the adapters in the order Windows enumerates them.
func dxgiAdapters() []GPUAdapter {
	var factory unsafe.Pointer
	if r, _, _ := procCreateDXGIFactory1.Call(uintptr(unsafe.Pointer(&iidDXGIFactory1)), uintptr(unsafe.Pointer(&factory))); r != 0 || factory == nil {
		return nil
	}
	defer vcall(factory, 2) // Release
	p := int(unsafe.Sizeof(uintptr(0)))
	var out []GPUAdapter
	for i := 0; i < 16; i++ {
		var ad unsafe.Pointer
		if vcall(factory, 12, uintptr(i), uintptr(unsafe.Pointer(&ad))) != 0 || ad == nil { // IDXGIFactory1::EnumAdapters1
			break
		}
		desc := make([]byte, 320)
		hr := vcall(ad, 10, uintptr(unsafe.Pointer(&desc[0]))) // IDXGIAdapter1::GetDesc1
		runtime.KeepAlive(desc)
		if hr == 0 {
			base := 272
			g := GPUAdapter{Name: utf16Chars(desc[:256]), VendorID: le32(desc, 256), DeviceID: le32(desc, 260)}
			rd := func(o int) uint64 {
				if p == 8 {
					return le64(desc, o)
				}
				return uint64(le32(desc, o))
			}
			g.Dedicated, g.Shared = rd(base), rd(base+2*p)
			luidAt := base + 3*p
			g.LUID = fmt.Sprintf("0x%08X_0x%08X", le32(desc, luidAt+4), le32(desc, luidAt))
			flags := le32(desc, luidAt+8)
			g.Vendor = gpuVendors[g.VendorID]
			switch {
			case flags&2 != 0 || g.VendorID == 0x1414:
				g.Kind = "software"
			case npuName.MatchString(g.Name):
				g.Kind = "npu"
			case g.Dedicated < 512<<20 && g.Shared > 0 && (g.VendorID == 0x8086 || g.VendorID == 0x1002 || g.VendorID == 0x1022 || g.VendorID == 0x5143):
				g.Kind = "igpu"
			default:
				g.Kind = "gpu"
			}
			out = append(out, g)
		}
		vcall(ad, 2)
	}
	return out
}

// kindLabel is the heading the page shows for an adapter kind.
func kindLabel(k string) string {
	switch k {
	case "igpu":
		return "Integrated GPU"
	case "npu":
		return "NPU"
	case "software":
		return "Software adapter"
	}
	return "Graphics card"
}

func (g GPUAdapter) String() string {
	return strings.TrimSpace(g.Name) + " (" + kindLabel(g.Kind) + ")"
}
