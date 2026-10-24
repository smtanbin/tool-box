# Builds, for each architecture:
#   dist\ToolBoxSetup-<version>-<arch>.msi   (installer)
#   dist\ToolBox-<version>-<arch>.exe        (portable)
# Requires: Go, WiX Toolset v5 (`dotnet tool install --global wix --version 5.0.2`) with extensions
#   wix extension add -g WixToolset.UI.wixext/5.0.2
#   wix extension add -g WixToolset.Util.wixext/5.0.2
# Usage: powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 2.2.0] [-Arch x64,x86,arm64]
param(
    [string]$Version = "2.2.0",
    [string[]]$Arch = @("x64", "x86", "arm64"),
    # Optional code signing (needed for Microsoft Store policy 10.2.9). When a PFX is given,
    # ToolBox.exe and every MSI are signed with signtool (SHA256 + RFC3161 timestamp).
    [string]$SignPfx = "",         # path to a code-signing .pfx from a Microsoft Trusted Root CA
    [string]$SignPfxPassword = ""
)
$ErrorActionPreference = "Stop"
$signing = ($SignPfx -ne "")
function Sign-File([string]$Path) {
    if (-not $signing) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK)" }
    & $signtool.FullName sign /v /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $SignPfx /p $SignPfxPassword $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $Path" }
}
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$build = Join-Path $here "build"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $build, $dist | Out-Null
$goArch = @{ x64 = "amd64"; x86 = "386"; arm64 = "arm64" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }

Write-Host "Licence page"
$text = [IO.File]::ReadAllText((Join-Path $root "LICENSE"))
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $text.ToCharArray()) {
    switch ($ch) {
        '\' { [void]$sb.Append('\\') }
        '{' { [void]$sb.Append('\{') }
        '}' { [void]$sb.Append('\}') }
        "`r" { }
        "`n" { [void]$sb.Append("\par`n") }
        default {
            if ([int]$ch -gt 127) { [void]$sb.Append("\u$([int]$ch)?") } else { [void]$sb.Append($ch) }
        }
    }
}
$rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0\fswiss Segoe UI;}}\viewkind4\uc1\pard\f0\fs17 " + $sb.ToString() + "}"
[IO.File]::WriteAllText((Join-Path $build "License.rtf"), $rtf, [Text.Encoding]::ASCII)

Write-Host "Wizard images"
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "src\web\icon-256.png"))
$accentA = [System.Drawing.Color]::FromArgb(255, 0, 95, 184)
$accentB = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)

# Top banner 493x58: white, icon on the right (dialog titles are drawn on the left).
$bmp = New-Object System.Drawing.Bitmap 493, 58
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"
$g.Clear([System.Drawing.Color]::White)
$g.DrawImage($icon, 440, 7, 44, 44)
$g.FillRectangle((New-Object System.Drawing.SolidBrush $accentB), 0, 56, 493, 2)
$bmp.Save((Join-Path $build "banner.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose()

# Welcome / finish image 493x312: accent panel on the left 164px, white elsewhere.
$bmp = New-Object System.Drawing.Bitmap 493, 312
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"
$g.Clear([System.Drawing.Color]::White)
$rect = New-Object System.Drawing.Rectangle 0, 0, 164, 312
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $accentB, $accentA, 90
$g.FillRectangle($grad, $rect)
$g.DrawImage($icon, 26, 70, 112, 112)
$font = New-Object System.Drawing.Font "Segoe UI Semibold", 15
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"
$g.DrawString("My Tool Box", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 196, 164, 32), $fmt)
$small = New-Object System.Drawing.Font "Segoe UI", 8.5
$g.DrawString("v$Version", $small, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))), (New-Object System.Drawing.RectangleF 0, 226, 164, 20), $fmt)
$bmp.Save((Join-Path $build "dialog.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $icon.Dispose()

# Version info + icon resources (shown in Explorer's Properties > Details)
& (Join-Path $here "make-syso.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "make-syso failed" }

foreach ($a in $Arch) {
    if (-not $goArch.ContainsKey($a)) { throw "Unknown arch '$a' (use x64, x86, arm64)" }
    Write-Host "[$a] ToolBox.exe"
    $exeDir = Join-Path $build $a
    New-Item -ItemType Directory -Force $exeDir | Out-Null
    $exe = Join-Path $exeDir "ToolBox.exe"
    Push-Location (Join-Path $root "src")
    $env:GOOS = "windows"; $env:GOARCH = $goArch[$a]; $env:CGO_ENABLED = "0"
    try {
        go build -trimpath -ldflags "-H windowsgui -s -w -X main.appVersion=$Version" -o $exe .
        if ($LASTEXITCODE -ne 0) { throw "go build failed for $a" }
    } finally {
        Remove-Item Env:GOARCH, Env:GOOS, Env:CGO_ENABLED -ErrorAction SilentlyContinue
        Pop-Location
    }
    Sign-File $exe
    Copy-Item $exe (Join-Path $dist "ToolBox-$Version-$a.exe") -Force

    Write-Host "[$a] MSI"
    $out = Join-Path $dist "ToolBoxSetup-$Version-$a.msi"
    Push-Location $here
    try {
        wix build -arch $a -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d Version=$Version -d ExeSource=$exe -o $out ToolBox.wxs
        if ($LASTEXITCODE -ne 0) { throw "wix build failed for $a" }
    } finally { Pop-Location }
    Sign-File $out
}
Remove-Item (Join-Path $dist "*.wixpdb") -ErrorAction SilentlyContinue
Write-Host "Done:"; Get-ChildItem $dist -Filter "*$Version*" | ForEach-Object { Write-Host "  $($_.Name)  $([math]::Round($_.Length/1MB,2)) MB" }
