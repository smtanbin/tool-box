# My Tool Box

A growing collection of everyday IT tools in one native-feeling Windows 11 app.
Single portable `.exe`, no installer, works offline. Written in Go with a Fluent
Design interface that follows your Windows light/dark theme and accent colour.

## Tools

**Disk**
- **Folder Size** — find out where your disk space went. Fast parallel scanner
  with a squarified treemap, pie chart, file-type breakdown, largest files,
  name search (`*` `?` wildcards) and a content-based duplicate finder.
  Delete to the Recycle Bin, open in Explorer, export to CSV.
- **Disk info** — CrystalDiskInfo-style drive health: model, serial, firmware,
  interface (NVMe / SATA / USB), capacity, partitions and drive letters, a
  Good / Caution / Bad health badge, temperature, power-on hours and count, host
  reads / writes, and the full S.M.A.R.T. attribute table. NVMe health works as a
  normal user; SATA SMART needs **Run as administrator** (one click in the app).

**Network**
- **Subnet calculator** — IPv4 address with prefix or netmask: network, broadcast,
  host range, wildcard, class, address type, binary/hex views, split into subnets.
- **IP scanner** — scan a CIDR or range by ping, ARP (local subnets) and optional
  TCP ports; shows host names, latency, MAC addresses; cancellable, CSV export.
- **Ping** — continuous or N-count ICMP ping (no admin needed) with a live latency
  chart and min / avg / max / jitter / loss.
- **Telnet / ports** — TCP port tester (open / closed / filtered with timing) and a
  raw TCP / Telnet console.

**Crypto & text**
- **Hash text** — MD5, SHA-1, SHA-256, SHA-384, SHA-512 of text or a file, optional HMAC.
- **Encrypt / decrypt** — AES-256-GCM with a passphrase (PBKDF2-SHA-256, 210,000 rounds).
- **Token & UUID** — UUID v4 / v7, ULID, random tokens and passwords.
- **Base64 & encoders** — Base64, Base64url, URL, HTML entities, hex, Unicode escapes.

**Developer** · JWT decoder · JSON formatter · Timestamp converter · Number base converter

Everything runs on your PC; the crypto and developer tools work entirely offline.

## Install

Download the installer for your PC from
[Releases](https://github.com/smtanbin/tool-box/releases/latest):
`ToolBoxSetup-<version>-x64.msi` (most PCs), `-x86.msi` (32-bit Windows) or
`-arm64.msi` (Windows on ARM). Portable `ToolBox-<version>-<arch>.exe` builds are
there too.

The installer works for the current user only (no
administrator rights needed) into `%LocalAppData%\Programs\My Tool Box`, adds
**My Tool Box** shortcuts to the Desktop and Start menu, and can be removed from
**Settings → Apps → Installed apps**. Installing a newer version upgrades in place.

## Run

Or skip the installer: download or build `ToolBox.exe` and double-click it. It opens in its own app
window (Microsoft Edge or Chrome in app mode, both already on Windows 11).

```
ToolBox.exe                  # open the app
ToolBox.exe D:\Projects      # open and scan a folder straight away
ToolBox.exe -nobrowser -port 8765   # run the server only (for development)
```

Some data (protected folders, disk health details) needs **Run as administrator**.

## Build

Requires Go 1.24 or newer. No C compiler or cgo needed.

```
go build -ldflags "-H windowsgui -s -w" -o ToolBox.exe .
```

`rsrc_windows_amd64.syso` embeds the app icon (`app.ico`). To regenerate it:

```
go run github.com/akavel/rsrc@v0.10.2 -ico app.ico -o rsrc_windows_amd64.syso
```

### Installer

Requires the [WiX Toolset](https://wixtoolset.org) v5 with the UI and Util extensions:

```
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
wix extension add -g WixToolset.Util.wixext/5.0.2
```

Then build `dist\ToolBoxSetup-<version>-<arch>.msi` and portable
`dist\ToolBox-<version>-<arch>.exe` for x64, x86 and arm64:

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.1
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.1 -Arch x64
```

### Releases (CI/CD)

GitHub Actions builds every push for all three architectures (`.github/workflows/ci.yml`).
Pushing a version tag builds the installers and publishes a GitHub Release
(`.github/workflows/release.yml`):

```
git tag v1.0.2
git push origin v1.0.2
```

The installers are also published to GitHub Pages, which serves files directly
with no redirect (GitHub Release download links redirect, which the Microsoft Store
rejects). These are the URLs to use as the Store "Package URL":

```
https://smtanbin.github.io/tool-box/downloads/1.0.2/ToolBoxSetup-1.0.2-x64.msi
https://smtanbin.github.io/tool-box/downloads/1.0.2/ToolBoxSetup-1.0.2-x86.msi
https://smtanbin.github.io/tool-box/downloads/1.0.2/ToolBoxSetup-1.0.2-arm64.msi
```

One-time setup: repo **Settings → Pages → Build and deployment → Deploy from a
branch → `gh-pages` / `/ (root)`**.

## How it works

A small Go HTTP server listens on `127.0.0.1` only and serves an embedded
single-page UI (`web/`). Windows features (folder picker, Recycle Bin, drive
list, ICMP ping) use the Win32 API directly through `syscall`. Nothing is sent
off your PC, and closing the window quits the app.

## License

Apache License 2.0 with the **Commons Clause** — see [LICENSE](LICENSE).

You may use, copy, modify and share My Tool Box for free, for personal or work use.
You may **not** sell it or make money from it (paid copies, hosting or support
based on it).

## Privacy

My Tool Box collects no personal data and sends nothing off your PC. Network tools
only contact the hosts you enter. Read the full
[Privacy Policy](PRIVACY.md): <https://github.com/smtanbin/tool-box/blob/main/PRIVACY.md>

## Credits

Designed and developed by **Tanbin** · Developer: [magestica.com](https://magestica.com/)
