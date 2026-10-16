// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"fmt"
	"regexp"
	"runtime"
	"strings"
)

// Processor details that WMI does not give: family / model fields, instruction sets and cache levels come from CPUID,
// the code name and manufacturing process from a small table of well-known models.

type CacheLevel struct {
	Name  string `json:"name"`  // L1 Data, L1 Inst., Level 2, Level 3
	Size  int    `json:"size"`  // bytes per instance
	Count int    `json:"count"` // instances
	Ways  int    `json:"ways"`
	Line  int    `json:"line"`
}

type CPUDetail struct {
	Vendor      string       `json:"vendor"`
	Brand       string       `json:"brand"`
	Family      string       `json:"family"`    // hex, as CPU-Z shows it
	Model       string       `json:"model"`     // hex
	ExtFamily   string       `json:"extFamily"` // hex
	ExtModel    string       `json:"extModel"`  // hex
	Stepping    int          `json:"stepping"`
	FamilyDec   int          `json:"familyDec"`
	ModelDec    int          `json:"modelDec"`
	Codename    string       `json:"codename"`
	Tech        string       `json:"tech"`
	Instruction string       `json:"instructions"`
	Caches      []CacheLevel `json:"caches"`
	Logical     int          `json:"logical"`
	Supported   bool         `json:"supported"`
}

func regStr(a, b, c uint32) string {
	var out []byte
	for _, r := range []uint32{a, b, c} {
		out = append(out, byte(r), byte(r>>8), byte(r>>16), byte(r>>24))
	}
	return strings.TrimRight(strings.TrimSpace(string(out)), "\x00 ")
}

func bit(v uint32, n uint) bool { return v>>n&1 == 1 }

// instructionList turns the CPUID feature bits into the grouped text CPU-Z prints.
func instructionList(vendor string, l1c, l1d, l7b, e1c, e1d uint32, avxOK bool) string {
	var parts []string
	add := func(ok bool, s string) {
		if ok {
			parts = append(parts, s)
		}
	}
	add(bit(l1d, 23), "MMX")
	var sse []string
	for _, f := range []struct {
		ok bool
		s  string
	}{{bit(l1d, 25), "1"}, {bit(l1d, 26), "2"}, {bit(l1c, 0), "3"}, {bit(l1c, 19), "4.1"}, {bit(l1c, 20), "4.2"}, {bit(e1c, 6), "4A"}} {
		if f.ok {
			sse = append(sse, f.s)
		}
	}
	if len(sse) > 0 {
		parts = append(parts, "SSE ("+strings.Join(sse, ", ")+")")
	}
	add(bit(l1c, 9), "SSSE3")
	add(bit(e1d, 29), "x86-64")
	add(strings.Contains(vendor, "AMD") && bit(e1c, 2), "AMD-V")
	add(strings.Contains(vendor, "Intel") && bit(l1c, 5), "VT-x")
	add(bit(l1c, 25), "AES")
	add(avxOK && bit(l1c, 28), "AVX")
	add(avxOK && bit(l7b, 5), "AVX2")
	add(avxOK && bit(l7b, 16), "AVX-512")
	add(bit(l1c, 12), "FMA3")
	add(bit(l7b, 3), "BMI1")
	add(bit(l7b, 8), "BMI2")
	add(bit(l1c, 23), "POPCNT")
	add(bit(l7b, 29), "SHA")
	add(bit(l1c, 30), "RDRAND")
	add(bit(l7b, 18), "RDSEED")
	return strings.Join(parts, ", ")
}

// decodeCache decodes one cache descriptor (CPUID leaf 4 on Intel, 0x8000001D on AMD).
func decodeCache(a, b, c uint32, logical int) (CacheLevel, bool) {
	typ, level := a&0x1f, int(a>>5&7)
	if typ == 0 {
		return CacheLevel{}, false
	}
	sharing := int(a>>14&0xfff) + 1
	line, parts, ways, sets := int(b&0xfff)+1, int(b>>12&0x3ff)+1, int(b>>22)+1, int(c)+1
	cl := CacheLevel{Size: ways * parts * line * sets, Ways: ways, Line: line}
	cl.Count = max(1, (logical+sharing-1)/sharing)
	switch {
	case level == 1 && typ == 1:
		cl.Name = "L1 Data"
	case level == 1 && typ == 2:
		cl.Name = "L1 Inst."
	case level == 2:
		cl.Name = "Level 2"
	case level == 3:
		cl.Name = "Level 3"
	default:
		cl.Name = fmt.Sprintf("Level %d", level)
	}
	return cl, true
}

