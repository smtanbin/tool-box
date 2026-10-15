// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/binary"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"sort"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

// Disk information in the style of CrystalDiskInfo, read with DeviceIoControl (no cgo).
// Device descriptors, layout and temperature work as a normal user; SMART / NVMe health
// usually needs Administrator, and PowerShell's Get-PhysicalDisk fills in health otherwise.

const (
	ioctlStorageQueryProperty   = 0x2D1400
	ioctlDiskGetDriveGeometryEx = 0x700A0
	ioctlDiskGetDriveLayoutEx   = 0x70050
	ioctlVolumeGetDiskExtents   = 0x560000
	smartRcvDriveData           = 0x7C088
	smartGetVersion             = 0x74080
)

var (
	procIsUserAnAdmin     = shell32.NewProc("IsUserAnAdmin")
	procShellExecute      = shell32.NewProc("ShellExecuteW")
	procGetVolumeInfo     = kernel32.NewProc("GetVolumeInformationW")
	procGetLogicalDrives2 = procGetLogicalDrives
	procQueryDosDevice    = kernel32.NewProc("QueryDosDeviceW")
	procSetErrorMode      = kernel32.NewProc("SetErrorMode")
)

type DiskAttr struct {
	ID     int    `json:"id"`
	Name   string `json:"name"`
	Cur    int    `json:"cur"`
	Worst  int    `json:"worst"`
	Thr    int    `json:"thr"`
	Raw    string `json:"raw"`
	RawHex string `json:"rawHex"`
	Status string `json:"status"` // ok | caution | bad
}

type DiskPart struct {
	Num    int    `json:"num"`
	Offset int64  `json:"offset"`
	Size   int64  `json:"size"`
	Type   string `json:"type"`
	Name   string `json:"name"`
	Letter string `json:"letter"`
	Label  string `json:"label"`
	FS     string `json:"fs"`
	Free   int64  `json:"free"`
}

type DiskInfo struct {
	Index      int        `json:"index"`
	Model      string     `json:"model"`
	Vendor     string     `json:"vendor"`
	Serial     string     `json:"serial"`
	Firmware   string     `json:"firmware"`
	Bus        string     `json:"bus"`
	Media      string     `json:"media"`
	Removable  bool       `json:"removable"`
	Size       int64      `json:"size"`
	Style      string     `json:"style"`
	Parts      []DiskPart `json:"parts"`
	Letters    []string   `json:"letters"`
	Source     string     `json:"source"`
	Attrs      []DiskAttr `json:"attrs"`
	Health     string     `json:"health"` // Good | Caution | Bad | Unknown
	HealthPct  int        `json:"healthPct"`
	Reasons    []string   `json:"reasons"`
	Temp       int        `json:"temp"` // °C, 0 = unknown
	TempWarn   int        `json:"tempWarn"`
	TempCrit   int        `json:"tempCrit"`
	PowerHours int64      `json:"powerHours"`
	PowerCount int64      `json:"powerCount"`
	HostReads  int64      `json:"hostReads"`
	HostWrites int64      `json:"hostWrites"`
	NeedAdmin  bool       `json:"needAdmin"`
	Notes      []string   `json:"notes"`
}

var busNames = map[uint32]string{1: "SCSI", 2: "ATAPI", 3: "ATA", 4: "IEEE 1394", 5: "SSA", 6: "Fibre Channel", 7: "USB", 8: "RAID",
	9: "iSCSI", 10: "SAS", 11: "SATA", 12: "SD", 13: "MMC", 14: "Virtual", 15: "File-backed virtual", 16: "Storage Spaces", 17: "NVMe", 18: "SCM", 19: "UFS"}

func isAdmin() bool { r, _, _ := procIsUserAnAdmin.Call(); return r != 0 }

func openDev(path string, access uint32) (syscall.Handle, error) {
	p, _ := syscall.UTF16PtrFromString(path)
	return syscall.CreateFile(p, access, syscall.FILE_SHARE_READ|syscall.FILE_SHARE_WRITE, nil, syscall.OPEN_EXISTING, 0, 0)
}

