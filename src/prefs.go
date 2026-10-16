// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"bytes"
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"sync"
)

// User preferences (theme, skin, last tool, sidebar state, zoom, ...) kept in %LocalAppData%\MyToolBox\prefs.json.
// The page's localStorage alone is not enough: the app listens on a new random port every launch, and the
// browser keeps localStorage per origin (host + port), so it would start empty each time.

var prefsMu sync.Mutex

const prefsPlaceholder = "/*PREFS*/{}"

func prefsPath() string { return filepath.Join(dataDir(), "prefs.json") }

func loadPrefs() map[string]string {
	prefsMu.Lock()
	defer prefsMu.Unlock()
	m := map[string]string{}
	if b, err := os.ReadFile(prefsPath()); err == nil {
		json.Unmarshal(b, &m)
	}
	return m
}

func savePrefs(m map[string]string) error {
	prefsMu.Lock()
	defer prefsMu.Unlock()
	if err := os.MkdirAll(dataDir(), 0o755); err != nil {
		return err
	}
	b, _ := json.MarshalIndent(m, "", "  ")
	tmp := prefsPath() + ".tmp"
	if err := os.WriteFile(tmp, b, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, prefsPath())
}

// injectPrefs puts the saved preferences into index.html so they apply before the first paint.
// json.Marshal escapes < > & so the result is safe inside a <script> element.
func injectPrefs(page []byte) []byte {
	b, err := json.Marshal(loadPrefs())
	if err != nil {
		return page
	}
	return bytes.Replace(page, []byte(prefsPlaceholder), b, 1)
}

func registerPrefs(mux *http.ServeMux) {
	mux.HandleFunc("/api/prefs", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodGet {
			writeJSON(w, loadPrefs())
			return
		}
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		var in map[string]string
		if err := decodeJSON(r, &in); err != nil {
			fail(w, 400, err.Error())
			return
		}
		out := map[string]string{}
		for k, v := range in { // small settings only; skip anything that looks like bulk data
			if len(k) <= 64 && len(v) <= 4096 && len(out) < 200 {
				out[k] = v
			}
		}
		if err := savePrefs(out); err != nil {
			fail(w, 500, err.Error())
			return
		}
		w.WriteHeader(204)
	})
}
