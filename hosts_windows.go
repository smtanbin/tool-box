// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
)

// Hosts file editor: read, save (with a timestamped backup next to it) and flush the DNS cache.
// Writing needs Administrator; the page offers the same restart button as Disk info.

func hostsPath() string {
	root := os.Getenv("SystemRoot")
	if root == "" {
		root = `C:\Windows`
	}
	return filepath.Join(root, "System32", "drivers", "etc", "hosts")
}

// normalizeHosts converts any line endings to CRLF and guarantees a trailing newline.
func normalizeHosts(s string) string {
	s = strings.ReplaceAll(strings.ReplaceAll(s, "\r\n", "\n"), "\r", "\n")
	s = strings.TrimRight(s, "\n") + "\n"
	return strings.ReplaceAll(s, "\n", "\r\n")
}

func registerHosts(mux *http.ServeMux) {
	mux.HandleFunc("/api/hosts", func(w http.ResponseWriter, r *http.Request) {
		p := hostsPath()
		b, err := os.ReadFile(p)
		if err != nil && !os.IsNotExist(err) {
			fail(w, 500, err.Error())
			return
		}
		writeJSON(w, map[string]any{"path": p, "text": strings.TrimPrefix(string(b), "\xef\xbb\xbf"), "admin": isAdmin(), "exists": err == nil})
	})
	mux.HandleFunc("/api/hosts/save", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var req struct {
			Text string `json:"text"`
		}
		if err := decodeJSON(r, &req); err != nil {
			fail(w, 400, err.Error())
			return
		}
		p := hostsPath()
		backup := ""
		if old, err := os.ReadFile(p); err == nil {
			backup = p + ".bak-" + time.Now().Format("20060102-150405")
			if err := os.WriteFile(backup, old, 0o644); err != nil {
				fail(w, 403, hostsErr(err))
				return
			}
		}
		if err := os.WriteFile(p, []byte(normalizeHosts(req.Text)), 0o644); err != nil {
			fail(w, 403, hostsErr(err))
			return
		}
		writeJSON(w, map[string]string{"backup": backup})
	})
	mux.HandleFunc("/api/hosts/flush", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		c := exec.Command("ipconfig", "/flushdns")
		c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
		if out, err := c.CombinedOutput(); err != nil {
			fail(w, 500, strings.TrimSpace(string(out)+" "+err.Error()))
			return
		}
		w.WriteHeader(204)
	})
}

func hostsErr(err error) string {
	if os.IsPermission(err) {
		return "Access denied: restart My Tool Box as administrator to change the hosts file"
	}
	return fmt.Sprint(err)
}
