// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

// Environment variables and PATH editor. Reads and writes the registry directly so the value type
// (plain or expandable, e.g. %SystemRoot%\system32) is kept, then tells running programs about the change.
// User variables need no special rights; system variables need Administrator.

var (
	advapi32          = syscall.NewLazyDLL("advapi32.dll")
	procRegEnumValue  = advapi32.NewProc("RegEnumValueW")
	procRegSetValueEx = advapi32.NewProc("RegSetValueExW")
	procRegDelValue   = advapi32.NewProc("RegDeleteValueW")
	procSendMsgTO     = user32.NewProc("SendMessageTimeoutW")
)

const (
	regSZ       = 1
	regExpandSZ = 2
	keyRead     = 0x20019
	keyWrite    = 0x20006 // KEY_SET_VALUE | KEY_CREATE_SUB_KEY | READ_CONTROL
)

type EnvVar struct {
	Name   string `json:"name"`
	Value  string `json:"value"`
	Expand bool   `json:"expand"` // REG_EXPAND_SZ: contains %OTHER% references
}

const sysEnvKey = `SYSTEM\CurrentControlSet\Control\Session Manager\Environment`

func envKey(scope string) (syscall.Handle, string, error) {
	root, sub := syscall.Handle(syscall.HKEY_CURRENT_USER), "Environment" // typed, so the 0x8000000x keys fit a 32-bit build
	switch scope {
	case "user":
	case "system":
		root, sub = syscall.Handle(syscall.HKEY_LOCAL_MACHINE), sysEnvKey
	default:
		return 0, "", fmt.Errorf("scope must be user or system")
	}
	return root, sub, nil
}

func openEnv(scope string, access uint32) (syscall.Handle, error) {
	root, sub, err := envKey(scope)
	if err != nil {
		return 0, err
	}
	var h syscall.Handle
	p, _ := syscall.UTF16PtrFromString(sub)
	if e := syscall.RegOpenKeyEx(root, p, 0, access, &h); e != nil {
		if e == syscall.ERROR_ACCESS_DENIED {
			return 0, fmt.Errorf("Access denied: restart My Tool Box as administrator to change system variables")
		}
		return 0, e
	}
	return h, nil
}

func readEnv(scope string) ([]EnvVar, error) {
	h, err := openEnv(scope, keyRead)
	if err != nil {
		return nil, err
	}
	defer syscall.RegCloseKey(h)
	out := []EnvVar{}
	for i := uint32(0); ; i++ {
		name := make([]uint16, 16384)
		nl := uint32(len(name))
		var typ, dl uint32
		r, _, _ := procRegEnumValue.Call(uintptr(h), uintptr(i), uintptr(unsafe.Pointer(&name[0])), uintptr(unsafe.Pointer(&nl)), 0, uintptr(unsafe.Pointer(&typ)), 0, uintptr(unsafe.Pointer(&dl)))
		if r == 259 { // ERROR_NO_MORE_ITEMS
			break
		}
		if r != 0 && r != 234 { // 234 = more data (we only asked for the size)
			return nil, syscall.Errno(r)
		}
		n := syscall.UTF16ToString(name[:nl])
		if typ != regSZ && typ != regExpandSZ {
			continue // binary / number values are not environment strings
		}
		buf := make([]uint16, dl/2+2)
		sz := uint32(len(buf) * 2)
		np, _ := syscall.UTF16PtrFromString(n)
		if e := syscall.RegQueryValueEx(h, np, nil, &typ, (*byte)(unsafe.Pointer(&buf[0])), &sz); e != nil {
			continue
		}
		out = append(out, EnvVar{Name: n, Value: syscall.UTF16ToString(buf), Expand: typ == regExpandSZ})
	}
	sort.Slice(out, func(i, j int) bool { return strings.ToLower(out[i].Name) < strings.ToLower(out[j].Name) })
	return out, nil
}

func validEnvName(n string) bool {
	return n != "" && len(n) <= 255 && !strings.ContainsAny(n, "=\x00") && n == strings.TrimSpace(n)
}

func writeEnv(scope, name, value string, expand bool) error {
	if !validEnvName(name) {
		return fmt.Errorf("invalid variable name")
	}
	h, err := openEnv(scope, keyWrite)
	if err != nil {
		return err
	}
	defer syscall.RegCloseKey(h)
	typ := uint32(regSZ)
	if expand {
		typ = regExpandSZ
	}
	np, _ := syscall.UTF16PtrFromString(name)
	vu, _ := syscall.UTF16FromString(value)
	if r, _, _ := procRegSetValueEx.Call(uintptr(h), uintptr(unsafe.Pointer(np)), 0, uintptr(typ), uintptr(unsafe.Pointer(&vu[0])), uintptr(len(vu)*2)); r != 0 {
		return syscall.Errno(r)
	}
	broadcastEnvChange()
	return nil
}