var steppingRe = regexp.MustCompile(`Stepping (\d+)`)

type codeInfo struct{ name, tech string }

// key: vendor:total family:total model (decimal). Only well-documented models.
var codenames = map[string]codeInfo{
	"AMD:23:1": {"Summit Ridge", "14 nm"}, "AMD:23:8": {"Pinnacle Ridge", "12 nm"}, "AMD:23:17": {"Raven Ridge", "14 nm"}, "AMD:23:24": {"Picasso", "12 nm"},
	"AMD:23:49": {"Rome", "7 nm"}, "AMD:23:96": {"Renoir", "7 nm"}, "AMD:23:113": {"Matisse", "7 nm"}, "AMD:23:104": {"Lucienne", "7 nm"},
	"AMD:25:1": {"Milan", "7 nm"}, "AMD:25:33": {"Vermeer", "7 nm"}, "AMD:25:80": {"Cezanne", "7 nm"}, "AMD:25:68": {"Rembrandt", "6 nm"},
	"AMD:25:97": {"Raphael", "5 nm"}, "AMD:25:116": {"Phoenix", "4 nm"}, "AMD:25:17": {"Genoa", "5 nm"}, "AMD:26:68": {"Granite Ridge", "4 nm"},
	"Intel:6:42": {"Sandy Bridge", "32 nm"}, "Intel:6:58": {"Ivy Bridge", "22 nm"}, "Intel:6:60": {"Haswell", "22 nm"}, "Intel:6:69": {"Haswell ULT", "22 nm"},
	"Intel:6:61": {"Broadwell", "14 nm"}, "Intel:6:78": {"Skylake-U", "14 nm"}, "Intel:6:94": {"Skylake", "14 nm"}, "Intel:6:142": {"Kaby Lake", "14 nm"},
	"Intel:6:158": {"Coffee Lake", "14 nm"}, "Intel:6:165": {"Comet Lake", "14 nm"}, "Intel:6:167": {"Rocket Lake", "14 nm"}, "Intel:6:126": {"Ice Lake", "10 nm"},
	"Intel:6:140": {"Tiger Lake", "10 nm"}, "Intel:6:141": {"Tiger Lake-H", "10 nm"}, "Intel:6:151": {"Alder Lake", "Intel 7"}, "Intel:6:154": {"Alder Lake-P", "Intel 7"},
	"Intel:6:183": {"Raptor Lake", "Intel 7"}, "Intel:6:191": {"Raptor Lake", "Intel 7"}, "Intel:6:170": {"Meteor Lake", "Intel 4"}, "Intel:6:198": {"Arrow Lake", "3 nm"},
}

func lookupCodename(vendor string, fam, model int) (string, string) {
	v := "AMD"
	if strings.Contains(vendor, "Intel") {
		v = "Intel"
	}
	c := codenames[fmt.Sprintf("%s:%d:%d", v, fam, model)]
	return c.name, c.tech
}

// readCPUDetail reads everything CPUID offers; identifier is the registry text "... Family 25 Model 33 Stepping 2" when known.
func readCPUDetail(identifier string) CPUDetail {
	d := CPUDetail{Logical: runtime.NumCPU(), Supported: cpuidSupported}
	if !cpuidSupported {
		return d
	}
	maxLeaf, vb, vc, vd := cpuid(0, 0)
	d.Vendor = regStr(vb, vd, vc)
	a1, _, c1, d1 := cpuid(1, 0)
	stepping := int(a1 & 0xf)
	if m := steppingRe.FindStringSubmatch(identifier); m != nil {
		fmt.Sscan(m[1], &stepping) // Windows' own stepping number (matches CPU-Z)
	}
	fam, mod, extMod, extFam := int(a1>>8&0xf), int(a1>>4&0xf), int(a1>>16&0xf), int(a1>>20&0xff)
	d.Stepping = stepping
	d.Family, d.Model = fmt.Sprintf("%X", fam), fmt.Sprintf("%X", mod)
	d.FamilyDec, d.ModelDec = fam, mod
	if fam == 0xf {
		d.FamilyDec = fam + extFam
	}
	if fam == 0xf || fam == 0x6 {
		d.ModelDec = extMod<<4 | mod
	}
	d.ExtFamily, d.ExtModel = fmt.Sprintf("%X", d.FamilyDec), fmt.Sprintf("%X", d.ModelDec)
	var l7b uint32
	if maxLeaf >= 7 {
		_, l7b, _, _ = cpuid(7, 0)
	}
	maxExt, _, _, _ := cpuid(0x80000000, 0)
	var e1c, e1d uint32
	if maxExt >= 0x80000001 {
		_, _, e1c, e1d = cpuid(0x80000001, 0)
	}
	d.Instruction = instructionList(d.Vendor, c1, d1, l7b, e1c, e1d, bit(c1, 27)) // OSXSAVE: the OS saves AVX state
	if maxExt >= 0x80000004 {
		var b []byte
		for l := uint32(0x80000002); l <= 0x80000004; l++ {
			a, bb, c, dd := cpuid(l, 0)
			for _, r := range []uint32{a, bb, c, dd} {
				b = append(b, byte(r), byte(r>>8), byte(r>>16), byte(r>>24))
			}
		}
		d.Brand = strings.Join(strings.Fields(strings.TrimRight(string(b), "\x00")), " ")
	}
	d.Codename, d.Tech = lookupCodename(d.Vendor, d.FamilyDec, d.ModelDec)
	leaf := uint32(4)
	if strings.Contains(d.Vendor, "AMD") && maxExt >= 0x8000001D {
		leaf = 0x8000001D
	}
	if leaf == 0x8000001D || maxLeaf >= 4 {
		for i := uint32(0); i < 16; i++ {
			a, b, c, _ := cpuid(leaf, i)
			cl, ok := decodeCache(a, b, c, d.Logical)
			if !ok {
				break
			}
			d.Caches = append(d.Caches, cl)
		}
	}
	return d
}

