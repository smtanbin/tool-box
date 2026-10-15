# Renders the Microsoft Store logo (300x300) and poster (1080x1080) from web\icon.svg
# using headless Microsoft Edge. Output: store\logos\
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$svgUrl = "file:///" + ((Join-Path $root "web\icon.svg") -replace "\\", "/")
$outDir = Join-Path $root "store\logos"
New-Item -ItemType Directory -Force $outDir | Out-Null

function New-Logo([int]$size, [string]$name, [int]$pad) {
    $inner = $size - 2 * $pad
    $html = Join-Path $env:TEMP "logo-$size.html"
    Set-Content $html "<html><body style='margin:0;background:transparent'><div style='width:${size}px;height:${size}px;display:flex;align-items:center;justify-content:center'><img src='$svgUrl' width='$inner' height='$inner'></div></body></html>"
    $out = Join-Path $outDir $name
    if (Test-Path $out) { Remove-Item $out }
    $htmlUrl = "file:///" + ($html -replace "\\", "/")
    Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @(
        "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
        "--user-data-dir=$env:TEMP\tb-edge-logo", "--default-background-color=00000000",
        "--window-size=$size,$size", "--screenshot=$out", $htmlUrl)
    Write-Host "$name : $(Test-Path $out)"
}

New-Logo 300 "logo-300x300.png" 20
New-Logo 1080 "poster-1080x1080.png" 140
