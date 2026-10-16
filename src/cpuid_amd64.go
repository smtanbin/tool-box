// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

//go:build amd64

package main

// cpuid runs the CPUID instruction (implemented in cpuid_amd64.s).
func cpuid(eaxArg, ecxArg uint32) (eax, ebx, ecx, edx uint32)

const cpuidSupported = true
