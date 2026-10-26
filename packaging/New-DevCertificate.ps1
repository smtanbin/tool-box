# Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
# Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
#
# Makes a self-signed code-signing certificate for TESTING a sideloaded MSIX. Writes dist\sideload\ToolBox-dev.pfx and .cer.
# To install a package signed with it, trust the .cer once on that PC (Administrator):
#   certutil -addstore TrustedPeople dist\sideload\ToolBox-dev.cer
# Real releases use the Microsoft Store (which signs for you) or your own purchased code-signing certificate.
param(
    [string]$Subject = "CN=Magestica Dev",
    [string]$Password = "toolbox-dev",
    [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root "dist\sideload" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature -FriendlyName "My Tool Box (dev)" `
    -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(2) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pfx = Join-Path $OutDir "ToolBox-dev.pfx"; $cer = Join-Path $OutDir "ToolBox-dev.cer"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $Password -AsPlainText -Force) | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"   # the files are the deliverable; keep the user's certificate store clean
Write-Host "Created $pfx (password '$Password') and $cer"
Write-Host "Subject: $($cert.Subject)"
