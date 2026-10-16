// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/binary"
	"fmt"
	"regexp"
	"strings"
	"syscall"
)

// Drive maker, controller and DRAM-cache detection.
//
// Windows has no API that says "this SSD has a DRAM cache", so the answer is built from evidence:
//  1. NVMe Identify Controller: the PCI vendor ID names the controller maker, and HMPRE > 0 means the drive
//     asks the PC for a Host Memory Buffer, which is what DRAM-less NVMe drives do.
//  2. A small database of well-known models with their controller and whether it has DRAM.
//
// The result carries a confidence so the page can say "likely" instead of pretending to be sure.

// pciVendors maps NVMe PCI vendor IDs to the company named in the Identify Controller data.
var pciVendors = map[uint16]string{
	0x144D: "Samsung", 0x15B7: "SanDisk / Western Digital", 0x1344: "Micron", 0x1987: "Phison", 0x1C5C: "SK hynix",
	0x1E0F: "KIOXIA", 0x8086: "Intel", 0x126F: "Silicon Motion", 0x1CC1: "ADATA", 0x1E4B: "MAXIO", 0x1B4B: "Marvell",
	0x10EC: "Realtek", 0x1D97: "Shenzhen Longsys (Lexar)", 0x2646: "Kingston", 0x1D1D: "CNEX Labs", 0x1CC4: "Union Memory",
	0x1BB1: "Seagate", 0x1179: "Toshiba", 0x1C58: "HGST (WD)", 0x025E: "Solidigm", 0x1F40: "Netac", 0x1DEE: "Biwin",
	0x1CD7: "Hikvision", 0x1D79: "Transcend", 0x1B36: "QEMU (virtual)", 0x15AD: "VMware (virtual)",
}

// makers are matched against the model string (case-insensitive), first hit wins. The text is the brand shown.
var makers = []struct {
	re   *regexp.Regexp
	name string
}{
	{regexp.MustCompile(`(?i)samsung|^mz`), "Samsung"},
	{regexp.MustCompile(`(?i)sandisk`), "SanDisk (Western Digital)"},
	{regexp.MustCompile(`(?i)\bwdc\b|western digital|^wd|\bwds\d`), "Western Digital"},
	{regexp.MustCompile(`(?i)crucial|^ct\d`), "Crucial (Micron)"},
	{regexp.MustCompile(`(?i)micron`), "Micron"},
	{regexp.MustCompile(`(?i)kingston|^sa400|^snv|^skc|^sfyr|^sa2000`), "Kingston"},
	{regexp.MustCompile(`(?i)hynix|^hf[smg]`), "SK hynix"},
	{regexp.MustCompile(`(?i)kioxia|^kxg|^kbg|^kcd`), "KIOXIA"},
	{regexp.MustCompile(`(?i)toshiba|^thnsn|^thnsf`), "Toshiba"},
	{regexp.MustCompile(`(?i)solidigm`), "Solidigm"},
	{regexp.MustCompile(`(?i)intel|^ssdpe|^ssdsc`), "Intel"},
	{regexp.MustCompile(`(?i)adata|^as[uxp]\d|^aleg|^agammix`), "ADATA"},
	{regexp.MustCompile(`(?i)corsair|force mp`), "Corsair"},
	{regexp.MustCompile(`(?i)sabrent|^sb-`), "Sabrent"},
	{regexp.MustCompile(`(?i)seagate|^st\d|^za\d`), "Seagate"},
	{regexp.MustCompile(`(?i)lexar`), "Lexar"},
	{regexp.MustCompile(`(?i)team ?group|^tm8|^t253`), "TeamGroup"},
	{regexp.MustCompile(`(?i)patriot`), "Patriot"},
	{regexp.MustCompile(`(?i)\bpny\b`), "PNY"},
	{regexp.MustCompile(`(?i)gigabyte|^ag4`), "Gigabyte"},
	{regexp.MustCompile(`(?i)transcend|^ts\d`), "Transcend"},
	{regexp.MustCompile(`(?i)silicon power|^sp\d`), "Silicon Power"},
	{regexp.MustCompile(`(?i)netac`), "Netac"},
	{regexp.MustCompile(`(?i)lite-?on`), "Lite-On"},
	{regexp.MustCompile(`(?i)plextor`), "Plextor"},
	{regexp.MustCompile(`(?i)hitachi|^hgst|^hts|^hus`), "HGST / Hitachi (WD)"},
	{regexp.MustCompile(`(?i)apacer`), "Apacer"},
	{regexp.MustCompile(`(?i)inland`), "Inland (Micro Center)"},
	{regexp.MustCompile(`(?i)\bhp\b`), "HP"},
	{regexp.MustCompile(`(?i)\bdell\b`), "Dell"},
	{regexp.MustCompile(`(?i)\bmsi\b`), "MSI"},
	{regexp.MustCompile(`(?i)lenovo`), "Lenovo"},
}

