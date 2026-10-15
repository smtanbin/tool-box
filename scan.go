// Copyright 2026 Tanbin (magestica.com). Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"errors"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// Node is one file or folder in the scanned tree.
type Node struct {
	ID       int32
	Name     string
	Size     int64
	Files    int64 // files in subtree (1 for a file)
	Dirs     int64 // folders in subtree (excluding itself)
	Mod      int64 // unix seconds; for folders: newest file inside
	IsDir    bool
	Err      bool
	Dead     bool
	Parent   *Node
	Children []*Node
}

func (n *Node) Path() string {
	if n.Parent == nil {
		return n.Name
	}
	return filepath.Join(n.Parent.Path(), n.Name)
}

func (n *Node) Ext() string {
	if n.IsDir {
		return ""
	}
	return strings.ToLower(filepath.Ext(n.Name))
}

func walkTree(n *Node, fn func(*Node) bool) {
	if !fn(n) {
		return
	}
	for _, c := range n.Children {
		walkTree(c, fn)
	}
}

func aggregate(n *Node) {
	if !n.IsDir {
		n.Files = 1
		return
	}
	var sz, f, d, mod int64
	for _, c := range n.Children {
		aggregate(c)
		sz += c.Size
		f += c.Files
		if c.IsDir {
			d += c.Dirs + 1
		}
		if c.Mod > mod {
			mod = c.Mod
		}
	}
	n.Size, n.Files, n.Dirs = sz, f, d
	if mod > 0 {
		n.Mod = mod
	}
	sort.Slice(n.Children, func(i, j int) bool { return n.Children[i].Size > n.Children[j].Size })
}

func longPath(p string) string {
	if len(p) > 240 && !strings.HasPrefix(p, `\\?\`) {
		if strings.HasPrefix(p, `\\`) {
			return `\\?\UNC\` + p[2:]
		}
		return `\\?\` + p
	}
	return p
}

type Scanner struct {
	files, dirs, bytes, errs atomic.Int64
	cur                      atomic.Value
	cancel, done             atomic.Bool
	start                    time.Time
	end                      atomic.Int64
	root                     string
	expected                 int64
	errMsg                   atomic.Value
	sem                      chan struct{}
}

func (s *Scanner) walk(n *Node, path string, wg *sync.WaitGroup) {
	if s.cancel.Load() {
		return
	}
	s.cur.Store(path)
	var entries []os.DirEntry
	f, err := os.Open(longPath(path))
	if err == nil {
		entries, err = f.ReadDir(-1)
		f.Close()
	}
	if err != nil {
		s.errs.Add(1)
		n.Err = true
		if n.Parent == nil && len(entries) == 0 {
			s.errMsg.Store(err.Error())
		}
	}
	n.Children = make([]*Node, 0, len(entries))
	var subs []*Node
	var nf, nb int64
	for _, e := range entries {
		if e.Type()&(os.ModeSymlink|os.ModeIrregular) != 0 {
			continue // junctions/symlinks: avoid loops and double counting
		}
		info, err := e.Info()
		if err != nil {
			s.errs.Add(1)
			continue
		}
		c := &Node{Name: e.Name(), Parent: n, Mod: info.ModTime().Unix()}
		if e.IsDir() {
			c.IsDir = true
			subs = append(subs, c)
		} else {
			c.Size = info.Size()
			nf++
			nb += c.Size
		}
		n.Children = append(n.Children, c)
	}
	s.files.Add(nf)
	s.bytes.Add(nb)
	s.dirs.Add(int64(len(subs)))
	for _, c := range subs {
		cp := filepath.Join(path, c.Name)
		select {
		case s.sem <- struct{}{}:
			wg.Add(1)
			go func(c *Node, cp string) {
				defer wg.Done()
				defer func() { <-s.sem }()
				s.walk(c, cp, wg)
			}(c, cp)
		default:
			s.walk(c, cp, wg)
		}
	}
}

type State struct {
	mu       sync.RWMutex
	root     *Node
	byID     []*Node
	sc       *Scanner
	scanning bool
}

func (st *State) node(id int) *Node {
	if id < 0 || id >= len(st.byID) {
		return nil
	}
	n := st.byID[id]
	if n == nil || n.Dead {
		return nil
	}
	return n
}

func (st *State) StartScan(path string) error {
	path = filepath.Clean(path)
	if fi, err := os.Stat(path); err != nil || !fi.IsDir() {
		return errors.New("not a readable folder: " + path)
	}
	st.mu.Lock()
	if st.scanning {
		st.mu.Unlock()
		return errors.New("a scan is already running")
	}
	sc := &Scanner{start: time.Now(), root: path, sem: make(chan struct{}, 48)}
	sc.cur.Store(path)
	for _, d := range listDrives() {
		if strings.EqualFold(d.Path, path) {
			sc.expected = d.Total - d.Free
		}
	}
	st.sc, st.scanning = sc, true
	st.mu.Unlock()

	go func() {
		root := &Node{Name: path, IsDir: true}
		var wg sync.WaitGroup
		sc.walk(root, path, &wg)
		wg.Wait()
		if !sc.cancel.Load() {
			aggregate(root)
			var idx []*Node
			walkTree(root, func(n *Node) bool {
				n.ID = int32(len(idx))
				idx = append(idx, n)
				return true
			})
			st.mu.Lock()
			st.root, st.byID = root, idx
			st.mu.Unlock()
		}
		sc.end.Store(time.Now().UnixNano())
		sc.done.Store(true)
		st.mu.Lock()
		st.scanning = false
		st.mu.Unlock()
	}()
	return nil
}

// detach removes a deleted node and fixes ancestor totals. Caller holds the write lock.
func (st *State) detach(n *Node) {
	if n.Parent == nil || n.Dead {
		return
	}
	for a := n.Parent; a != nil; a = a.Parent {
		a.Size -= n.Size
		a.Files -= n.Files
		if n.IsDir {
			a.Dirs -= n.Dirs + 1
		}
	}
	p := n.Parent
	for i, c := range p.Children {
		if c == n {
			p.Children = append(p.Children[:i], p.Children[i+1:]...)
			break
		}
	}
	walkTree(n, func(x *Node) bool { x.Dead = true; return true })
}
