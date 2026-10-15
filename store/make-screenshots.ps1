# Captures 1600x900 Store screenshots of a running app with headless Edge.
# 1) Start the app:  ToolBox.exe -nobrowser -port 18780
# 2) Scan a folder:  POST http://127.0.0.1:18780/api/scan?path=C:\Program%20Files
# 3) Run this script. Output: store\screenshots\
# Do not use folders or networks that show private data (serial numbers, device names).
param([int]$Port = 18780)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$dir = Join-Path $root "store\screenshots"
New-Item -ItemType Directory -Force $dir | Out-Null

function New-Shot([string]$name, [string]$tool, [bool]$dark) {
    $out = Join-Path $dir "$name.png"
    if (Test-Path $out) { Remove-Item $out }
    $theme = if ($dark) { "--force-dark-mode" } else { "--blink-settings=preferredColorScheme=1" }
    Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @(
        "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
        "--user-data-dir=$env:TEMP\tb-edge-shot-$name", "--window-size=1600,900",
        "--virtual-time-budget=8000", $theme, "--screenshot=$out", "http://127.0.0.1:$Port/#$tool")
    Write-Host "$name : $(Test-Path $out)"
}

New-Shot "01-folder-size-light" "foldersize" $false
New-Shot "02-folder-size-dark" "foldersize" $true
New-Shot "03-subnet-calculator" "subnet" $false
New-Shot "04-token-uuid-generator" "token" $true
