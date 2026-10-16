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
@{cpu=@($cpu);board=@($bb);bios=@($bios);system=@($cs);memory=@($mem);gpu=@($gpu);os=@($os);vram=$vram}|ConvertTo-Json -Depth 4 -Compress`

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
		writeJSON(w, map[string]any{"wmi": data, "live": map[string]any{"memTotal": m.Total, "memUsed": m.Total - m.Avail, "memLoad": m.Load,
			"pageTotal": m.TotalPage, "pageUsed": m.TotalPage - m.AvailPage, "goarch": runtime.GOARCH, "logical": runtime.NumCPU()}})
	})
}
