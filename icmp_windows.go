// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

package main

import (
	"encoding/binary"
	"fmt"
	"net"
	"syscall"
	"time"
	"unsafe"
)

// ICMP echo through iphlpapi.dll: works without Administrator rights and without cgo.
var (
	iphlpapi            = syscall.NewLazyDLL("iphlpapi.dll")
	procIcmpCreateFile  = iphlpapi.NewProc("IcmpCreateFile")
	procIcmpCloseHandle = iphlpapi.NewProc("IcmpCloseHandle")
	procIcmpSendEcho    = iphlpapi.NewProc("IcmpSendEcho")
	procSendARP         = iphlpapi.NewProc("SendARP")
)

type ipOptionInfo struct {
	Ttl         uint8
	Tos         uint8
	Flags       uint8
	OptionsSize uint8
	OptionsData uintptr
}

// icmpEchoReply mirrors ICMP_ECHO_REPLY (64-bit layout).
type icmpEchoReply struct {
	Address       uint32
	Status        uint32
	RoundTripTime uint32
	DataSize      uint16
	Reserved      uint16
	Data          uintptr
	Options       ipOptionInfo
}

type PingResult struct {
	IP     string  `json:"ip"`
	OK     bool    `json:"ok"`
	Ms     float64 `json:"ms"`
	TTL    int     `json:"ttl"`
	Status string  `json:"status"`
}

var icmpStatus = map[uint32]string{
	0: "Reply", 11002: "Destination net unreachable", 11003: "Destination host unreachable",
	11004: "Destination protocol unreachable", 11005: "Destination port unreachable", 11009: "Packet too big",
	11010: "Request timed out", 11013: "TTL expired in transit", 11050: "General failure",
}

func ipv4Key(ip net.IP) uint32 { return binary.LittleEndian.Uint32(ip.To4()) }

// icmpPing sends one echo request to an IPv4 address.
func icmpPing(ip net.IP, timeout time.Duration, size int, ttl int) PingResult {
	res := PingResult{IP: ip.String()}
	if ip.To4() == nil {
		res.Status = "Only IPv4 is supported"
		return res
	}
	h, _, _ := procIcmpCreateFile.Call()
	if h == uintptr(syscall.InvalidHandle) || h == 0 {
		res.Status = "IcmpCreateFile failed"
		return res
	}
	defer procIcmpCloseHandle.Call(h)
	if size < 0 {
		size = 0
	}
	if size > 65500 {
		size = 65500
	}
	data := make([]byte, size+1)
	for i := range data {
		data[i] = byte('a' + i%23)
	}
	reply := make([]byte, int(unsafe.Sizeof(icmpEchoReply{}))+size+64)
	var opts *ipOptionInfo
	if ttl > 0 && ttl < 256 {
		opts = &ipOptionInfo{Ttl: uint8(ttl)}
	}
	n, _, errno := procIcmpSendEcho.Call(h, uintptr(ipv4Key(ip)), uintptr(unsafe.Pointer(&data[0])), uintptr(size),
		uintptr(unsafe.Pointer(opts)), uintptr(unsafe.Pointer(&reply[0])), uintptr(len(reply)), uintptr(timeout.Milliseconds()))
	if n == 0 {
		code := uint32(0)
		if e, ok := errno.(syscall.Errno); ok {
			code = uint32(e)
		}
		if s, ok := icmpStatus[code]; ok {
			res.Status = s
		} else {
			res.Status = fmt.Sprintf("Error %d", code)
		}
		return res
	}
	r := (*icmpEchoReply)(unsafe.Pointer(&reply[0]))
	if s, ok := icmpStatus[r.Status]; ok {
		res.Status = s
	} else {
		res.Status = fmt.Sprintf("Status %d", r.Status)
	}
	res.OK = r.Status == 0
	res.TTL = int(r.Options.Ttl)
	res.Ms = float64(r.RoundTripTime) // whole ms; 0 means under 1 ms
	return res
}

// arpMAC returns the MAC address of an IPv4 host on the local network, or "".
func arpMAC(ip net.IP) string {
	var mac [8]byte
	l := uint32(len(mac))
	r, _, _ := procSendARP.Call(uintptr(ipv4Key(ip)), 0, uintptr(unsafe.Pointer(&mac[0])), uintptr(unsafe.Pointer(&l)))
	if r != 0 || l < 6 {
		return ""
	}
	return net.HardwareAddr(mac[:6]).String()
}
