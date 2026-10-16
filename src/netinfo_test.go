// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"
)

func get(t *testing.T, mux *http.ServeMux, url string, v any) int {
	t.Helper()
	w := httptest.NewRecorder()
	mux.ServeHTTP(w, httptest.NewRequest("GET", url, nil))
	if v != nil && w.Code == 200 {
		if err := json.Unmarshal(w.Body.Bytes(), v); err != nil {
			t.Fatalf("%s: bad JSON: %v\n%s", url, err, w.Body.String())
		}
	}
	return w.Code
}

func TestAdapters(t *testing.T) {
	list, err := listAdapters()
	if err != nil {
		t.Fatal(err)
	}
	if len(list) == 0 {
		t.Fatal("no adapters")
	}
	sawLoopback := false
	for _, a := range list {
		if a.Name == "" || a.Status == "" || a.Type == "" {
			t.Errorf("incomplete adapter: %+v", a)
		}
		for _, ad := range a.Addrs {
			max := 32
			if ad.V6 {
				max = 128
			}
			if ad.Prefix < 0 || ad.Prefix > max {
				t.Errorf("%s: %s has prefix %d", a.Name, ad.IP, ad.Prefix)
			}
			if !ad.V6 && strings.HasPrefix(ad.IP, "127.") {
				sawLoopback = true
			}
		}
		t.Logf("%s | %s | %s | %v gw=%v dns=%v mac=%s speed=%d dhcpsrv=%q", a.Name, a.Type, a.Status, a.Addrs, a.Gateways, a.DNS, a.MAC, a.SpeedBps, a.DHCPServer)
	}
	if !sawLoopback {
		t.Log("loopback adapter not listed (allowed)")
	}
}

func TestDNSLocalhostAndErrors(t *testing.T) {
	mux := http.NewServeMux()
	registerNetInfo(mux)
	var r struct {
		Results []struct {
			Type    string
			Records []DNSRecord
		}
	}
	if code := get(t, mux, "/api/net/dns?name=localhost&type=A", &r); code != 200 || len(r.Results) != 1 || len(r.Results[0].Records) == 0 {
		t.Errorf("localhost A: code=%d %+v", code, r)
	}
	if code := get(t, mux, "/api/net/dns?name=127.0.0.1", &r); code != 200 || r.Results[0].Type != "PTR" {
		t.Errorf("IP input should switch to PTR: %d %+v", code, r)
	}
	if code := get(t, mux, "/api/net/dns?name=x.test&server=not-an-ip", nil); code != 400 {
		t.Errorf("bad server: %d", code)
	}
	if code := get(t, mux, "/api/net/dns", nil); code != 400 {
		t.Errorf("missing name: %d", code)
	}
}

func TestTraceHopLoopback(t *testing.T) {
	mux := http.NewServeMux()
	registerNetInfo(mux)
	var h struct {
		Hop    string
		Done   bool
		Probes []HopProbe
	}
	if code := get(t, mux, "/api/net/hop?host=127.0.0.1&ttl=1&names=0", &h); code != 200 {
		t.Fatalf("code %d", code)
	}
	if !h.Done || h.Hop != "127.0.0.1" || len(h.Probes) != 3 {
		t.Errorf("loopback should be reached on hop 1: %+v", h)
	}
	if code := get(t, mux, "/api/net/hop?host=%20", nil); code != 400 {
		t.Errorf("empty host: %d", code)
	}
}

func TestEnvReadWriteDelete(t *testing.T) {
	const name = "TOOLBOX_UNIT_TEST_VAR"
	deleteEnv("user", name)
	defer deleteEnv("user", name)
	if err := writeEnv("user", name, `%SystemRoot%\x`, true); err != nil {
		t.Fatal(err)
	}
	vars, err := readEnv("user")
	if err != nil {
		t.Fatal(err)
	}
	found := false
	for _, v := range vars {
		if v.Name == name {
			found = true
			if v.Value != `%SystemRoot%\x` || !v.Expand {
				t.Errorf("round trip lost data: %+v", v)
			}
		}
	}
	if !found {
		t.Fatal("variable not read back")
	}
	if err := writeEnv("user", name, "plain", false); err != nil {
		t.Fatal(err)
	}
	vars, _ = readEnv("user")
	for _, v := range vars {
		if v.Name == name && (v.Value != "plain" || v.Expand) {
			t.Errorf("type change failed: %+v", v)
		}
	}
	if err := deleteEnv("user", name); err != nil {
		t.Fatal(err)
	}
	vars, _ = readEnv("user")
	for _, v := range vars {
		if v.Name == name {
			t.Error("variable still present after delete")
		}
	}
	if err := writeEnv("user", "bad=name", "x", false); err == nil {
		t.Error("name with = must be rejected")
	}
	if err := writeEnv("machine", "X", "x", false); err == nil {
		t.Error("unknown scope must be rejected")
	}
}

func TestEnvSystemReadable(t *testing.T) {
	vars, err := readEnv("system")
	if err != nil || len(vars) == 0 {
		t.Fatalf("system variables: %v (%d)", err, len(vars))
	}
}

func TestCheckPaths(t *testing.T) {
	os.Setenv("TB_TEST_DIR", os.TempDir())
	got := checkPaths([]string{"%TB_TEST_DIR%", `"` + os.TempDir() + `"`, `C:\definitely\not\here`, "%NOPE_UNKNOWN%"})
	if !got[0].Exists || !got[0].Dir || !got[1].Exists {
		t.Errorf("existing dirs not found: %+v", got)
	}
	if got[2].Exists || got[3].Exists {
		t.Errorf("missing dirs reported as existing: %+v", got)
	}
	if got[3].Expanded != "%NOPE_UNKNOWN%" {
		t.Errorf("unknown variables must stay as written: %q", got[3].Expanded)
	}
}

func TestEnvWriteRejectsCrossOrigin(t *testing.T) {
	mux := http.NewServeMux()
	registerEnv(mux)
	for _, p := range []string{"/api/env/set", "/api/env/delete", "/api/env/pathcheck"} {
		r := httptest.NewRequest("POST", "http://127.0.0.1:1"+p, strings.NewReader(`{}`))
		r.Header.Set("Origin", "http://evil.example")
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, r)
		if w.Code != 403 {
			t.Errorf("%s: status %d, want 403", p, w.Code)
		}
	}
}
