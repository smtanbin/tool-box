// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/json"
	"net/http"
	"os/exec"
	"runtime"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

// Hardware information in the style of CPU-Z: processor, mainboard and BIOS, memory modules, graphics, system and OS.
// Windows keeps all of this in WMI, which is reached through one PowerShell call (cached for a minute) so the app
// stays free of cgo. The live memory figures come straight from the Win32 API.

const hwScript = `$ErrorActionPreference='SilentlyContinue'
function D($v){ if($v){ try{ ([datetime]$v).ToString('yyyy-MM-dd') }catch{ "$v" } } }
$cpu=Get-CimInstance Win32_Processor|Select-Object Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,CurrentClockSpeed,L2CacheSize,L3CacheSize,SocketDesignation,Architecture,ProcessorId,Revision,Description,VirtualizationFirmwareEnabled
$bb=Get-CimInstance Win32_BaseBoard|Select-Object Manufacturer,Product,Version,SerialNumber
$bios=Get-CimInstance Win32_BIOS|Select-Object Manufacturer,SMBIOSBIOSVersion,Version,@{n='ReleaseDate';e={D $_.ReleaseDate}}
$cs=Get-CimInstance Win32_ComputerSystem|Select-Object Manufacturer,Model,SystemType,TotalPhysicalMemory
$mem=Get-CimInstance Win32_PhysicalMemory|Select-Object BankLabel,DeviceLocator,Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber,SerialNumber,SMBIOSMemoryType,FormFactor,ConfiguredVoltage
$gpu=Get-CimInstance Win32_VideoController|Select-Object Name,AdapterRAM,DriverVersion,VideoProcessor,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate,@{n='DriverDate';e={D $_.DriverDate}}
$os=Get-CimInstance Win32_OperatingSystem|Select-Object Caption,Version,BuildNumber,OSArchitecture,@{n='InstallDate';e={D $_.InstallDate}},@{n='LastBoot';e={ if($_.LastBootUpTime){ $_.LastBootUpTime.ToString('yyyy-MM-dd HH:mm') } }}
$vram=@{}
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'|ForEach-Object{ $p=Get-ItemProperty $_.PSPath; if($p.DriverDesc -and $p.'HardwareInformation.qwMemorySize'){ $vram[$p.DriverDesc]=[int64]$p.'HardwareInformation.qwMemorySize' } }
$cp=Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0'
$ucode=if($cp.'Update Revision'){ ($cp.'Update Revision'|ForEach-Object{'{0:X2}' -f $_}) -join '' }else{''}
@{ident="$($cp.Identifier)";ucode=$ucode;cpu=@($cpu);board=@($bb);bios=@($bios);system=@($cs);memory=@($mem);gpu=@($gpu);os=@($os);vram=$vram}|ConvertTo-Json -Depth 4 -Compress`

var (
	hwMu   sync.Mutex
	hwData json.RawMessage
	hwAt   time.Time
	hwErr  string
)

func readHardware(force bool) (json.RawMessage, string) {
	hwMu.Lock()
	defer hwMu.Unlock()
	if !force && hwData != nil && time.Since(hwAt) < time.Minute {
		return hwData, ""
	}
	cmd := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", hwScript)
	cmd.Stdin = strings.NewReader("")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	var out strings.Builder
	cmd.Stdout = &out
	if err := cmd.Start(); err != nil {
		return nil, "PowerShell could not be started: " + err.Error()
	}
	done := make(chan error, 1)
	go func() { done <- cmd.Wait() }()
	select {
	case <-done:
	case <-time.After(25 * time.Second):
		cmd.Process.Kill()
		return nil, "Reading the hardware took too long"
	}
	b := []byte(strings.TrimSpace(strings.TrimPrefix(out.String(), "\xef\xbb\xbf")))
	if len(b) == 0 || b[0] != '{' || !json.Valid(b) {
		return nil, "Windows did not return hardware information"
	}
	hwData, hwAt = b, time.Now()
	return hwData, ""
}

type memStatus struct {
	Len                                                uint32
	Load                                               uint32
	Total, Avail, TotalPage, AvailPage, TotalV, AvailV uint64
	Ext                                                uint64
}

