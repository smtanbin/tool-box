# Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# One entry point for building My Tool Box.
#
#   .\build.ps1                                  build and publish x64 into dist\publish\x64
#   .\build.ps1 -Test                            ... and smoke-test the published exe (every engine call once)
#   .\build.ps1 -Zip                             ... and make a portable zip
#   .\build.ps1 -Msix                            ... and make the Microsoft Store MSIX (unsigned; the Store signs it)
#   .\build.ps1 -Msix -Arch x64,x86,arm64        ... one MSIX per architecture plus a .msixbundle (upload this to Partner Center)
#   .\build.ps1 -Msix -SelfSign                  sideload build signed with a throw-away test certificate (see packaging\New-DevCertificate.ps1)
#   .\build.ps1 -Msix -SignPfx my.pfx -SignPfxPassword ...   sideload build signed with your own certificate
#
# Needs: .NET 9 SDK (the Windows App SDK and the Windows SDK build tools are restored from NuGet).
# The version comes from Directory.Build.props unless -Version is given.
param(
    [string]$Version = "",
    [string[]]$Arch = @("x64"),                 # x64, x86, arm64 (comma separated also works with powershell -File)
    [string]$Configuration = "Release",
    [switch]$Test,
    [switch]$Zip,
    [switch]$Installer,
    [switch]$Msix,
    [switch]$SelfSign,
    [string]$SignPfx = "",
    [string]$SignPfxPassword = "",
    # Partner Center -> Product management -> Product identity
    [string]$IdentityName = "Magestica.MyToolBox",
    [string]$Publisher = "CN=F8598E86-A98F-4FD0-90D7-DA85D833223D",
    [string]$PublisherDisplayName = "Magestica",
    [switch]$Clean
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\ToolBox\ToolBox.csproj"
$dist = Join-Path $root "dist"
$publishRoot = Join-Path $dist "publish"

if (-not $Version) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -notmatch '<Version>(\d+\.\d+\.\d+)</Version>') { throw "No <Version> found in Directory.Build.props" }
    $Version = $Matches[1]
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET SDK not found. Install .NET 9: https://dotnet.microsoft.com/download" }

$Arch = @($Arch | ForEach-Object { $_ -split "[,; ]+" } | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
foreach ($a in $Arch) { if ($a -notin "x64", "x86", "arm64") { throw "Unknown architecture '$a' (use x64, x86, arm64)" } }
$platform = @{ x64 = "x64"; x86 = "x86"; arm64 = "ARM64" }
$rid = @{ x64 = "win-x64"; x86 = "win-x86"; arm64 = "win-arm64" }

if ($Clean -and (Test-Path $dist)) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Write-Host "My Tool Box $Version  ($Configuration, $($Arch -join ', '))" -ForegroundColor Cyan

foreach ($a in $Arch) {
    Write-Host "[$a] publish" -ForegroundColor Cyan
    $out = Join-Path $publishRoot $a
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    # self-contained: the .NET and Windows App SDK runtimes travel with the app, so nothing has to be installed first
    dotnet publish $project -c $Configuration -p:Platform=$($platform[$a]) -r $rid[$a] --self-contained true `
        -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $out --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $a" }
    $size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("[{0}] {1} files, {2} MB -> {3}" -f $a, (Get-ChildItem $out -Recurse -File).Count, [math]::Round($size / 1MB, 1), $out)
}

if ($Test) {
    $exe = Join-Path $publishRoot "$($Arch[0])\ToolBox.exe"
    if ($Arch[0] -ne "x64" -and [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne "Arm64") { Write-Warning "Skipping the smoke test: $($Arch[0]) cannot run on this PC."; }
    else {
        Write-Host "[$($Arch[0])] smoke test" -ForegroundColor Cyan
        $report = Join-Path ([IO.Path]::GetTempPath()) "toolbox-selftest.txt"
        Remove-Item $report -ErrorAction SilentlyContinue
        $env:TOOLBOX_SELFTEST = "1"
        try { $p = Start-Process $exe -PassThru; if (-not $p.WaitForExit(120000)) { $p.Kill(); throw "The smoke test did not finish in 2 minutes" } }
        finally { Remove-Item Env:TOOLBOX_SELFTEST -ErrorAction SilentlyContinue }
        if (-not (Test-Path $report)) { throw "The smoke test wrote no report (the app may have crashed; see $env:TEMP\toolbox-crash.log)" }
        Get-Content $report | ForEach-Object { Write-Host "  $_" }
        if ($p.ExitCode -ne 0) { throw "Smoke test failed" }
    }
}

if ($Zip) {
    foreach ($a in $Arch) {
        $z = Join-Path $dist "ToolBox-$Version-$a.zip"
        if (Test-Path $z) { Remove-Item $z }
        Compress-Archive -Path (Join-Path $publishRoot "$a\*") -DestinationPath $z -CompressionLevel Optimal
        Write-Host ("[{0}] {1}  {2} MB" -f $a, (Split-Path $z -Leaf), [math]::Round((Get-Item $z).Length / 1MB, 1))
    }
}

if ($Installer) {
    foreach ($a in $Arch) {
        $ia = @{ Version = $Version; Arch = $a; PublishRoot = $publishRoot; OutDir = $dist }
        if ($SignPfx) { $ia.SignPfx = $SignPfx; $ia.SignPfxPassword = $SignPfxPassword }
        & (Join-Path $root "packaging\make-installer.ps1") @ia
    }
}

if ($Msix) {
    $args2 = @{ Version = $Version; Arch = $Arch; PublishRoot = $publishRoot; OutDir = $dist; IdentityName = $IdentityName; Publisher = $Publisher; PublisherDisplayName = $PublisherDisplayName }
    if ($SelfSign -and -not $SignPfx) {
        & (Join-Path $root "packaging\New-DevCertificate.ps1") | Out-Null
        $SignPfx = Join-Path $dist "sideload\ToolBox-dev.pfx"; $SignPfxPassword = "toolbox-dev"
    }
    if ($SignPfx) {
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $SignPfx, $SignPfxPassword
        $args2.Publisher = $cert.Subject
        if ($IdentityName -eq "Magestica.MyToolBox") { $args2.IdentityName = "Magestica.MyToolBox.Sideload" }
        $args2.SignPfx = $SignPfx; $args2.SignPfxPassword = $SignPfxPassword
        $args2.OutDir = Join-Path $dist "sideload"
        New-Item -ItemType Directory -Force $args2.OutDir | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $args2.OutDir "ToolBox-signing.cer"), $cert.Export("Cer"))
        Write-Host "Sideload build, signed as $($cert.Subject)" -ForegroundColor Cyan
    }
    else { Write-Host "Store build (unsigned; the Microsoft Store signs it)" -ForegroundColor Cyan }
    & (Join-Path $root "packaging\make-msix.ps1") @args2
    if ($SignPfx) { Write-Host "Trust the certificate once, as Administrator:  certutil -addstore TrustedPeople `"$($args2.OutDir)\ToolBox-signing.cer`"" }
}

Write-Host "Done." -ForegroundColor Green