func ioctl(h syscall.Handle, code uint32, in []byte, outSize int) ([]byte, error) {
	out := make([]byte, outSize)
	var n uint32
	var inp *byte
	if len(in) > 0 {
		inp = &in[0]
	}
	if err := syscall.DeviceIoControl(h, code, inp, uint32(len(in)), &out[0], uint32(len(out)), &n, nil); err != nil {
		return nil, err
	}
	return out[:n], nil
}

func cstr(b []byte, off uint32) string {
	if off == 0 || int(off) >= len(b) {
		return ""
	}
	e := int(off)
	for e < len(b) && b[e] != 0 {
		e++
	}
	return strings.TrimSpace(string(b[off:e]))
}

func le16(b []byte, o int) uint16 { return binary.LittleEndian.Uint16(b[o:]) }
func le32(b []byte, o int) uint32 { return binary.LittleEndian.Uint32(b[o:]) }
func le64(b []byte, o int) uint64 { return binary.LittleEndian.Uint64(b[o:]) }

func queryProp(h syscall.Handle, id uint32, extra []byte, outSize int) ([]byte, error) {
	in := make([]byte, 8+len(extra)+4)
	binary.LittleEndian.PutUint32(in, id) // PropertyId; QueryType 0 = PropertyStandardQuery
	copy(in[8:], extra)
	return ioctl(h, ioctlStorageQueryProperty, in, outSize)
}

func listDisks() []*DiskInfo {
	procSetErrorMode.Call(0x0001 | 0x8000) // no "insert a disk" boxes: SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX
	var disks []*DiskInfo
	for i := 0; i < 32; i++ {
		h, err := openDev(fmt.Sprintf(`\\.\PhysicalDrive%d`, i), 0)
		if err != nil {
			continue
		}
		d := &DiskInfo{Index: i, Health: "Unknown", Parts: []DiskPart{}, Letters: []string{}, Attrs: []DiskAttr{}, Reasons: []string{}, Notes: []string{}}
		readDescriptor(h, d)
		if b, err := ioctl(h, ioctlDiskGetDriveGeometryEx, nil, 256); err == nil && len(b) >= 32 {
			d.Size = int64(le64(b, 24))
		}
		readLayout(h, d)
		readTemperature(h, d)
		syscall.CloseHandle(h)
		readSMART(d)
		disks = append(disks, d)
	}
	mapLetters(disks)
	return disks
}

func readDescriptor(h syscall.Handle, d *DiskInfo) {
	b, err := queryProp(h, 0, nil, 1024) // StorageDeviceProperty
	if err != nil || len(b) < 36 {
		return
	}
	d.Removable = b[10] != 0
	d.Vendor, d.Model, d.Firmware, d.Serial = cstr(b, le32(b, 12)), cstr(b, le32(b, 16)), cstr(b, le32(b, 20)), strings.TrimRight(cstr(b, le32(b, 24)), ". ")
	if d.Vendor != "" && !strings.HasPrefix(d.Model, d.Vendor) && d.Vendor != "ATA" && d.Vendor != "NVMe" {
		d.Model = d.Vendor + " " + d.Model
	}
	d.Bus = busNames[le32(b, 28)]
	if d.Bus == "" {
		d.Bus = fmt.Sprintf("Type %d", le32(b, 28))
	}
	if sp, err := queryProp(h, 7, nil, 64); err == nil && len(sp) >= 9 { // StorageDeviceSeekPenaltyProperty
		if sp[8] != 0 {
			d.Media = "HDD"
		} else {
			d.Media = "SSD"
		}
	}
}

var gptTypes = map[string]string{
	"C12A7328-F81F-11D2-BA4B-00A0C93EC93B": "EFI system", "E3C9E316-0B5C-4DB8-817D-F92DF00215AE": "Microsoft reserved",
	"EBD0A0A2-B9E5-4433-87C0-68B6B72699C7": "Basic data", "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC": "Recovery",
	"5808C8AA-7E8F-42E0-85D2-E1E90434CFB3": "LDM metadata", "AF9B60A0-1431-4F62-BC68-3311714A69AD": "LDM data",
	"E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D": "Storage Spaces", "0FC63DAF-8483-4772-8E79-3D69D8477DE4": "Linux filesystem",
	"0657FD6D-A4AB-43C4-84E5-0933C84B4F4F": "Linux swap",
}