// A known model: regex on the model string, the controller used, and whether the design has DRAM.
type knownDrive struct {
	re         *regexp.Regexp
	controller string
	dram       bool
}

func kd(re, controller string, dram bool) knownDrive {
	return knownDrive{regexp.MustCompile(`(?i)` + re), controller, dram}
}

// Only models whose design is widely documented. Vendors swap parts between revisions, so the page labels this "model database".
var knownDrives = []knownDrive{
	kd(`crucial.*bx500|ct\d+bx500`, "Silicon Motion SM2259XT", false),
	kd(`crucial.*mx500|ct\d+mx500`, "Silicon Motion SM2258", true),
	kd(`crucial.*mx[23]00|ct\d+mx[23]00`, "Marvell 88SS1074", true),
	kd(`crucial.*p3 plus|ct\d+p3pssd`, "Phison E21T", false),
	kd(`crucial.*p3\b|ct\d+p3ssd`, "Phison E21T / SM2267XT", false),
	kd(`crucial.*p2\b|ct\d+p2ssd`, "Phison E13T", false),
	kd(`crucial.*p5 plus|ct\d+p5plus`, "Micron DM02A1", true),
	kd(`crucial.*p5\b|ct\d+p5ssd`, "Micron DM01B2", true),
	kd(`crucial.*p1\b|ct\d+p1ssd`, "Silicon Motion SM2263", true),
	kd(`kingston.*sa400|sa400s37`, "Phison S11 / Silicon Motion SM2259XT", false),
	kd(`kingston.*snv2s|kingston.*nv2|snv2s`, "Phison E21T / SM2267XT", false),
	kd(`kingston.*snvs|kingston.*nv1|snvs`, "Phison E13T", false),
	kd(`kingston.*skc2500|kingston.*kc2500`, "Silicon Motion SM2262EN", true),
	kd(`kingston.*skc3000|kingston.*kc3000`, "Phison E18", true),
	kd(`kingston.*skc600|kingston.*kc600`, "Silicon Motion SM2259", true),
	kd(`kingston.*fury renegade|sfyrd`, "Phison E18", true),
	kd(`wd.*sn350|wds\d+g2.*sn350|wd green sn350`, "SanDisk 20-82-10055", false),
	kd(`wd.*sn550|wds\d+g2.*sn550`, "SanDisk 20-82-10033", false),
	kd(`wd.*sn570|wds\d+g2.*sn570`, "SanDisk 20-82-10052", false),
	kd(`wd.*sn580|wds\d+g3.*sn580`, "SanDisk 20-82-10102", false),
	kd(`wd.*sn770|wds\d+g3x0e`, "SanDisk 20-82-10081", false),
	kd(`wd.*sn5000`, "SanDisk (DRAM-less)", false),
	kd(`wd.*sn750|wds\d+g3x0c`, "SanDisk 20-82-007011", true),
	kd(`wd.*sn850x|wds\d+t2x0e`, "SanDisk 20-82-10133", true),
	kd(`wd.*sn850|wds\d+t1x0e`, "SanDisk 20-82-10079", true),
	kd(`wd.*sn720|wd.*sn730`, "SanDisk", true),
	kd(`wd.*blue.*sa510|wds\d+g.*sa510`, "SanDisk", false),
	kd(`samsung.*980 pro|mz-v8p`, "Samsung Elpis", true),
	kd(`samsung.*990 pro|mz-v9p`, "Samsung Pascal", true),
	kd(`samsung.*990 evo|mz-v9e`, "Samsung Piccolo", false),
	kd(`samsung.*980\b|mz-v8v`, "Samsung Pablo", false),
	kd(`samsung.*970 evo plus|mz-v7s`, "Samsung Phoenix", true),
	kd(`samsung.*970 evo|mz-v7e`, "Samsung Phoenix", true),
	kd(`samsung.*970 pro|mz-v7p`, "Samsung Phoenix", true),
	kd(`samsung.*960`, "Samsung Polaris", true),
	kd(`samsung.*87\d ?(evo|qvo)|mz-77`, "Samsung MKX / MJX", true),
	kd(`samsung.*86\d ?(evo|pro|qvo)`, "Samsung MJX", true),
	kd(`samsung.*850`, "Samsung MEX", true),
	kd(`samsung.*pm9a1|mzvl2`, "Samsung Elpis", true),
	kd(`sk ?hynix.*gold p31|hfs\d+gdt`, "SK hynix Dolphin", true),
	kd(`sk ?hynix.*bc711|hfm\d+gdj`, "SK hynix (DRAM-less)", false),
	kd(`adata.*su630|asu630`, "Silicon Motion SM2258XT", false),
	kd(`adata.*su650|asu650`, "Silicon Motion SM2258XT", false),
	kd(`adata.*su800|asu800`, "Silicon Motion SM2258", true),
	kd(`adata.*sx8200 pro|asx8200pnp`, "Silicon Motion SM2262EN", true),
	kd(`adata.*sx6000 lite|asx6000lnp`, "Realtek RTS5762", false),
	kd(`adata.*legend 710|aleg-710`, "Phison E21T / MAXIO", false),
	kd(`adata.*legend 960|aleg-960`, "InnoGrit IG5236", true),
	kd(`teamgroup.*mp33|tm8fp6`, "Phison E13T", false),
	kd(`teamgroup.*mp34|tm8fp4`, "Phison E12", true),
	kd(`teamgroup.*gx2|t253x2`, "Silicon Motion SM2258XT", false),
	kd(`lexar.*nm620`, "MAXIO MAP1202", false),
	kd(`lexar.*nm790`, "MAXIO MAP1602", true),
	kd(`intel.*660p|ssdpeke`, "Silicon Motion SM2263", true),
	kd(`intel.*670p|ssdpekn`, "Silicon Motion SM2265", true),
	kd(`corsair.*mp600`, "Phison E16", true),
	kd(`corsair.*mp400`, "Silicon Motion SM2263", true),
	kd(`corsair.*mp510`, "Phison E12", true),
	kd(`seagate.*firecuda 530`, "Phison E18", true),
	kd(`seagate.*barracuda.*q5`, "Phison E12S", false),
	kd(`seagate.*barracuda.*120|za\d+cm10003`, "Phison S12 / SM2259XT", false),
	kd(`sabrent.*rocket nvme 4.0|sb-rkt4`, "Phison E16", true),
	kd(`sabrent.*rocket q\b|sb-rktq`, "Phison E12", true),
	kd(`pny.*cs900|pny cs900`, "Phison S11 / SM2259XT", false),
	kd(`pny.*cs1030`, "Phison E13T", false),
	kd(`patriot.*p210`, "Silicon Motion SM2259XT", false),
	kd(`patriot.*p300|patriot.*p310`, "Phison E13T / E21T", false),
}

