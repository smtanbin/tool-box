// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"crypto/sha256"
	"fmt"
	"io"
	"net/http"
	"os"
	"sort"
	"strconv"
	"sync"
	"sync/atomic"
)

type DupeItem struct {
	ID   int32  `json:"id"`
	Path string `json:"path"`
	Mod  int64  `json:"mod"`
}

type DupeGroup struct {
	Size   int64      `json:"size"`
	Wasted int64      `json:"wasted"`
	Items  []DupeItem `json:"items"`
}

type DupeJob struct {
	mu      sync.Mutex
	running bool
	phase   string
	done    atomic.Int64
	total   atomic.Int64
	groups  []DupeGroup
	wasted  int64
	cancel  atomic.Bool
}

var dupes = &DupeJob{}

type cand struct {
	n    *Node
	path string
	key  string
}

func hashFile(path string, limit int64) (string, error) {
	f, err := os.Open(longPath(path))
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	var rd io.Reader = f
	if limit > 0 {
		rd = io.LimitReader(f, limit)
	}
	if _, err := io.Copy(h, rd); err != nil {
		return "", err
	}
	return fmt.Sprintf("%x", h.Sum(nil)), nil
}

func parallel(items []*cand, workers int, fn func(*cand)) {
	ch := make(chan *cand)
	var wg sync.WaitGroup
	for i := 0; i < workers; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for c := range ch {
				if !dupes.cancel.Load() {
					fn(c)
				}
				dupes.done.Add(1)
			}
		}()
	}
	for _, c := range items {
		ch <- c
	}
	close(ch)
	wg.Wait()
}

func regroup(cs []*cand, bySize bool) [][]*cand {
	m := map[string][]*cand{}
	for _, c := range cs {
		if c.key == "" {
			continue
		}
		k := fmt.Sprintf("%d|%s", c.n.Size, c.key)
		m[k] = append(m[k], c)
	}
	var out [][]*cand
	for _, g := range m {
		if len(g) > 1 {
			out = append(out, g)
		}
	}
	return out
}

func runDupes(root *Node, minSize int64) {
	j := dupes
	j.phase = "Grouping by size"
	bySize := map[int64][]*cand{}
	st.mu.RLock()
	walkTree(root, func(n *Node) bool {
		if !n.IsDir && n.Size >= minSize && n.Size > 0 {
			bySize[n.Size] = append(bySize[n.Size], &cand{n: n})
		}
		return true
	})
	var stage1 []*cand
	for _, g := range bySize {
		if len(g) > 1 {
			for _, c := range g {
				c.path = c.n.Path()
			}
			stage1 = append(stage1, g...)
		}
	}
	st.mu.RUnlock()

	j.phase = "Reading file starts"
	j.done.Store(0)
	j.total.Store(int64(len(stage1)))
	parallel(stage1, 6, func(c *cand) { c.key, _ = hashFile(c.path, 65536) })
	var stage2 []*cand
	var final [][]*cand
	for _, g := range regroup(stage1, true) {
		if g[0].n.Size <= 65536 {
			final = append(final, g)
		} else {
			stage2 = append(stage2, g...)
			for _, c := range g {
				c.key = ""
			}
		}
	}
	j.phase = "Comparing full contents"
	j.done.Store(0)
	j.total.Store(int64(len(stage2)))
	parallel(stage2, 4, func(c *cand) { c.key, _ = hashFile(c.path, 0) })
	final = append(final, regroup(stage2, true)...)

	var groups []DupeGroup
	var wasted int64
	for _, g := range final {
		sort.Slice(g, func(a, b int) bool { return g[a].n.Mod < g[b].n.Mod })
		dg := DupeGroup{Size: g[0].n.Size, Wasted: g[0].n.Size * int64(len(g)-1)}
		for _, c := range g {
			dg.Items = append(dg.Items, DupeItem{c.n.ID, c.path, c.n.Mod})
		}
		wasted += dg.Wasted
		groups = append(groups, dg)
	}
	sort.Slice(groups, func(a, b int) bool { return groups[a].Wasted > groups[b].Wasted })
	j.mu.Lock()
	j.groups, j.wasted, j.running, j.phase = groups, wasted, false, "Done"
	j.mu.Unlock()
}

func hDupesStart(w http.ResponseWriter, r *http.Request) {
	st.mu.RLock()
	n := nodeFrom(r)
	st.mu.RUnlock()
	if n == nil {
		fail(w, 404, "no such node")
		return
	}
	minSize, _ := strconv.ParseInt(r.URL.Query().Get("min"), 10, 64)
	j := dupes
	j.mu.Lock()
	if j.running {
		j.mu.Unlock()
		fail(w, 409, "already running")
		return
	}
	j.running, j.groups, j.wasted = true, nil, 0
	j.cancel.Store(false)
	j.mu.Unlock()
	go runDupes(n, minSize)
	w.WriteHeader(204)
}

func hDupesStatus(w http.ResponseWriter, r *http.Request) {
	j := dupes
	j.mu.Lock()
	defer j.mu.Unlock()
	out := map[string]any{"running": j.running, "phase": j.phase, "done": j.done.Load(), "total": j.total.Load()}
	if !j.running {
		g := j.groups
		if len(g) > 500 {
			g = g[:500]
		}
		out["groups"], out["wasted"], out["groupCount"] = g, j.wasted, len(j.groups)
	}
	writeJSON(w, out)
}
