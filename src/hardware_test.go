// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestHardwareReport(t *testing.T) {
	mux := http.NewServeMux()
	registerHardware(mux)
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, httptest.NewRequest("GET", "/api/hw", nil))
	if w.Code != 200 {
		t.Skipf("WMI not available here: %d %s", w.Code, w.Body.String())
	}
	var r struct {
		WMI struct {
			CPU    []map[string]any `json:"cpu"`
			Board  []map[string]any `json:"board"`
			Memory []map[string]any `json:"memory"`
			GPU    []map[string]any `json:"gpu"`
			OS     []map[string]any `json:"os"`
		} `json:"wmi"`
		Live struct {
			MemTotal uint64 `json:"memTotal"`
			MemUsed  uint64 `json:"memUsed"`
		} `json:"live"`
	}
	if err := json.Unmarshal(w.Body.Bytes(), &r); err != nil {
		t.Fatal(err)
	}
	if len(r.WMI.CPU) == 0 || r.WMI.CPU[0]["Name"] == nil {
		t.Errorf("no processor: %+v", r.WMI.CPU)
	}
	if len(r.WMI.OS) == 0 || len(r.WMI.GPU) == 0 {
		t.Errorf("os/gpu missing: os=%d gpu=%d", len(r.WMI.OS), len(r.WMI.GPU))
	}
	if r.Live.MemTotal == 0 || r.Live.MemUsed == 0 || r.Live.MemUsed > r.Live.MemTotal {
		t.Errorf("live memory: %+v", r.Live)
	}
	t.Logf("CPU=%v board=%d modules=%d gpus=%d", r.WMI.CPU[0]["Name"], len(r.WMI.Board), len(r.WMI.Memory), len(r.WMI.GPU))
}