// readNVMeIdentify reads the NVMe Identify Controller data (4096 bytes) on a query-only handle.
func readNVMeIdentify(h syscall.Handle, d *DiskInfo) {
	extra := make([]byte, 40)
	binary.LittleEndian.PutUint32(extra[0:], 3)    // ProtocolTypeNvme
	binary.LittleEndian.PutUint32(extra[4:], 1)    // NVMeDataTypeIdentify
	binary.LittleEndian.PutUint32(extra[8:], 1)    // CNS 1: Identify Controller
	binary.LittleEndian.PutUint32(extra[16:], 40)  // ProtocolDataOffset
	binary.LittleEndian.PutUint32(extra[20:], 4096) // ProtocolDataLength
	in := make([]byte, 8+40+4096)
	binary.LittleEndian.PutUint32(in, 50) // StorageDeviceProtocolSpecificProperty
	copy(in[8:], extra)
	out, err := ioctl(h, ioctlStorageQueryProperty, in, len(in))
	if err != nil || len(out) < 48+300 {
		return
	}
	id := out[48:]
	d.PCIVendor = le16(id, 0)
	d.SubVendor = le16(id, 2)
	d.HMBBytes = int64(le32(id, 272)) * 4096 // HMPRE: preferred Host Memory Buffer size, in 4 KiB units
	d.hasIdentify = true
}

