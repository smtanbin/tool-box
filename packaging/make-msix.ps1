# Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# Wraps a published ToolBox folder (build.ps1 -> dist\publish\<arch>) into an MSIX package, and several of them
# into one .msixbundle. Called by build.ps1 -Msix; it can also be run by hand.
#
#   Store build   : no -SignPfx. The package is unsigned and carries the Partner Center publisher. The Store signs
#                   it. Windows will refuse to install it by hand.
#   Sideload build: -SignPfx cert.pfx. The Publisher is taken from the certificate, so the signature matches and the
#                   package installs with a double-click once the certificate is trusted (see New-DevCertificate.ps1).
#
# IdentityName / Publisher / PublisherDisplayName must match Partner Center -> Product management -> Product identity.
param(
    [Parameter(Mandatory)][string]$Version,
    [string[]]$Arch = @("x64"),
    [string]$PublishRoot = "",     # default: <repo>\dist\publish   (one sub-folder per architecture)
    [string]$OutDir = "",          # default: <repo>\dist
    [string]$IdentityName = "Magestica.MyToolBox",
    [string]$Publisher = "CN=F8598E86-A98F-4FD0-90D7-DA85D833223D",
    [string]$PublisherDisplayName = "Magestica",
    [string]$SignPfx = "",
    [string]$SignPfxPassword = ""
)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$msixVersion = "$Version.0"    # the Store reserves the last segment and requires it to be 0
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishRoot) { $PublishRoot = Join-Path $root "dist\publish" }
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }
$work = Join-Path $root "dist\msix-work"
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

# Windows SDK tools: an explicit path, the installed SDK, or the Microsoft.Windows.SDK.BuildTools NuGet package the project already restores
function Find-SdkTool([string]$name, [string]$envVar) {
    $override = [Environment]::GetEnvironmentVariable($envVar)
    if ($override -and (Test-Path $override)) { return (Get-Item $override).FullName }
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools")
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem $r -Recurse -Filter $name -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "$name not found. Install the Windows 10/11 SDK or set `$env:$envVar to its path."
}
$makeappx = Find-SdkTool "makeappx.exe" "MAKEAPPX"
$signtool = if ($SignPfx) { Find-SdkTool "signtool.exe" "SIGNTOOL" } else { $null }

# Tile and Store logos are cut from the app icon
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "src\ToolBox\Assets\icon-256.png"))
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

function Sign-File([string]$path) {
    if (-not $SignPfx) { return }
    & $signtool sign /fd SHA256 /f $SignPfx /p $SignPfxPassword /tr http://timestamp.digicert.com /td SHA256 $path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $path" }
}

$bundleDir = Join-Path $work "bundle"
New-Item -ItemType Directory -Force $bundleDir | Out-Null
$built = @()
foreach ($a in $Arch) {
    $src = Join-Path $PublishRoot $a
    if (-not (Test-Path (Join-Path $src "ToolBox.exe"))) { throw "$src\ToolBox.exe not found. Run build.ps1 first." }
    Write-Host "[$a] MSIX"
    $layout = Join-Path $work "layout-$a"
    New-Item -ItemType Directory -Force (Join-Path $layout "Assets") | Out-Null
    Copy-Item (Join-Path $src "*") $layout -Recurse -Force
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

    $out = Join-Path $OutDir "ToolBox-$Version-$a.msix"
    if (Test-Path $out) { Remove-Item $out }
    & $makeappx pack /o /d $layout /p $out | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $a" }
    Sign-File $out
    Copy-Item $out $bundleDir
    $built += $out
}
$icon.Dispose()

if ($Arch.Count -gt 1) {
    $bundle = Join-Path $OutDir "ToolBox-$Version.msixbundle"
    if (Test-Path $bundle) { Remove-Item $bundle }
    & $makeappx bundle /o /d $bundleDir /bv $msixVersion /p $bundle | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }
    Sign-File $bundle
    $built += $bundle
}
Remove-Item $work -Recurse -Force
Write-Host "MSIX output:"
$built | ForEach-Object { Write-Host ("  {0}  {1} MB" -f (Split-Path $_ -Leaf), [math]::Round((Get-Item $_).Length / 1MB, 1)) }
