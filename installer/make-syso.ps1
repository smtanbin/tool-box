# Copyright 2026 Tanbin (magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# Regenerates the Windows resource files (rsrc_windows_*.syso): app icon plus the version info
# shown in Explorer's Properties > Details tab. The version comes from -Version (x.y.z).
#
# Usage: powershell -ExecutionPolicy Bypass -File installer\make-syso.ps1 -Version 2.0.0
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Version must look like 1.2.3 (got '$Version')" }
$maj, $min, $pat = [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$json = Get-Content (Join-Path $root "src\versioninfo.json") -Raw | ConvertFrom-Json
foreach ($k in "FileVersion", "ProductVersion") {
    $json.FixedFileInfo.$k.Major = $maj; $json.FixedFileInfo.$k.Minor = $min; $json.FixedFileInfo.$k.Patch = $pat; $json.FixedFileInfo.$k.Build = 0
}
$json.StringFileInfo.FileVersion = "$Version.0"
$json.StringFileInfo.ProductVersion = "$Version.0"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "toolbox-versioninfo.json"
[IO.File]::WriteAllText($tmp, ($json | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))

Push-Location (Join-Path $root "src")
try {
    $tool = "github.com/josephspurrier/goversioninfo/cmd/goversioninfo@v1.4.1"
    go run $tool -icon app.ico -o rsrc_windows_amd64.syso -64 $tmp
    if ($LASTEXITCODE -ne 0) { throw "goversioninfo failed (amd64)" }
    go run $tool -icon app.ico -o rsrc_windows_386.syso $tmp
    if ($LASTEXITCODE -ne 0) { throw "goversioninfo failed (386)" }
    go run $tool -icon app.ico -o rsrc_windows_arm64.syso -arm -64 $tmp
    if ($LASTEXITCODE -ne 0) { throw "goversioninfo failed (arm64)" }
} finally { Pop-Location; Remove-Item $tmp -ErrorAction SilentlyContinue }
Write-Host "Wrote rsrc_windows_{amd64,386,arm64}.syso for version $Version"
