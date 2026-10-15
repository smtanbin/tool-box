# Builds dist\ToolBoxSetup-<version>.msi
# Requires: Go, WiX Toolset v5 (`dotnet tool install --global wix`) with extensions
#   wix extension add -g WixToolset.UI.wixext/5.0.2
#   wix extension add -g WixToolset.Util.wixext/5.0.2
# Usage: powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$build = Join-Path $here "build"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $build, $dist | Out-Null

Write-Host "1/4 Building ToolBox.exe"
Push-Location $root
try {
    go build -ldflags "-H windowsgui -s -w" -o ToolBox.exe .
    if ($LASTEXITCODE -ne 0) { throw "go build failed" }
} finally { Pop-Location }

Write-Host "2/4 Licence page"
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

Write-Host "3/4 Wizard images"
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "web\icon-256.png"))
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
$g.DrawString("Tool Box", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 196, 164, 32), $fmt)
$small = New-Object System.Drawing.Font "Segoe UI", 8.5
$g.DrawString("v$Version", $small, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))), (New-Object System.Drawing.RectangleF 0, 226, 164, 20), $fmt)
$bmp.Save((Join-Path $build "dialog.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $icon.Dispose()

Write-Host "4/4 MSI"
$out = Join-Path $dist "ToolBoxSetup-$Version.msi"
Push-Location $here
try {
    wix build -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d Version=$Version -o $out ToolBox.wxs
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
} finally { Pop-Location }
Remove-Item (Join-Path $dist "*.wixpdb") -ErrorAction SilentlyContinue
Write-Host "Done: $out"