// classifyDisk fills Maker, Controller, ControllerVendor and the DRAM verdict from everything read so far.
func classifyDisk(d *DiskInfo) {

	// Brand: the model string first; for NVMe the subsystem vendor ID is often the brand too.
	for _, m := range makers {
		if m.re.MatchString(strings.TrimSpace(d.Model)) || m.re.MatchString(strings.TrimSpace(d.Vendor)) {
			d.Maker = m.name
			break
		}
	}
	if d.hasIdentify {
		if d.ControllerVendor = pciVendors[d.PCIVendor]; d.ControllerVendor == "" {
			d.ControllerVendor = fmt.Sprintf("Vendor ID 0x%04X", d.PCIVendor)
		}
		if d.Maker == "" {
			d.Maker = pciVendors[d.SubVendor]
		}
		if d.Maker == "" {
			d.Maker = d.ControllerVendor
		}
	}
	if (d.Maker == "" || strings.HasPrefix(d.Maker, "Vendor ID")) && d.Model != "" { // unknown IDs: the first word of the model is usually the brand
		d.Maker = strings.Fields(d.Model)[0]
	}

	var known *knownDrive
	for i := range knownDrives {
		if knownDrives[i].re.MatchString(d.Model) {
			known = &knownDrives[i]
			break
		}
	}
	if known != nil {
		d.Controller = known.controller
		if d.ControllerVendor == "" {
			d.ControllerVendor = vendorOfController(known.controller)
		}
	}

	d.DRAM, d.DRAMNote = "n/a", ""
	if d.Media != "SSD" {
		if d.Media == "HDD" {
			d.DRAMNote = "Spinning disk: it has a small DRAM buffer, but there is no SSD cache to judge."
		}
		return
	}
	hmb := d.HMBBytes > 0
	switch {
	case known != nil && !known.dram && hmb:
		d.DRAM, d.DRAMNote = "dramless", fmt.Sprintf("DRAM-less. Matches %s and asks Windows for a %s Host Memory Buffer instead of onboard DRAM.", known.controller, fmt.Sprintf("%d MB", d.HMBBytes>>20))
	case known != nil && !known.dram:
		d.DRAM, d.DRAMNote = "dramless", "DRAM-less according to the model database ("+known.controller+")."
	case known != nil && known.dram && hmb:
		d.DRAM, d.DRAMNote = "dram", "Has a DRAM cache according to the model database ("+known.controller+"); it also supports a Host Memory Buffer."
	case known != nil:
		d.DRAM, d.DRAMNote = "dram", "Has a DRAM cache according to the model database ("+known.controller+")."
	case hmb:
		d.DRAM, d.DRAMNote = "dramless-likely", fmt.Sprintf("Probably DRAM-less: the drive requests a %d MB Host Memory Buffer from the PC, which DRAM-less NVMe drives do.", d.HMBBytes>>20)
	case d.hasIdentify:
		d.DRAM, d.DRAMNote = "dram-likely", "Probably has a DRAM cache: the drive does not use a Host Memory Buffer. Not certain; some low-end drives have neither."
	default:
		d.DRAM, d.DRAMNote = "unknown", "Windows does not report DRAM for SATA SSDs and this model is not in the database. Check the controller on the manufacturer's spec sheet."
	}
}

// vendorOfController names the company from a controller string such as "Silicon Motion SM2259XT".
func vendorOfController(c string) string {
	for _, v := range []string{"Silicon Motion", "Phison", "Marvell", "SanDisk", "Samsung", "Micron", "SK hynix", "MAXIO", "InnoGrit", "Realtek"} {
		if strings.HasPrefix(c, v) {
			return v
		}
	}
	return ""
}
