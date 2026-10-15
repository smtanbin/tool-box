// Copyright 2026 Tanbin (magestica.com). Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"embed"
	"encoding/csv"
	"encoding/json"
	"flag"
	"fmt"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync/atomic"
	"time"
)

//go:embed web/index.html web/tools.js web/icon.svg web/icon-256.png web/tool-foldersize.svg
var webFS embed.FS

var (
	st       = &State{}
	lastPing atomic.Int64
	byeAt    atomic.Int64
	autoExit bool
)

func writeJSON(w http.ResponseWriter, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	json.NewEncoder(w).Encode(v)
}

func fail(w http.ResponseWriter, code int, msg string) {
	http.Error(w, msg, code)
}

// nodeFrom resolves ?id= (default: root). Caller must hold st.mu.
func nodeFrom(r *http.Request) *Node {
	id := 0
	if s := r.URL.Query().Get("id"); s != "" {
		id, _ = strconv.Atoi(s)
	}
	return st.node(id)
}

type NodeDTO struct {
	ID    int32  `json:"id"`
	Name  string `json:"name"`
	Size  int64  `json:"size"`
	Files int64  `json:"files"`
	Dirs  int64  `json:"dirs"`
	Mod   int64  `json:"mod"`
	Dir   bool   `json:"dir"`
	Err   bool   `json:"err,omitempty"`
	Ext   string `json:"ext,omitempty"`
}

func dto(n *Node) NodeDTO {
	return NodeDTO{n.ID, n.Name, n.Size, n.Files, n.Dirs, n.Mod, n.IsDir, n.Err, n.Ext()}
}

type Crumb struct {
	ID   int32  `json:"id"`
	Name string `json:"name"`
}

func main() {
	port := flag.Int("port", 0, "port to listen on (0 = random)")
	noBrowser := flag.Bool("nobrowser", false, "do not open a window; keep running")
	flag.Parse()
	autoExit = !*noBrowser

	mux := http.NewServeMux()
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		name, ctype := "index.html", "text/html; charset=utf-8"
		switch r.URL.Path {
		case "/":
		case "/icon.svg", "/tool-foldersize.svg":
			name, ctype = r.URL.Path[1:], "image/svg+xml"
		case "/tools.js":
			name, ctype = "tools.js", "text/javascript; charset=utf-8"
		case "/icon-256.png", "/favicon.ico":
			name, ctype = "icon-256.png", "image/png"
		default:
			http.NotFound(w, r)
			return
		}
		b, _ := webFS.ReadFile("web/" + name)
		w.Header().Set("Content-Type", ctype)
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
		openBrowser(url)
	} else {
		fmt.Println("Tool Box running at", url)
	}
	http.Serve(ln, mux)
}

func hScan(w http.ResponseWriter, r *http.Request) {
	p := strings.TrimSpace(r.URL.Query().Get("path"))
	if p == "" {
		fail(w, 400, "path required")
		return
	}
	if err := st.StartScan(p); err != nil {
		fail(w, 400, err.Error())
		return
	}
	w.WriteHeader(204)
}

func hStatus(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	out := map[string]any{"scanning": st.scanning, "hasRoot": st.root != nil}
	if st.root != nil {
		out["root"] = st.root.Name
	}
	if sc := st.sc; sc != nil {
		cur, _ := sc.cur.Load().(string)
		end := time.Now()
		if e := sc.end.Load(); e != 0 {
			end = time.Unix(0, e)
		}
		msg, _ := sc.errMsg.Load().(string)
		out["files"], out["dirs"], out["bytes"], out["errors"] = sc.files.Load(), sc.dirs.Load(), sc.bytes.Load(), sc.errs.Load()
		out["current"], out["elapsed"], out["expected"] = cur, end.Sub(sc.start).Seconds(), sc.expected
		out["cancelled"], out["scanRoot"], out["error"] = sc.cancel.Load(), sc.root, msg
	}
	writeJSON(w, out)
}

