// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"net/http"
	"os"
	"path/filepath"
)

// App-wide facts and actions used by the File menu: administrator state and the app's data folder.

// dataDir is %LocalAppData%\MyToolBox (environment-variable backups live under it).
func dataDir() string {
	base := os.Getenv("LocalAppData")
	if base == "" {
		base = os.TempDir()
	}
	return filepath.Join(base, "MyToolBox")
}

func registerAppInfo(mux *http.ServeMux) {
	mux.HandleFunc("/api/info", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, map[string]any{"admin": isAdmin(), "version": appVersion, "dataDir": dataDir()})
	})
	mux.HandleFunc("/api/opendata", func(w http.ResponseWriter, r *http.Request) {
		if !sameOrigin(r) {
			fail(w, 403, "POST from this app only")
			return
		}
		d := dataDir()
		if err := os.MkdirAll(d, 0o755); err != nil {
			fail(w, 500, err.Error())
			return
		}
		explorerOpen(d)
		w.WriteHeader(204)
	})
}
