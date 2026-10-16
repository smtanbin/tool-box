// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

//go:build !amd64

package main

// CPUID exists only on x86; other builds fall back to what WMI reports.
func cpuid(eaxArg, ecxArg uint32) (eax, ebx, ecx, edx uint32) { return }

const cpuidSupported = false
