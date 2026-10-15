# Tool Box

A growing collection of everyday IT tools in one native-feeling Windows 11 app.
Single portable `.exe`, no installer, works offline. Written in Go with a Fluent
Design interface that follows your Windows light/dark theme and accent colour.

## Tools

**Disk**
- **Folder Size** — find out where your disk space went. Fast parallel scanner
  with a squarified treemap, pie chart, file-type breakdown, largest files,
  name search (`*` `?` wildcards) and a content-based duplicate finder.
  Delete to the Recycle Bin, open in Explorer, export to CSV.

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

Run `ToolBoxSetup-<version>.msi`. It installs for the current user only (no
administrator rights needed) into `%LocalAppData%\Programs\Tool Box`, adds
**Tool Box** shortcuts to the Desktop and Start menu, and can be removed from
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

Then build `dist\ToolBoxSetup-<version>.msi` (it rebuilds `ToolBox.exe` first):

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.0
```

## How it works

A small Go HTTP server listens on `127.0.0.1` only and serves an embedded
single-page UI (`web/`). Windows features (folder picker, Recycle Bin, drive
list, ICMP ping) use the Win32 API directly through `syscall`. Nothing is sent
off your PC, and closing the window quits the app.

## License

Apache License 2.0 with the **Commons Clause** — see [LICENSE](LICENSE).

You may use, copy, modify and share Tool Box for free, for personal or work use.
You may **not** sell it or make money from it (paid copies, hosting or support
based on it).

## Credits

Designed and developed by **Tanbin** · Developer: [magestica.com](https://magestica.com)
