// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"syscall"
	"unsafe"
)

// osAccent returns the Windows accent colour as #RRGGBB, or Windows blue when it cannot be read.
// The browser's CSS AccentColor does not follow the Windows setting, so the Black skin uses this instead.
func osAccent() string {
	const fallback = "#0078d4"
	var h syscall.Handle
	key, _ := syscall.UTF16PtrFromString(`Software\Microsoft\Windows\DWM`)
	if syscall.RegOpenKeyEx(syscall.HKEY_CURRENT_USER, key, 0, syscall.KEY_READ, &h) != nil {
		return fallback
	}
	defer syscall.RegCloseKey(h)
	var v, typ uint32
	n := uint32(4)
	name, _ := syscall.UTF16PtrFromString("AccentColor")
	if syscall.RegQueryValueEx(h, name, nil, &typ, (*byte)(unsafe.Pointer(&v)), &n) != nil || typ != syscall.REG_DWORD {
		return fallback
	}
	return accentHex(v)
}

// accentHex turns the registry's 0xAABBGGRR value into #RRGGBB.
func accentHex(v uint32) string {
	return fmt.Sprintf("#%02x%02x%02x", v&0xff, (v>>8)&0xff, (v>>16)&0xff)
}

const accentPlaceholder = `/*OSACCENT*/""`

func injectAccent(page []byte) []byte {
	b, _ := json.Marshal(osAccent())
	return bytes.Replace(page, []byte(accentPlaceholder), b, 1)
}