func guidStr(b []byte) string {
	return strings.ToUpper(fmt.Sprintf("%08x-%04x-%04x-%x-%x", le32(b, 0), le16(b, 4), le16(b, 6), b[8:10], b[10:16]))
}

func readLayout(h syscall.Handle, d *DiskInfo) {
	b, err := ioctl(h, ioctlDiskGetDriveLayoutEx, nil, 48+144*128)
	if err != nil || len(b) < 48 {
		return
	}
	style, count := le32(b, 0), int(le32(b, 4))
	d.Style = map[uint32]string{0: "MBR", 1: "GPT", 2: "RAW"}[style]
	for i := 0; i < count && 48+(i+1)*144 <= len(b); i++ {
		p := b[48+i*144:]
		pd := DiskPart{Num: int(le32(p, 24)), Offset: int64(le64(p, 8)), Size: int64(le64(p, 16))}
		if pd.Size == 0 {
			continue
		}
		if le32(p, 0) == 1 { // GPT
			g := guidStr(p[32:48])
			pd.Type = gptTypes[g]
			if pd.Type == "" {
				pd.Type = "GPT " + g
			}
			pd.Name = syscall.UTF16ToString((*[36]uint16)(unsafe.Pointer(&p[32+40]))[:])
		} else {
			t := p[32]
			if t == 0 {
				continue
			}
			pd.Type = map[byte]string{7: "NTFS / exFAT", 0x0B: "FAT32", 0x0C: "FAT32 (LBA)", 0x27: "Recovery", 0x05: "Extended", 0x0F: "Extended (LBA)", 0x83: "Linux", 0xEE: "GPT protective"}[t]
			if pd.Type == "" {
				pd.Type = fmt.Sprintf("Type 0x%02X", t)
			}
		}
		d.Parts = append(d.Parts, pd)
	}
	sort.Slice(d.Parts, func(i, j int) bool { return d.Parts[i].Offset < d.Parts[j].Offset })
}