func registerHardware(mux *http.ServeMux) {
	mux.HandleFunc("/api/hw", func(w http.ResponseWriter, r *http.Request) {
		data, msg := readHardware(r.URL.Query().Get("fresh") == "1")
		if data == nil {
			fail(w, 500, msg)
			return
		}
		var m memStatus
		m.Len = uint32(unsafe.Sizeof(m))
		procGlobalMemoryStatusEx.Call(uintptr(unsafe.Pointer(&m)))
		var extra struct {
			Ident string `json:"ident"`
			Ucode string `json:"ucode"`
			Board []struct {
				Product string `json:"Product"`
			} `json:"board"`
			CPU []struct {
				Socket string `json:"SocketDesignation"`
			} `json:"cpu"`
		}
		json.Unmarshal(data, &extra)
		cur, curMax, maxMhz := cpuClocks()
		board, socket := "", ""
		if len(extra.Board) > 0 {
			board = chipsetFromBoard(extra.Board[0].Product)
		}
		if len(extra.CPU) > 0 {
			socket = socketText(extra.CPU[0].Socket)
		}
		writeJSON(w, map[string]any{"wmi": data, "cpuid": readCPUDetail(extra.Ident), "clock": map[string]any{"current": cur, "currentMax": curMax, "max": maxMhz},
			"socket": socket, "chipset": board, "microcode": microcodeText(extra.Ucode), "live": map[string]any{"memTotal": m.Total, "memUsed": m.Total - m.Avail, "memLoad": m.Load,
				"pageTotal": m.TotalPage, "pageUsed": m.TotalPage - m.AvailPage, "goarch": runtime.GOARCH, "logical": runtime.NumCPU()}})
	})
}

/* ---------- live CPU load (Task Manager style) ---------- */

var (
	ntdll                  = syscall.NewLazyDLL("ntdll.dll")
	procNtQuerySystemInfo  = ntdll.NewProc("NtQuerySystemInformation")
	procGetPerformanceInfo = kernel32.NewProc("K32GetPerformanceInfo")
	procGetTickCount64     = kernel32.NewProc("GetTickCount64")
	cpuMu                  sync.Mutex
	cpuPrev                []cpuTimes
)

type cpuTimes struct{ idle, kernel, user int64 }

// sysProcPerf mirrors SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION (48 bytes per logical processor).
type sysProcPerf struct {
	Idle, Kernel, User, Dpc, Interrupt int64
	InterruptCount                     uint32
	_                                  uint32
}

type perfInfo struct {
	Cb                                                                                 uint32
	CommitTotal, CommitLimit, CommitPeak, PhysicalTotal                                uintptr
	PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize uintptr
	HandleCount, ProcessCount, ThreadCount                                             uint32
}

// cpuLoad returns each logical processor's busy percentage since the previous call.
func cpuLoad() []float64 {
	n := runtime.NumCPU()
	buf := make([]sysProcPerf, n)
	var got uint32
	r, _, _ := procNtQuerySystemInfo.Call(8, uintptr(unsafe.Pointer(&buf[0])), uintptr(uint32(n)*uint32(unsafe.Sizeof(buf[0]))), uintptr(unsafe.Pointer(&got)))
	if r != 0 {
		return nil
	}
	n = int(got) / int(unsafe.Sizeof(buf[0]))
	cpuMu.Lock()
	defer cpuMu.Unlock()
	cur := make([]cpuTimes, n)
	out := make([]float64, n)
	for i := 0; i < n; i++ {
		cur[i] = cpuTimes{buf[i].Idle, buf[i].Kernel, buf[i].User} // kernel time already includes idle time
		if len(cpuPrev) == n {
			dt := float64((cur[i].kernel + cur[i].user) - (cpuPrev[i].kernel + cpuPrev[i].user))
			di := float64(cur[i].idle - cpuPrev[i].idle)
			if dt > 0 {
				out[i] = min(100, max(0, (1-di/dt)*100))
			}
		}
	}
	cpuPrev = cur
	return out
}

func registerHardwareLive(mux *http.ServeMux) {
	mux.HandleFunc("/api/hw/cpu", func(w http.ResponseWriter, r *http.Request) {
		cores := cpuLoad()
		var total float64
		for _, c := range cores {
			total += c
		}
		if len(cores) > 0 {
			total /= float64(len(cores))
		}
		var pi perfInfo
		pi.Cb = uint32(unsafe.Sizeof(pi))
		procGetPerformanceInfo.Call(uintptr(unsafe.Pointer(&pi)), uintptr(pi.Cb))
		tick, _, _ := procGetTickCount64.Call()
		cur, curMax, maxMhz := cpuClocks()
		writeJSON(w, map[string]any{"cores": cores, "total": total, "speed": curMax, "core0": cur, "max": maxMhz,
			"processes": pi.ProcessCount, "threads": pi.ThreadCount, "handles": pi.HandleCount, "uptime": uint64(tick) / 1000})
	})
}