func hNode(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	limit := 1500
	if s := r.URL.Query().Get("limit"); s != "" {
		limit, _ = strconv.Atoi(s)
	}
	var crumbs []Crumb
	for a := n; a != nil; a = a.Parent {
		crumbs = append([]Crumb{{a.ID, a.Name}}, crumbs...)
	}
	res := map[string]any{"node": dto(n), "path": n.Path(), "crumbs": crumbs}
	if n.IsDir {
		kids := make([]NodeDTO, 0, min(limit, len(n.Children)))
		for i, c := range n.Children {
			if i >= limit {
				break
			}
			kids = append(kids, dto(c))
		}
		res["children"] = kids
		res["more"] = max(0, len(n.Children)-limit)
	}
	writeJSON(w, res)
}

type TM struct {
	ID    int32  `json:"i"`
	Name  string `json:"n"`
	Size  int64  `json:"s"`
	Dir   bool   `json:"d,omitempty"`
	Ext   string `json:"e,omitempty"`
	Mod   int64  `json:"m,omitempty"`
	Kids  []*TM  `json:"k,omitempty"`
	Other int    `json:"o,omitempty"`
}

func hTreemap(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	budget := 9000
	limits := []int{400, 120, 60, 40, 30}
	mk := func(c *Node) *TM {
		return &TM{ID: c.ID, Name: c.Name, Size: c.Size, Dir: c.IsDir, Ext: c.Ext(), Mod: c.Mod}
	}
	rt := mk(n)
	type item struct {
		tm  *TM
		n   *Node
		lvl int
	}
	q := []item{{rt, n, 0}}
	count := 1
	for len(q) > 0 {
		it := q[0]
		q = q[1:]
		lim := limits[min(it.lvl, len(limits)-1)]
		shown, restN := 0, 0
		var rest int64
		for _, c := range it.n.Children {
			if c.Size == 0 {
				continue
			}
			if shown >= lim || count >= budget {
				rest += c.Size
				restN++
				continue
			}
			ct := mk(c)
			it.tm.Kids = append(it.tm.Kids, ct)
			count++
			shown++
			if c.IsDir && len(c.Children) > 0 {
				q = append(q, item{ct, c, it.lvl + 1})
			}
		}
		if rest > 0 {
			it.tm.Kids = append(it.tm.Kids, &TM{ID: -1, Name: fmt.Sprintf("(%d smaller items)", restN), Size: rest, Other: restN})
			sort.SliceStable(it.tm.Kids, func(i, j int) bool { return it.tm.Kids[i].Size > it.tm.Kids[j].Size })
		}
	}
	writeJSON(w, rt)
}

func hTypes(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	type T struct {
		Ext   string `json:"ext"`
		Size  int64  `json:"size"`
		Count int64  `json:"count"`
	}
	m := map[string]*T{}
	walkTree(n, func(x *Node) bool {
		if !x.IsDir {
			e := x.Ext()
			t := m[e]
			if t == nil {
				t = &T{Ext: e}
				m[e] = t
			}
			t.Size += x.Size
			t.Count++
		}
		return true
	})
	out := make([]*T, 0, len(m))
	for _, t := range m {
		out = append(out, t)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Size > out[j].Size })
	writeJSON(w, map[string]any{"types": out, "total": n.Size})
}

type Item struct {
	ID   int32  `json:"id"`
	Name string `json:"name"`
	Dir  bool   `json:"dir"`
	Size int64  `json:"size"`
	Mod  int64  `json:"mod"`
	Path string `json:"path"`
}

func toItems(ns []*Node) []Item {
	out := make([]Item, len(ns))
	for i, n := range ns {
		out[i] = Item{n.ID, n.Name, n.IsDir, n.Size, n.Mod, n.Path()}
	}
	return out
}

