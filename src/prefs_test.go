// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestPrefsSurviveAndAreInjected(t *testing.T) {
	t.Setenv("LocalAppData", t.TempDir())
	mux := http.NewServeMux()
	registerPrefs(mux)

	post := func(body, origin string) int {
		r := httptest.NewRequest("POST", "http://127.0.0.1:5000/api/prefs", strings.NewReader(body))
		r.Header.Set("Origin", origin)
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, r)
		return w.Code
	}
	if c := post(`{"skin":"aero","themeMode":"dark","x":"</script><script>alert(1)</script>"}`, "http://127.0.0.1:5000"); c != 204 {
		t.Fatalf("save: %d", c)
	}
	if c := post(`{"skin":"evil"}`, "http://evil.example"); c != 403 {
		t.Fatalf("cross-origin save must be refused: %d", c)
	}
	p := loadPrefs()
	if p["skin"] != "aero" || p["themeMode"] != "dark" {
		t.Fatalf("not saved: %v", p)
	}
	// a new launch (any port) gets them inside the page, safely escaped
	page := injectPrefs([]byte(`<script>var P=` + prefsPlaceholder + `;</script>`))
	s := string(page)
	if !strings.Contains(s, `"skin":"aero"`) || !strings.Contains(s, `"themeMode":"dark"`) {
		t.Fatalf("not injected: %s", s)
	}
	if strings.Count(s, "</script>") != 1 {
		t.Fatalf("a saved value broke out of the script element: %s", s)
	}
}

func TestIndexHasPrefsPlaceholder(t *testing.T) {
	b, _ := webFS.ReadFile("web/index.html")
	if !strings.Contains(string(b), prefsPlaceholder) {
		t.Fatal("index.html lost the /*PREFS*/{} placeholder")
	}
}

func TestAccentHex(t *testing.T) {
	if got := accentHex(0xffd47800); got != "#0078d4" { // ABGR for Windows blue
		t.Errorf("got %s", got)
	}
	if a := osAccent(); len(a) != 7 || a[0] != '#' {
		t.Errorf("osAccent = %q", a)
	}
	b, _ := webFS.ReadFile("web/index.html")
	if !strings.Contains(string(b), accentPlaceholder) {
		t.Error("index.html lost the OS accent placeholder")
	}
}