func deleteEnv(scope, name string) error {
	h, err := openEnv(scope, keyWrite)
	if err != nil {
		return err
	}
	defer syscall.RegCloseKey(h)
	np, _ := syscall.UTF16PtrFromString(name)
	if r, _, _ := procRegDelValue.Call(uintptr(h), uintptr(unsafe.Pointer(np))); r != 0 {
		return syscall.Errno(r)
	}
	broadcastEnvChange()
	return nil
}

// broadcastEnvChange tells Explorer and other programs to re-read the environment (WM_SETTINGCHANGE).
func broadcastEnvChange() {
	s, _ := syscall.UTF16PtrFromString("Environment")
	var res uintptr
	procSendMsgTO.Call(0xFFFF, 0x1A, 0, uintptr(unsafe.Pointer(s)), 0x2, 3000, uintptr(unsafe.Pointer(&res)))
}

// backupEnv saves every variable (both scopes) to %LocalAppData%\MyToolBox before a change, so a bad edit can be undone by hand.
func backupEnv() string {
	dir := os.Getenv("LocalAppData")
	if dir == "" {
		return ""
	}
	dir = filepath.Join(dir, "MyToolBox", "env-backups")
	if os.MkdirAll(dir, 0o755) != nil {
		return ""
	}
	user, _ := readEnv("user")
	sys, _ := readEnv("system")
	b, _ := json.MarshalIndent(map[string]any{"saved": time.Now().Format(time.RFC3339), "user": user, "system": sys}, "", "  ")
	p := filepath.Join(dir, "env-"+time.Now().Format("20060102-150405")+".json")
	if os.WriteFile(p, b, 0o644) != nil {
		return ""
	}
	// keep the newest 30
	if ents, err := os.ReadDir(dir); err == nil && len(ents) > 30 {
		for _, e := range ents[:len(ents)-30] {
			os.Remove(filepath.Join(dir, e.Name()))
		}
	}
	return p
}

var pctVar = regexp.MustCompile(`%([^%]+)%`)

// expandPct expands %NAME% references with the current environment (unknown names stay as they are).
func expandPct(s string) string {
	return pctVar.ReplaceAllStringFunc(s, func(m string) string {
		if v, ok := os.LookupEnv(m[1 : len(m)-1]); ok {
			return v
		}
		return m
	})
}

type PathCheck struct {
	Expanded string `json:"expanded"`
	Exists   bool   `json:"exists"`
	Dir      bool   `json:"dir"`
}

func checkPaths(paths []string) []PathCheck {
	out := make([]PathCheck, len(paths))
	for i, p := range paths {
		e := expandPct(strings.Trim(strings.TrimSpace(p), `"`))
		out[i].Expanded = e
		if st, err := os.Stat(e); err == nil {
			out[i].Exists, out[i].Dir = true, st.IsDir()
		}
	}
	return out
}

func registerEnv(mux *http.ServeMux) {
	mux.HandleFunc("/api/env", func(w http.ResponseWriter, r *http.Request) {
		user, err := readEnv("user")
		if err != nil {
			fail(w, 500, err.Error())
			return
		}
		sys, err := readEnv("system")
		if err != nil {
			fail(w, 500, err.Error())
			return
		}
		writeJSON(w, map[string]any{"user": user, "system": sys, "admin": isAdmin()})
	})
	mux.HandleFunc("/api/env/set", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var q struct {
			Scope, Name, Value string
			Expand             bool
			Rename             string // old name when a variable is renamed
		}
		if err := decodeJSON(r, &q); err != nil {
			fail(w, 400, err.Error())
			return
		}
		backup := backupEnv()
		if err := writeEnv(q.Scope, q.Name, q.Value, q.Expand); err != nil {
			fail(w, 403, err.Error())
			return
		}
		if q.Rename != "" && q.Rename != q.Name {
			deleteEnv(q.Scope, q.Rename)
		}
		writeJSON(w, map[string]string{"backup": backup})
	})
	mux.HandleFunc("/api/env/delete", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var q struct{ Scope, Name string }
		if err := decodeJSON(r, &q); err != nil || !validEnvName(q.Name) {
			fail(w, 400, "scope and name required")
			return
		}
		backup := backupEnv()
		if err := deleteEnv(q.Scope, q.Name); err != nil {
			fail(w, 403, err.Error())
			return
		}
		writeJSON(w, map[string]string{"backup": backup})
	})
	mux.HandleFunc("/api/env/pathcheck", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var q struct{ Paths []string }
		if err := decodeJSON(r, &q); err != nil {
			fail(w, 400, err.Error())
			return
		}
		writeJSON(w, checkPaths(q.Paths))
	})
}
