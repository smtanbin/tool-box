# Copyright 2026 Tanbin (magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# Builds a SIGNED MSIX that can be installed directly (double-click), for testing or for handing out outside the Store.
# The Store build (make-msix.ps1) is unsigned and carries the Partner Center publisher, so Windows refuses to install it by hand
# ("This app package's publisher certificate could not be verified"). This build takes its Publisher from YOUR code-signing
# certificate, so the two match, and signs every package with signtool.
#
# Usage (run build.ps1 first, or copy the exes to installer\build\<arch>\ToolBox.exe):
#   powershell -ExecutionPolicy Bypass -File installer\make-sideload.ps1 -Version 2.0.0 -SignPfx C:\path\cert.pfx -SignPfxPassword "..."
# Then trust the certificate once per PC, as Administrator (a self-signed certificate must also go into Root):
#   certutil -addstore TrustedPeople dist\sideload\ToolBox-signing.cer
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$SignPfx,
    [string]$SignPfxPassword = "",
    [string]$IdentityName = "Magestica.MyToolBox.Sideload",
    [string]$PublisherDisplayName = "Magestica",
    [string[]]$Arch = @("x64", "x86", "arm64")
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$out = Join-Path $root "dist\sideload"
New-Item -ItemType Directory -Force $out | Out-Null

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $SignPfx, $SignPfxPassword
$subject = $cert.Subject
Write-Host "Signing as $subject"
[IO.File]::WriteAllBytes((Join-Path $out "ToolBox-signing.cer"), $cert.Export("Cer"))

if ($env:SIGNTOOL -and (Test-Path $env:SIGNTOOL)) { $signtool = Get-Item $env:SIGNTOOL }
else {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
}
if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK, or set SIGNTOOL to its path)" }

& (Join-Path $here "make-msix.ps1") -Version $Version -IdentityName $IdentityName -Publisher $subject -PublisherDisplayName $PublisherDisplayName -Arch $Arch -OutDir $out

foreach ($f in Get-ChildItem $out -Recurse | Where-Object { $_.Extension -in ".msix", ".msixbundle" }) {
    & $signtool.FullName sign /fd SHA256 /f $SignPfx /p $SignPfxPassword /tr http://timestamp.digicert.com /td SHA256 $f.FullName
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $($f.Name)" }
}
Write-Host "Signed packages in $out"
Get-ChildItem $out | ForEach-Object { Write-Host "  $($_.Name)  $([math]::Round($_.Length/1MB,2)) MB" }
Write-Host "Trust the certificate once (Administrator):  certutil -addstore TrustedPeople `"$out\ToolBox-signing.cer`""
