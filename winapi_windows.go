// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/binary"
	"os"
	"os/exec"
	"runtime"
	"syscall"
	"time"
	"unsafe"
)

var (
	kernel32               = syscall.NewLazyDLL("kernel32.dll")
	shell32                = syscall.NewLazyDLL("shell32.dll")
	procGetLogicalDrives   = kernel32.NewProc("GetLogicalDrives")
	procGetDriveType       = kernel32.NewProc("GetDriveTypeW")
	procGetDiskFreeSpaceEx = kernel32.NewProc("GetDiskFreeSpaceExW")
	procSHFileOperation    = shell32.NewProc("SHFileOperationW")
)

type Drive struct {
	Path  string `json:"path"`
	Total int64  `json:"total"`
	Free  int64  `json:"free"`
}

func listDrives() []Drive {
	var out []Drive
	mask, _, _ := procGetLogicalDrives.Call()
	for i := 0; i < 26; i++ {
		if mask&(1<<uint(i)) == 0 {
			continue
		}
		p := string(rune('A'+i)) + `:\`
		pp, _ := syscall.UTF16PtrFromString(p)
		t, _, _ := procGetDriveType.Call(uintptr(unsafe.Pointer(pp)))
		if t != 2 && t != 3 && t != 4 {
			continue
		}
		var free, total, tf uint64
		r, _, _ := procGetDiskFreeSpaceEx.Call(uintptr(unsafe.Pointer(pp)),
			uintptr(unsafe.Pointer(&free)), uintptr(unsafe.Pointer(&total)), uintptr(unsafe.Pointer(&tf)))
		if r == 0 {
			continue
		}
		out = append(out, Drive{p, int64(total), int64(free)})
	}
	return out
}

// recycle moves a file or folder to the Recycle Bin.
//
// SHFILEOPSTRUCTW is 8-byte aligned on 64-bit Windows but byte-packed on
// 32-bit Windows, so it is laid out by hand for the running architecture.
func recycle(path string) error {
	u, err := syscall.UTF16FromString(path)
	if err != nil {
		return err
	}
	u = append(u, 0) // double NUL terminated
	from := uintptr(unsafe.Pointer(&u[0]))
	const flags = 0x40 | 0x10 | 0x4 | 0x400 // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI

	var op [56]byte
	var abortedAt int
	if unsafe.Sizeof(uintptr(0)) == 8 { // hwnd 0, wFunc 8, pFrom 16, pTo 24, fFlags 32, fAnyOperationsAborted 36
		binary.LittleEndian.PutUint32(op[8:], 3) // FO_DELETE
		binary.LittleEndian.PutUint64(op[16:], uint64(from))
		binary.LittleEndian.PutUint16(op[32:], flags)
		abortedAt = 36
	} else { // packed: hwnd 0, wFunc 4, pFrom 8, pTo 12, fFlags 16, fAnyOperationsAborted 18
		binary.LittleEndian.PutUint32(op[4:], 3)
		binary.LittleEndian.PutUint32(op[8:], uint32(from))
		binary.LittleEndian.PutUint16(op[16:], flags)
		abortedAt = 18
	}
	r, _, _ := procSHFileOperation.Call(uintptr(unsafe.Pointer(&op[0])))
	runtime.KeepAlive(u)
	if r != 0 || binary.LittleEndian.Uint32(op[abortedAt:]) != 0 {
		return syscall.Errno(0x1F)
	}
	return nil
}

func explorerSelect(path string) {
	c := exec.Command("explorer.exe")
	c.SysProcAttr = &syscall.SysProcAttr{CmdLine: `explorer.exe /select,"` + path + `"`}
	c.Start()
}

func explorerOpen(path string) {
	c := exec.Command("explorer.exe")
	c.SysProcAttr = &syscall.SysProcAttr{CmdLine: `explorer.exe "` + path + `"`}
	c.Start()
}

var (
	ole32                = syscall.NewLazyDLL("ole32.dll")
	user32               = syscall.NewLazyDLL("user32.dll")
	procCoInitializeEx   = ole32.NewProc("CoInitializeEx")
	procCoUninitialize   = ole32.NewProc("CoUninitialize")
	procCoCreateInstance = ole32.NewProc("CoCreateInstance")
	procCoTaskMemFree    = ole32.NewProc("CoTaskMemFree")
	procFindWindow       = user32.NewProc("FindWindowW")
	procSetWindowPos     = user32.NewProc("SetWindowPos")
	procSetForeground    = user32.NewProc("SetForegroundWindow")
	procKeybdEvent       = user32.NewProc("keybd_event")
)

type guid struct {
	D1 uint32
	D2 uint16
	D3 uint16
	D4 [8]byte
}

var (
	clsidFileOpenDialog = guid{0xDC1C5A9C, 0xE88A, 0x4DDE, [8]byte{0xA5, 0xA1, 0x60, 0xF8, 0x2A, 0x20, 0xAE, 0xF7}}
	iidFileOpenDialog   = guid{0xd57c7288, 0xd4ad, 0x4768, [8]byte{0xbe, 0x02, 0x9d, 0x96, 0x95, 0x32, 0xd9, 0x60}}
)

// vcall invokes method idx of a COM object through its vtable.
func vcall(obj unsafe.Pointer, idx int, args ...uintptr) uintptr {
	vtbl := *(*unsafe.Pointer)(obj)
	fn := *(*uintptr)(unsafe.Add(vtbl, idx*int(unsafe.Sizeof(uintptr(0)))))
	r, _, _ := syscall.SyscallN(fn, append([]uintptr{uintptr(obj)}, args...)...)
	return r
}

// pickFolder shows the standard Windows 11 "Select Folder" dialog (IFileOpenDialog).
func pickFolder() string {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	procCoInitializeEx.Call(0, 2) // COINIT_APARTMENTTHREADED
	defer procCoUninitialize.Call()

	// No owner window (a foreign owner dismisses the dialog), so find it and force it to the front.
	const dlgTitle = "Select a folder to scan"
	done := make(chan struct{})
	defer close(done)
	go func() {
		tp, _ := syscall.UTF16PtrFromString(dlgTitle)
		for i := 0; i < 100; i++ {
			select {
			case <-done:
				return
			case <-time.After(30 * time.Millisecond):
			}
			if h, _, _ := procFindWindow.Call(0, uintptr(unsafe.Pointer(tp))); h != 0 {
				procKeybdEvent.Call(0x12, 0, 0, 0) // tap Alt so Windows allows the focus change
				procKeybdEvent.Call(0x12, 0, 2, 0)
				procSetWindowPos.Call(h, ^uintptr(0), 0, 0, 0, 0, 0x43) // HWND_TOPMOST, NOMOVE|NOSIZE|SHOWWINDOW
				procSetForeground.Call(h)
				return
			}
		}
	}()

	var dlg unsafe.Pointer
	hr, _, _ := procCoCreateInstance.Call(uintptr(unsafe.Pointer(&clsidFileOpenDialog)), 0, 1,
		uintptr(unsafe.Pointer(&iidFileOpenDialog)), uintptr(unsafe.Pointer(&dlg)))
	if hr != 0 || dlg == nil {
		return ""
	}
	defer vcall(dlg, 2) // Release
	var opts uint32
	vcall(dlg, 10, uintptr(unsafe.Pointer(&opts))) // GetOptions
	vcall(dlg, 9, uintptr(opts|0x20|0x40))         // SetOptions: PICKFOLDERS | FORCEFILESYSTEM
	title, _ := syscall.UTF16PtrFromString(dlgTitle)
	vcall(dlg, 17, uintptr(unsafe.Pointer(title))) // SetTitle
	if vcall(dlg, 3, 0) != 0 {                     // Show (non-zero = cancelled)
		return ""
	}
	var item unsafe.Pointer
	if vcall(dlg, 20, uintptr(unsafe.Pointer(&item))) != 0 || item == nil { // GetResult
		return ""
	}
	defer vcall(item, 2)
	var ptr *uint16
	if vcall(item, 5, 0x80058000, uintptr(unsafe.Pointer(&ptr))) != 0 || ptr == nil { // GetDisplayName(FILESYSPATH)
		return ""
	}
	defer procCoTaskMemFree.Call(uintptr(unsafe.Pointer(ptr)))
	var buf []uint16
	for p := unsafe.Pointer(ptr); *(*uint16)(p) != 0; p = unsafe.Add(p, 2) {
		buf = append(buf, *(*uint16)(p))
	}
	return syscall.UTF16ToString(buf)
}

func openBrowser(url string) {
	for _, p := range []string{
		`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`,
		`C:\Program Files\Microsoft\Edge\Application\msedge.exe`,
		`C:\Program Files\Google\Chrome\Application\chrome.exe`,
		`C:\Program Files (x86)\Google\Chrome\Application\chrome.exe`,
	} {
		if _, err := os.Stat(p); err == nil {
			if exec.Command(p, "--app="+url, "--window-size=1360,860").Start() == nil {
				return
			}
		}
	}
	exec.Command("rundll32", "url.dll,FileProtocolHandler", url).Start()
}
