# Packs the portable ToolBox.exe built by installer\build.ps1 into Microsoft Store MSIX packages:
#   dist\ToolBox-<version>-<arch>.msix     (one per architecture)
#   dist\ToolBox-<version>.msixbundle      (all architectures; upload this one to Partner Center)
# The Store signs MSIX packages itself, so these are NOT signed here (they cannot be side-loaded
# as-is). Requires the Windows SDK (makeappx.exe) and a prior build.ps1 run for the same version.
#
# IdentityName / Publisher / PublisherDisplayName must match Partner Center ->
# Product management -> Product identity (Package/Identity/Name, Package/Identity/Publisher,
# Package/Properties/PublisherDisplayName) exactly.
#
# Usage: powershell -ExecutionPolicy Bypass -File installer\make-msix.ps1 -Version 1.1.0 `
#          -IdentityName 12345Publisher.MyToolBox -Publisher "CN=XXXXXXXX-XXXX-..." -PublisherDisplayName "Tanbin"
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName,
    [string[]]$Arch = @("x64", "x86", "arm64")
)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$msixVersion = "$Version.0"   # the Store reserves the last segment and requires it to be 0
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$build = Join-Path $here "build"
$dist = Join-Path $root "dist"

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found (install the Windows 10/11 SDK)" }

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "web\icon-256.png"))
function New-Logo([string]$path, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)
    $side = [math]::Min($w, $h)
    $g.DrawImage($icon, [int](($w - $side) / 2), [int](($h - $side) / 2), $side, $side)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$bundleDir = Join-Path $build "msixbundle"
if (Test-Path $bundleDir) { Remove-Item $bundleDir -Recurse -Force }
New-Item -ItemType Directory -Force $bundleDir | Out-Null

foreach ($a in $Arch) {
    $exe = Join-Path $build "$a\ToolBox.exe"
    if (-not (Test-Path $exe)) { throw "$exe not found - run installer\build.ps1 first" }
    Write-Host "[$a] MSIX"
    $layout = Join-Path $build "msix-$a"
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $layout "Assets") | Out-Null
    Copy-Item $exe $layout
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $layout "LICENSE.txt")
    New-Logo (Join-Path $layout "Assets\StoreLogo.png") 50 50
    New-Logo (Join-Path $layout "Assets\Square44x44Logo.png") 44 44
    New-Logo (Join-Path $layout "Assets\Square44x44Logo.targetsize-44_altform-unplated.png") 44 44
    New-Logo (Join-Path $layout "Assets\Square150x150Logo.png") 150 150
    New-Logo (Join-Path $layout "Assets\Wide310x150Logo.png") 310 150

    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="$IdentityName" Publisher="$Publisher" Version="$msixVersion" ProcessorArchitecture="$a" />
  <Properties>
    <DisplayName>My Tool Box</DisplayName>
    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="ToolBox" Executable="ToolBox.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="My Tool Box" Description="Everyday IT tools for Windows"
                          BackgroundColor="transparent"
                          Square150x150Logo="Assets\Square150x150Logo.png"
                          Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    [IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest, (New-Object Text.UTF8Encoding $false))

    $out = Join-Path $dist "ToolBox-$Version-$a.msix"
    if (Test-Path $out) { Remove-Item $out }
    & $makeappx.FullName pack /o /d $layout /p $out
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $a" }
    Copy-Item $out $bundleDir
}
$icon.Dispose()

$bundle = Join-Path $dist "ToolBox-$Version.msixbundle"
if (Test-Path $bundle) { Remove-Item $bundle }
& $makeappx.FullName bundle /o /d $bundleDir /bv $msixVersion /p $bundle
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }
Write-Host "Done:"; Get-ChildItem $dist -Filter "*$Version*.msix*" | ForEach-Object { Write-Host "  $($_.Name)  $([math]::Round($_.Length/1MB,2)) MB" }