var socketPins = map[string]int{"AM4": 1331, "AM5": 1718, "AM3+": 942, "AM3": 941, "TR4": 4094, "sTRX4": 4094, "SP3": 4094, "SP5": 6096, "FM2+": 906, "FM2": 904, "FM1": 905}
var socketNum = regexp.MustCompile(`(\d{3,4})`)

// socketText is "Socket AM4 (1331)" style text from the WMI socket name.
func socketText(s string) string {
	s = strings.TrimSpace(s)
	if s == "" {
		return ""
	}
	if n, ok := socketPins[s]; ok {
		return fmt.Sprintf("Socket %s (%d)", s, n)
	}
	up := strings.ToUpper(s)
	if m := socketNum.FindString(s); m != "" && (strings.HasPrefix(up, "LGA") || strings.HasPrefix(up, "SOCKET")) {
		return fmt.Sprintf("%s (%s)", s, m)
	}
	return s
}

// chipsetFromBoard guesses the chipset name from the board model, e.g. "B550M AORUS ELITE AX" -> "B550".
var chipsetRe = regexp.MustCompile(`\b([ABXZHQWC]\d{2,3}[A-Z]?|TRX40|WRX80|X670E?)\b`)

func chipsetFromBoard(model string) string {
	m := chipsetRe.FindStringSubmatch(strings.ToUpper(model))
	if m == nil {
		return ""
	}
	return strings.TrimRight(m[1], "MEIDFH")
}

// jedecMakers decodes the manufacturer codes some BIOSes return instead of a name.
var jedecMakers = map[string]string{"029E": "Corsair", "859B": "Crucial", "802C": "Micron", "80AD": "SK hynix", "CE80": "Samsung", "04CD": "G.Skill",
	"0198": "Kingston", "9801": "Kingston", "2C00": "Micron", "AD80": "SK hynix", "80CE": "Samsung", "CD04": "G.Skill", "9E02": "Corsair", "9B85": "Crucial",
	"7F7F7F9D": "Patriot", "014F": "Transcend", "0B00": "Nanya", "8551": "Apacer"}

func memMaker(s string) string {
	t := strings.ToUpper(strings.TrimSpace(s))
	if n, ok := jedecMakers[t]; ok {
		return n
	}
	return strings.TrimSpace(s)
}

// microcodeText turns the registry's "Update Revision" bytes (little-endian; 4 bytes on AMD, 8 on Intel) into 0xHEX.
func microcodeText(hexBytes string) string {
	if len(hexBytes) != 8 && len(hexBytes) != 16 {
		return ""
	}
	h := hexBytes[len(hexBytes)-8:] // Intel keeps the revision in the upper dword
	if len(hexBytes) == 8 {
		h = hexBytes
	}
	var b [4]byte
	for i := 0; i < 4; i++ {
		fmt.Sscanf(h[i*2:i*2+2], "%02x", &b[i])
	}
	return fmt.Sprintf("0x%X", uint32(b[0])|uint32(b[1])<<8|uint32(b[2])<<16|uint32(b[3])<<24)
}
