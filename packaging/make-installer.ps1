# Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# Builds the classic Windows installer, dist\ToolBoxSetup-<version>-<arch>.msi, from a published ToolBox folder
# (build.ps1 publishes it into dist\publish\<arch>). Called by build.ps1 -Installer.
# The wizard shows the licence and asks: only for me (user folder, no admin rights) or everyone (Program Files, admin). It adds
# Desktop and Start menu shortcuts and offers to launch the app.
# Silent install: msiexec /i ToolBoxSetup-<version>-x64.msi /qn /norestart   (add ALLUSERS=1 from an elevated prompt for everyone)
#
# Needs the WiX Toolset v5 command line tool. It is installed here on first use (a .NET global tool):
#   dotnet tool install --global wix --version 5.0.2
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Arch = "x64",
    [string]$PublishRoot = "",
    [string]$OutDir = "",
    [string]$SignPfx = "",
    [string]$SignPfxPassword = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishRoot) { $PublishRoot = Join-Path $root "dist\publish" }
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }
$publish = Join-Path $PublishRoot $Arch
if (-not (Test-Path (Join-Path $publish "ToolBox.exe"))) { throw "$publish\ToolBox.exe not found. Run build.ps1 first." }
$buildDir = Join-Path $root "dist\installer-work"
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force $buildDir, $OutDir | Out-Null

# --- WiX ---
$env:Path += ";$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Host "Installing the WiX Toolset v5 (one time)"
    dotnet tool install --global wix --version 5.0.2 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not install WiX. Install it yourself: dotnet tool install --global wix --version 5.0.2" }
}
$ext = (wix extension list -g 2>&1) -join "`n"
foreach ($e in "WixToolset.UI.wixext", "WixToolset.Util.wixext") {
    if ($ext -notmatch [regex]::Escape($e)) { wix extension add -g "$e/5.0.2" | Out-Null; if ($LASTEXITCODE -ne 0) { throw "Could not add the WiX extension $e" } }
}

# --- licence page (RTF) ---
Write-Host "[$Arch] wizard pages"
$text = [IO.File]::ReadAllText((Join-Path $root "LICENSE"))
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $text.ToCharArray()) {
    switch ($ch) {
        '\' { [void]$sb.Append('\\') }
        '{' { [void]$sb.Append('\{') }
        '}' { [void]$sb.Append('\}') }
        "`r" { }
        "`n" { [void]$sb.Append("\par`n") }
        default { if ([int]$ch -gt 127) { [void]$sb.Append("\u$([int]$ch)?") } else { [void]$sb.Append($ch) } }
    }
}
[IO.File]::WriteAllText((Join-Path $buildDir "License.rtf"), ("{\rtf1\ansi\deff0{\fonttbl{\f0\fswiss Segoe UI;}}\viewkind4\uc1\pard\f0\fs17 " + $sb.ToString() + "}"), [Text.Encoding]::ASCII)

# --- wizard images: top banner and welcome / finish panel ---
Add-Type -AssemblyName System.Drawing
$iconPng = Join-Path $root "src\ToolBox\Assets\icon-256.png"
$icon = [System.Drawing.Image]::FromFile($iconPng)
$accentA = [System.Drawing.Color]::FromArgb(255, 0, 95, 184); $accentB = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)
$bmp = New-Object System.Drawing.Bitmap 493, 58
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"
$g.Clear([System.Drawing.Color]::White); $g.DrawImage($icon, 440, 7, 44, 44)
$g.FillRectangle((New-Object System.Drawing.SolidBrush $accentB), 0, 56, 493, 2)
$bmp.Save((Join-Path $buildDir "banner.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp); $g.Dispose(); $bmp.Dispose()
$bmp = New-Object System.Drawing.Bitmap 493, 312
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"
$g.Clear([System.Drawing.Color]::White)
$rect = New-Object System.Drawing.Rectangle 0, 0, 164, 312
$g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $accentB, $accentA, 90), $rect)
$g.DrawImage($icon, 26, 70, 112, 112)
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"
$g.DrawString("My Tool Box", (New-Object System.Drawing.Font "Segoe UI Semibold", 15), [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 196, 164, 32), $fmt)
$g.DrawString("v$Version", (New-Object System.Drawing.Font "Segoe UI", 8.5), (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))), (New-Object System.Drawing.RectangleF 0, 226, 164, 20), $fmt)
$bmp.Save((Join-Path $buildDir "dialog.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp); $g.Dispose(); $bmp.Dispose(); $icon.Dispose()

# --- build ---
Write-Host "[$Arch] MSI"
$out = Join-Path $OutDir "ToolBoxSetup-$Version-$Arch.msi"
if (Test-Path $out) { Remove-Item $out }
Push-Location (Join-Path $PSScriptRoot "installer")
try {
    $appIcon = Join-Path $root "src\ToolBox\Assets\app.ico"; $licenseFile = Join-Path $root "LICENSE"
    wix build -arch $Arch -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d "Version=$Version" -d "PublishDir=$publish" -d "BuildDir=$buildDir" -d "IconSource=$appIcon" -d "LicenseFile=$licenseFile" `
        -o $out ToolBox.wxs
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
} finally { Pop-Location }

if ($SignPfx) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found" }
    & $signtool.FullName sign /fd SHA256 /f $SignPfx /p $SignPfxPassword /tr http://timestamp.digicert.com /td SHA256 $out
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}
Remove-Item (Join-Path $OutDir "*.wixpdb") -ErrorAction SilentlyContinue
Remove-Item $buildDir -Recurse -Force
Write-Host ("Installer: {0}  {1} MB" -f $out, [math]::Round((Get-Item $out).Length / 1MB, 1))