// mapLetters matches every drive letter's volume extent back to its disk partition.
func mapLetters(disks []*DiskInfo) {
	mask, _, _ := procGetLogicalDrives2.Call()
	for i := 0; i < 26; i++ {
		if mask&(1<<uint(i)) == 0 {
			continue
		}
		letter := string(rune('A'+i)) + ":"
		rp, _ := syscall.UTF16PtrFromString(letter + `\`)
		if t, _, _ := procGetDriveType.Call(uintptr(unsafe.Pointer(rp))); t != 2 && t != 3 {
			continue // only removable and fixed volumes; network shares can stall for seconds
		}
		var target [512]uint16
		lp, _ := syscall.UTF16PtrFromString(letter)
		if n, _, _ := procQueryDosDevice.Call(uintptr(unsafe.Pointer(lp)), uintptr(unsafe.Pointer(&target[0])), 512); n > 0 &&
			strings.HasPrefix(syscall.UTF16ToString(target[:]), `\??\`) {
			continue // a SUBST drive pointing at a folder, not a volume
		}
		h, err := openDev(`\\.\`+letter, 0)
		if err != nil {
			continue
		}
		b, err := ioctl(h, ioctlVolumeGetDiskExtents, nil, 8+24*8)
		syscall.CloseHandle(h)
		if err != nil || len(b) < 32 {
			continue
		}
		var label, fs [261]uint16
		root, _ := syscall.UTF16PtrFromString(letter + `\`)
		procGetVolumeInfo.Call(uintptr(unsafe.Pointer(root)), uintptr(unsafe.Pointer(&label[0])), 261, 0, 0, 0, uintptr(unsafe.Pointer(&fs[0])), 261)
		var free, total, tf uint64
		procGetDiskFreeSpaceEx.Call(uintptr(unsafe.Pointer(root)), uintptr(unsafe.Pointer(&free)), uintptr(unsafe.Pointer(&total)), uintptr(unsafe.Pointer(&tf)))
		for e := 0; e < int(le32(b, 0)) && 8+(e+1)*24 <= len(b); e++ {
			num, off := int(le32(b, 8+e*24)), int64(le64(b, 16+e*24))
			for _, d := range disks {
				if d.Index != num {
					continue
				}
				for k := range d.Parts { // virtual volumes (e.g. Google Drive) match no partition and are skipped
					if d.Parts[k].Offset == off {
						d.Letters = append(d.Letters, letter)
						d.Parts[k].Letter, d.Parts[k].Label, d.Parts[k].FS, d.Parts[k].Free = letter, syscall.UTF16ToString(label[:]), syscall.UTF16ToString(fs[:]), int64(free)
					}
				}
			}
		}
	}
}

func readTemperature(h syscall.Handle, d *DiskInfo) {
	b, err := queryProp(h, 52, nil, 512) // StorageDeviceTemperatureProperty
	if err != nil || len(b) < 24 {
		return
	}
	d.TempCrit, d.TempWarn = int(int16(le16(b, 8))), int(int16(le16(b, 10)))
	if int(le16(b, 12)) > 0 && len(b) >= 24+20 {
		if t := int(int16(le16(b, 26))); t > -50 && t < 200 && t != 0 {
			d.Temp = t
		}
	}
}

func readSMART(d *DiskInfo) {
	h, err := openDev(fmt.Sprintf(`\\.\PhysicalDrive%d`, d.Index), syscall.GENERIC_READ|syscall.GENERIC_WRITE)
	if err != nil {
		d.NeedAdmin = true
		if d.Bus != "NVMe" {
			return
		}
		// some NVMe drivers answer the health-log query on a query-only handle
		if h, err = openDev(fmt.Sprintf(`\\.\PhysicalDrive%d`, d.Index), 0); err != nil {
			return
		}
	}
	defer syscall.CloseHandle(h)
	if d.Bus == "NVMe" {
		readNVMe(h, d)
		return
	}
	readATA(h, d)
}

/* ---------- NVMe ---------- */

func u128(b []byte, o int) int64 { // the counters never need more than 63 bits in practice
	if le64(b, o+8) != 0 {
		return 1<<63 - 1
	}
	return int64(le64(b, o))
}

func readNVMe(h syscall.Handle, d *DiskInfo) {
	extra := make([]byte, 40)                     // STORAGE_PROTOCOL_SPECIFIC_DATA
	binary.LittleEndian.PutUint32(extra[0:], 3)   // ProtocolTypeNvme
	binary.LittleEndian.PutUint32(extra[4:], 2)   // NVMeDataTypeLogPage
	binary.LittleEndian.PutUint32(extra[8:], 2)   // SMART / health information log
	binary.LittleEndian.PutUint32(extra[16:], 40) // ProtocolDataOffset
	binary.LittleEndian.PutUint32(extra[20:], 512)
	in := make([]byte, 8+40+512)
	binary.LittleEndian.PutUint32(in, 50) // StorageDeviceProtocolSpecificProperty
	copy(in[8:], extra)
	out, err := ioctl(h, ioctlStorageQueryProperty, in, len(in))
	if err != nil || len(out) < 48+512 {
		d.Notes = append(d.Notes, "The NVMe health log could not be read: "+errText(err))
		return
	}
	l := out[48 : 48+512]
	d.Source = "NVMe SMART / health log"
	tempK := int(le16(l, 1))
	if tempK > 0 {
		d.Temp = tempK - 273
	}
	spare, spareThr, used := int(l[3]), int(l[4]), int(l[5])
	d.HostReads, d.HostWrites = u128(l, 32)*512000, u128(l, 48)*512000
	d.PowerCount, d.PowerHours = u128(l, 112), u128(l, 128)
	media := u128(l, 160)
	rows := []struct {
		name string
		v    int64
		st   string
	}{
		{"Critical Warning", int64(l[0]), map[bool]string{true: "bad", false: "ok"}[l[0] != 0]},
		{"Composite Temperature", int64(max(0, d.Temp)), "ok"},
		{"Available Spare", int64(spare), map[bool]string{true: "bad", false: "ok"}[spare < spareThr]},
		{"Available Spare Threshold", int64(spareThr), "ok"},
		{"Percentage Used", int64(used), map[bool]string{true: "caution", false: "ok"}[used >= 90]},
		{"Data Units Read", u128(l, 32), "ok"},
		{"Data Units Written", u128(l, 48), "ok"},
		{"Host Read Commands", u128(l, 64), "ok"},
		{"Host Write Commands", u128(l, 80), "ok"},
		{"Controller Busy Time", u128(l, 96), "ok"},
		{"Power Cycles", d.PowerCount, "ok"},
		{"Power On Hours", d.PowerHours, "ok"},
		{"Unsafe Shutdowns", u128(l, 144), "ok"},
		{"Media and Data Integrity Errors", media, map[bool]string{true: "caution", false: "ok"}[media > 0]},
		{"Number of Error Information Log Entries", u128(l, 176), "ok"},
	}
	for i, r := range rows {
		d.Attrs = append(d.Attrs, DiskAttr{ID: i + 1, Name: r.name, Raw: fmt.Sprint(r.v), RawHex: fmt.Sprintf("%012X", r.v), Status: r.st, Cur: -1, Worst: -1, Thr: -1})
	}
	d.HealthPct = max(0, 100-used)
	switch {
	case l[0] != 0:
		d.Health = "Bad"
		d.Reasons = append(d.Reasons, nvmeWarnings(l[0])...)
	case spare < spareThr:
		d.Health = "Bad"
		d.Reasons = append(d.Reasons, "Available spare is below its threshold")
	case used >= 90 || media > 0:
		d.Health = "Caution"
		if used >= 90 {
			d.Reasons = append(d.Reasons, fmt.Sprintf("%d%% of rated endurance used", used))
		}
		if media > 0 {
			d.Reasons = append(d.Reasons, fmt.Sprintf("%d media / data integrity errors", media))
		}
	default:
		d.Health = "Good"
	}
}

func nvmeWarnings(w byte) []string {
	var out []string
	for bit, s := range []string{"Available spare below threshold", "Temperature out of range", "Reliability degraded", "Media is read-only", "Volatile memory backup failed"} {
		if w&(1<<bit) != 0 {
			out = append(out, s)
		}
	}
	return out
}

/* ---------- ATA SMART ---------- */

var ataNames = map[int]string{1: "Read Error Rate", 2: "Throughput Performance", 3: "Spin-Up Time", 4: "Start/Stop Count", 5: "Reallocated Sectors Count",
	6: "Read Channel Margin", 7: "Seek Error Rate", 8: "Seek Time Performance", 9: "Power-On Hours", 10: "Spin Retry Count", 11: "Recalibration Retries",
	12: "Power Cycle Count", 13: "Soft Read Error Rate", 22: "Current Helium Level", 160: "Uncorrectable Sector Count", 161: "Valid Spare Blocks",
	163: "Initial Bad Blocks", 164: "Total Erase Count", 165: "Max Erase Count", 166: "Min Erase Count", 167: "Average Erase Count",
	168: "Max Erase Count of Spec", 169: "Remaining Life", 170: "Available Reserved Space", 171: "Program Fail Count", 172: "Erase Fail Count",
	173: "Wear Leveling Count", 174: "Unexpected Power Loss Count", 175: "Power Loss Protection Failure", 176: "Erase Fail Count (chip)",
	177: "Wear Range Delta", 179: "Used Reserved Block Count (total)", 180: "Unused Reserved Block Count (total)", 181: "Program Fail Count (total)",
	182: "Erase Fail Count (total)", 183: "Runtime Bad Block / SATA Downshift Errors", 184: "End-to-End Error", 187: "Reported Uncorrectable Errors",
	188: "Command Timeout", 189: "High Fly Writes", 190: "Airflow Temperature", 191: "G-Sense Error Rate", 192: "Power-off Retract Count",
	193: "Load/Unload Cycle Count", 194: "Temperature", 195: "Hardware ECC Recovered", 196: "Reallocation Event Count", 197: "Current Pending Sector Count",
	198: "Uncorrectable Sector Count", 199: "UltraDMA CRC Error Count", 200: "Write Error Rate", 201: "Soft Read Error Rate", 202: "Data Address Mark Errors",
	206: "Flying Height", 220: "Disk Shift", 222: "Loaded Hours", 223: "Load Retry Count", 224: "Load Friction", 225: "Load/Unload Cycle Count",
	226: "Load-in Time", 230: "GMR Head Amplitude / Drive Life Protection", 231: "SSD Life Left / Temperature", 232: "Endurance Remaining / Available Reserved Space",
	233: "Media Wearout Indicator / Power-On Hours", 234: "Average / Maximum Erase Count", 235: "Good Block Count", 240: "Head Flying Hours",
	241: "Total LBAs Written", 242: "Total LBAs Read", 243: "Total LBAs Written (expanded)", 244: "Total LBAs Read (expanded)", 249: "NAND Writes (1 GiB)",
	250: "Read Error Retry Rate", 251: "Minimum Spares Remaining", 252: "Newly Added Bad Flash Block", 254: "Free Fall Protection"}

func smartCmd(h syscall.Handle, feature byte, drive byte) ([]byte, error) {
	in := make([]byte, 33)
	binary.LittleEndian.PutUint32(in[0:], 512)
	in[4], in[5], in[6], in[7], in[8], in[9], in[10] = feature, 1, 1, 0x4F, 0xC2, 0xA0|((drive&1)<<4), 0xB0
	in[12] = drive
	out, err := ioctl(h, smartRcvDriveData, in, 16+512)
	if err != nil {
		return nil, err
	}
	if len(out) < 16+362 {
		return nil, fmt.Errorf("short SMART reply")
	}
	return out[16:], nil
}

func readATA(h syscall.Handle, d *DiskInfo) {
	drive := byte(0)
	if v, err := ioctl(h, smartGetVersion, nil, 24); err == nil && len(v) >= 5 {
		drive = v[4] // bIDEDeviceMap: the drive number to address
		if drive > 3 {
			drive = 0
		}
	}
	data, err := smartCmd(h, 0xD0, drive)
	if err != nil {
		if d.Bus == "USB" {
			d.Notes = append(d.Notes, "This USB enclosure does not pass SMART commands through to Windows.")
		} else {
			d.Notes = append(d.Notes, "SMART data could not be read: "+errText(err))
		}
		return
	}
	thr := map[int]int{}
	if t, err := smartCmd(h, 0xD1, drive); err == nil {
		for i := 0; i < 30; i++ {
			if o := 2 + i*12; t[o] != 0 {
				thr[int(t[o])] = int(t[o+1])
			}
		}
	}
	d.Source = "ATA SMART"
	bad, caution := false, false
	for i := 0; i < 30; i++ {
		o := 2 + i*12
		id := int(data[o])
		if id == 0 {
			continue
		}
		raw6 := data[o+5 : o+11]
		var raw int64
		for k := 5; k >= 0; k-- {
			raw = raw<<8 | int64(raw6[k])
		}
		a := DiskAttr{ID: id, Name: ataNames[id], Cur: int(data[o+3]), Worst: int(data[o+4]), Thr: thr[id], Raw: fmt.Sprint(raw), RawHex: fmt.Sprintf("%012X", raw), Status: "ok"}
		if a.Name == "" {
			a.Name = "Vendor specific"
		}
		switch id {
		case 9:
			d.PowerHours = raw & 0xFFFFFFFF
		case 12:
			d.PowerCount = raw & 0xFFFFFFFF
		case 194, 190:
			if t := int(raw & 0xFF); t > 0 && t < 120 && (id == 194 || d.Temp == 0) {
				d.Temp = t
			}
		case 241:
			d.HostWrites = raw * 512
		case 242:
			d.HostReads = raw * 512
		case 169, 231, 233:
			if d.Media == "SSD" && a.Cur > 0 && a.Cur <= 100 {
				d.HealthPct = a.Cur
			}
		}
		if a.Thr > 0 && a.Thr < 0xFE && a.Cur <= a.Thr {
			a.Status, bad = "bad", true
			d.Reasons = append(d.Reasons, fmt.Sprintf("%s (%02X) is at or below its threshold", a.Name, id))
		} else if (id == 5 || id == 196 || id == 197 || id == 198) && raw&0xFFFFFFFF > 0 {
			a.Status, caution = "caution", true
			d.Reasons = append(d.Reasons, fmt.Sprintf("%s: %d", a.Name, raw&0xFFFFFFFF))
		}
		d.Attrs = append(d.Attrs, a)
	}
	switch {
	case bad:
		d.Health = "Bad"
	case caution:
		d.Health = "Caution"
	default:
		d.Health = "Good"
	}
	if d.HealthPct == 0 && d.Media == "SSD" {
		d.HealthPct = -1
	}
}

func errText(err error) string {
	if err == nil {
		return "no data returned"
	}
	if err == syscall.ERROR_ACCESS_DENIED {
		return "access denied (run as administrator)"
	}
	return err.Error()
}

/* ---------- PowerShell fallback (health status works as a normal user) ---------- */

type psDisk struct {
	DeviceId     string
	HealthStatus string
	MediaType    string
}

var (
	psMu    sync.Mutex
	psCache map[int]psDisk
	psAt    time.Time
)

// psHealth asks Windows (MSFT_PhysicalDisk) for each disk's overall health; cached for a minute.
func psHealth() map[int]psDisk {
	psMu.Lock()
	defer psMu.Unlock()
	if psCache != nil && time.Since(psAt) < time.Minute {
		return psCache
	}
	script := `Get-PhysicalDisk|Select-Object DeviceId,@{n='HealthStatus';e={"$($_.HealthStatus)"}},@{n='MediaType';e={"$($_.MediaType)"}}|ConvertTo-Json -Compress`
	cmd := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
	cmd.Stdin = strings.NewReader("")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000} // CREATE_NO_WINDOW
	var outBuf strings.Builder
	cmd.Stdout = &outBuf
	if cmd.Start() != nil {
		return nil
	}
	done := make(chan struct{})
	go func() { cmd.Wait(); close(done) }()
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		cmd.Process.Kill()
		return nil
	}
	b := []byte(outBuf.String())
	b = []byte(strings.TrimSpace(string(b)))
	var list []psDisk
	if len(b) > 0 && b[0] == '{' {
		var one psDisk
		if json.Unmarshal(b, &one) == nil {
			list = []psDisk{one}
		}
	} else {
		json.Unmarshal(b, &list)
	}
	out := map[int]psDisk{}
	for _, p := range list {
		var n int
		if _, err := fmt.Sscan(p.DeviceId, &n); err == nil {
			out[n] = p
		}
	}
	psCache, psAt = out, time.Now()
	return out
}

func hDisks(w http.ResponseWriter, r *http.Request) {
	t0 := time.Now()
	disks := listDisks()
	t1 := time.Now()
	need := false
	for _, d := range disks {
		if d.Source == "" {
			need = true
		}
	}
	if need {
		ps := psHealth()
		for _, d := range disks {
			p, ok := ps[d.Index]
			if !ok {
				continue
			}
			if d.Media == "" && (p.MediaType == "SSD" || p.MediaType == "HDD") {
				d.Media = p.MediaType
			}
			if d.Source != "" {
				continue
			}
			d.Source = "Windows storage health (Get-PhysicalDisk)"
			switch p.HealthStatus {
			case "Healthy":
				d.Health = "Good"
			case "Warning":
				d.Health = "Caution"
			case "Unhealthy":
				d.Health = "Bad"
			}
		}
	}
	if disks == nil {
		disks = []*DiskInfo{}
	}
	writeJSON(w, map[string]any{"admin": isAdmin(), "disks": disks, "time": time.Now().Unix(),
		"ms": map[string]int64{"drives": t1.Sub(t0).Milliseconds(), "health": time.Since(t1).Milliseconds()}})
}

// hElevate restarts My Tool Box as Administrator (UAC prompt), then exits this copy.
func hElevate(w http.ResponseWriter, r *http.Request) {
	exe, _ := os.Executable()
	verb, _ := syscall.UTF16PtrFromString("runas")
	file, _ := syscall.UTF16PtrFromString(exe)
	params, _ := syscall.UTF16PtrFromString("--tool=disk")
	ret, _, _ := procShellExecute.Call(0, uintptr(unsafe.Pointer(verb)), uintptr(unsafe.Pointer(file)), uintptr(unsafe.Pointer(params)), 0, 1)
	if ret <= 32 {
		fail(w, 400, "Administrator rights were not granted")
		return
	}
	w.WriteHeader(204)
	go func() { time.Sleep(300 * time.Millisecond); os.Exit(0) }()
}