func hTop(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	cnt, _ := strconv.Atoi(r.URL.Query().Get("n"))
	if cnt <= 0 {
		cnt = 200
	}
	ext := r.URL.Query().Get("ext")
	hasExt := r.URL.Query().Has("ext")
	var best []*Node
	trim := func() {
		sort.Slice(best, func(i, j int) bool { return best[i].Size > best[j].Size })
		if len(best) > cnt {
			best = best[:cnt]
		}
	}
	walkTree(n, func(x *Node) bool {
		if !x.IsDir && (!hasExt || x.Ext() == ext) {
			best = append(best, x)
			if len(best) > cnt*4 {
				trim()
			}
		}
		return true
	})
	trim()
	writeJSON(w, toItems(best))
}

func hSearch(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	q := strings.ToLower(strings.TrimSpace(r.URL.Query().Get("q")))
	minSize, _ := strconv.ParseInt(r.URL.Query().Get("min"), 10, 64)
	wild := strings.ContainsAny(q, "*?")
	var hits []*Node
	walkTree(n, func(x *Node) bool {
		if x == n || x.Size < minSize {
			return true
		}
		name := strings.ToLower(x.Name)
		ok := q == ""
		if !ok {
			if wild {
				ok, _ = filepath.Match(q, name)
			} else {
				ok = strings.Contains(name, q)
			}
		}
		if ok {
			hits = append(hits, x)
		}
		return true
	})
	sort.Slice(hits, func(i, j int) bool { return hits[i].Size > hits[j].Size })
	total := len(hits)
	if len(hits) > 500 {
		hits = hits[:500]
	}
	writeJSON(w, map[string]any{"items": toItems(hits), "total": total})
}

func hExport(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	defer st.mu.RUnlock()
	n := nodeFrom(r)
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	withFiles := r.URL.Query().Get("files") == "1"
	w.Header().Set("Content-Type", "text/csv; charset=utf-8")
	w.Header().Set("Content-Disposition", `attachment; filename="folder-sizes.csv"`)
	w.Write([]byte("\xef\xbb\xbf"))
	cw := csv.NewWriter(w)
	cw.Write([]string{"Path", "Type", "SizeBytes", "Files", "Folders", "Modified"})
	walkTree(n, func(x *Node) bool {
		if x.IsDir || withFiles {
			t := "File"
			if x.IsDir {
				t = "Folder"
			}
			mod := ""
			if x.Mod > 0 {
				mod = time.Unix(x.Mod, 0).Format("2006-01-02 15:04:05")
			}
			cw.Write([]string{x.Path(), t, strconv.FormatInt(x.Size, 10), strconv.FormatInt(x.Files, 10), strconv.FormatInt(x.Dirs, 10), mod})
		}
		return true
	})
	cw.Flush()
}

func hReveal(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	n := nodeFrom(r)
	var p string
	var dir bool
	if n != nil {
		p, dir = n.Path(), n.IsDir
	}
	st.mu.RUnlock()
	if p == "" {
		fail(w, 404, "no such node")
		return
	}
	if dir && r.URL.Query().Get("open") == "1" {
		explorerOpen(p)
	} else {
		explorerSelect(p)
	}
	w.WriteHeader(204)
}

func hDelete(w http.ResponseWriter, r *http.Request) {
	var req struct {
		IDs       []int `json:"ids"`
		Permanent bool  `json:"permanent"`
	}
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		fail(w, 400, err.Error())
		return
	}
	type res struct {
		ID    int    `json:"id"`
		OK    bool   `json:"ok"`
		Error string `json:"error,omitempty"`
	}
	var out []res
	for _, id := range req.IDs {
		st.mu.RLock()
		n := st.node(id)
		var p string
		if n != nil && n.Parent != nil {
			p = n.Path()
		}
		st.mu.RUnlock()
		if p == "" {
			out = append(out, res{id, false, "not found or is the scan root"})
			continue
		}
		var err error
		if req.Permanent {
			err = os.RemoveAll(p)
		} else {
			err = recycle(p)
		}
		if err != nil {
			out = append(out, res{id, false, err.Error()})
			continue
		}
		st.mu.Lock()
		st.detach(n)
		st.mu.Unlock()
		out = append(out, res{id, true, ""})
	}
	writeJSON(w, out)
}
