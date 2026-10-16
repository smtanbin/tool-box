// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"embed"
	"encoding/json"
	"flag"
	"fmt"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"sync/atomic"
	"time"
)

//go:embed web/index.html web/skins.css web/vendor/7.css web/tools.js web/proc.js web/image.js web/boot.js web/hosts.js web/sysnet.js web/env.js web/home.js web/menus.js web/hw.js web/fluent-icons.js web/icon.svg web/icon-256.png web/tool-foldersize.svg
var webFS embed.FS

var (
	st       = &State{}
	lastPing atomic.Int64
	byeAt    atomic.Int64
	autoExit bool

	// appVersion is set at build time: -ldflags "-X main.appVersion=1.2.3".
	// When set it replaces the VERSION constant in web/index.html.
	appVersion  = ""
	versionDecl = regexp.MustCompile(`VERSION='[^']*'`)
)

func writeJSON(w http.ResponseWriter, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	json.NewEncoder(w).Encode(v)
}

func fail(w http.ResponseWriter, code int, msg string) {
	http.Error(w, msg, code)
}

func main() {
	port := flag.Int("port", 0, "port to listen on (0 = random)")
	noBrowser := flag.Bool("nobrowser", false, "do not open a window; keep running")
	openTool := flag.String("tool", "", "tool page to open, e.g. disk or ping")
	flag.Parse()
	autoExit = !*noBrowser

	mux := http.NewServeMux()
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		name, ctype := "index.html", "text/html; charset=utf-8"
		switch r.URL.Path {
		case "/":
		case "/icon.svg", "/tool-foldersize.svg":
			name, ctype = r.URL.Path[1:], "image/svg+xml"
		case "/tools.js", "/proc.js", "/image.js", "/boot.js", "/hosts.js", "/sysnet.js", "/env.js", "/home.js", "/menus.js", "/hw.js", "/fluent-icons.js":
			name, ctype = r.URL.Path[1:], "text/javascript; charset=utf-8"
		case "/vendor/7.css":
			name, ctype = "vendor/7.css", "text/css; charset=utf-8"
		case "/skins.css":
			name, ctype = "skins.css", "text/css; charset=utf-8"
		case "/icon-256.png", "/favicon.ico":
			name, ctype = "icon-256.png", "image/png"
		default:
			http.NotFound(w, r)
			return
		}
		b, _ := webFS.ReadFile("web/" + name)
		if name == "index.html" {
			b = injectAccent(injectPrefs(b))
		}
		if name == "index.html" && appVersion != "" {
			b = versionDecl.ReplaceAll(b, []byte("VERSION='"+appVersion+"'"))
		}
		w.Header().Set("Content-Type", ctype)
		w.Header().Set("Cache-Control", "no-cache") // a new build must never show the previous build's styles
		w.Write(b)
	})
	mux.HandleFunc("/api/ping", func(w http.ResponseWriter, r *http.Request) {
		lastPing.Store(time.Now().UnixNano())
		w.WriteHeader(204)
	})
	mux.HandleFunc("/api/bye", func(w http.ResponseWriter, r *http.Request) {
		now := time.Now().UnixNano()
		byeAt.Store(now)
		w.WriteHeader(204)
		if autoExit {
			go func() {
				time.Sleep(5 * time.Second)
				if lastPing.Load() < now {
					os.Exit(0)
				}
			}()
		}
	})
	mux.HandleFunc("/api/drives", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, listDrives()) })
	mux.HandleFunc("/api/browse", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, map[string]string{"path": pickFolder()})
	})
	mux.HandleFunc("/api/scan", hScan)
	mux.HandleFunc("/api/cancel", func(w http.ResponseWriter, r *http.Request) {
		st.mu.RLock()
		if st.sc != nil {
			st.sc.cancel.Store(true)
		}
		st.mu.RUnlock()
		w.WriteHeader(204)
	})
	mux.HandleFunc("/api/reset", func(w http.ResponseWriter, r *http.Request) {
		st.mu.Lock()
		if st.sc != nil {
			st.sc.cancel.Store(true)
		}
		st.root, st.byID = nil, nil
		st.mu.Unlock()
		dupes.cancel.Store(true)
		dupes.mu.Lock()
		dupes.groups, dupes.wasted = nil, 0
		dupes.mu.Unlock()
		w.WriteHeader(204)
	})
	mux.HandleFunc("/api/quit", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(204)
		go func() { time.Sleep(200 * time.Millisecond); os.Exit(0) }()
	})
	mux.HandleFunc("/api/status", hStatus)
	mux.HandleFunc("/api/node", hNode)
	mux.HandleFunc("/api/treemap", hTreemap)
	mux.HandleFunc("/api/types", hTypes)
	mux.HandleFunc("/api/top", hTop)
	mux.HandleFunc("/api/search", hSearch)
	mux.HandleFunc("/api/export", hExport)
	mux.HandleFunc("/api/delete", hDelete)
	mux.HandleFunc("/api/reveal", hReveal)
	mux.HandleFunc("/api/dupes/start", hDupesStart)
	mux.HandleFunc("/api/dupes/status", hDupesStatus)
	registerNet(mux)
	registerProc(mux)
	registerHosts(mux)
	registerNetInfo(mux)
	registerEnv(mux)
	registerAppInfo(mux)
	registerHardware(mux)
	registerHardwareLive(mux)
	registerPerf(mux)
	registerPrefs(mux)
	mux.HandleFunc("/api/disks", hDisks)
	mux.HandleFunc("/api/elevate", hElevate)

	ln, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", *port))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	url := "http://" + ln.Addr().String() + "/"
	if a := flag.Arg(0); a != "" {
		if abs, err := filepath.Abs(a); err == nil {
			st.StartScan(abs)
		}
	}
	if !*noBrowser {
		if *openTool != "" {
			url += "#" + *openTool
		}
		openBrowser(url)
	} else {
		fmt.Println("My Tool Box running at", url)
	}
	http.Serve(ln, mux)
}

