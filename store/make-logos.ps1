# Renders the Microsoft Store logo (300x300) and poster (1080x1080) from src\web\icon.svg
# using headless Microsoft Edge. Output: store\logos\
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$svgUrl = "file:///" + ((Join-Path $root "src\web\icon.svg") -replace "\\", "/")
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

# 2:3 portrait poster art: icon, name and tagline on a Fluent blue gradient.
# $scale 1 -> 720x1080, 2 -> 1440x2160 (rendered with device scale factor).
function New-Poster([int]$scale, [string]$name) {
    $html = Join-Path $env:TEMP "poster.html"
    Set-Content $html @"
<html><body style="margin:0;width:720px;height:1080px;overflow:hidden;font-family:'Segoe UI Variable Display','Segoe UI',sans-serif;color:#fff;
background:radial-gradient(900px 700px at 15% 8%,#4cc2ff 0%,transparent 60%),radial-gradient(800px 800px at 95% 100%,#7b3fe4 0%,transparent 55%),linear-gradient(160deg,#0a6fd1,#06306b)">
<div style="height:1080px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:34px;text-align:center">
<img src="$svgUrl" width="440" height="440" style="filter:drop-shadow(0 30px 40px rgba(0,0,0,.35))">
<div style="font-size:92px;font-weight:600;letter-spacing:-1px">My Tool Box</div>
<div style="font-size:34px;opacity:.9;line-height:1.35;max-width:560px">Everyday IT tools for Windows,<br>in one fast, private app</div>
</div></body></html>
"@
    $out = Join-Path $outDir $name
    if (Test-Path $out) { Remove-Item $out }
    $htmlUrl = "file:///" + ($html -replace "\\", "/")
    Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @(
        "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
        "--user-data-dir=$env:TEMP\tb-edge-poster", "--force-device-scale-factor=$scale",
        "--window-size=720,1080", "--screenshot=$out", $htmlUrl)
    Write-Host "$name : $(Test-Path $out)"
}

New-Logo 300 "logo-300x300.png" 20
New-Logo 1080 "poster-1080x1080.png" 140
New-Poster 1 "poster-720x1080.png"
New-Poster 2 "poster-1440x2160.png"
