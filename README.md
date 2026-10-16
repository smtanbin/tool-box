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
  The drive's **maker**, **controller** (vendor from the NVMe PCI ID, model from
  a built-in list of well-known drives) and a **DRAM / DRAM-less** badge are shown
  too. Windows has no "has DRAM" flag, so the verdict comes from the NVMe Host
  Memory Buffer request plus the model list and is labelled "probably" when it is
  only the HMB hint.

**System**
- **Hardware info** — CPU-Z style tabs (Processor, Mainboard, Memory with per-module SPD detail, Graphics, System), brand badges drawn from the CPU / GPU name, live charts for every core and for memory use,: code name, package, process, family / model,
  instruction sets and cache levels read with CPUID, live core clock, BIOS and microcode, memory modules
  (slot, size, type, speed, part number) with live usage, graphics (video memory, driver, resolution), and OS details.
  A plain-text report can be copied (serial numbers left out).
- **Process monitor** — live process list (CPU, memory, threads, path, listening
  ports), a Ports tab with every TCP / UDP port and the program that owns it, and
  **End process**, **End tree** and **Kill port owner** (type a port, free it).
  System-critical processes are protected; some need an administrator copy.
- **Hosts file** — edit `drivers\etc\hosts` as a table (enable / disable, add,
  delete, notes, validation) or as raw text. A timestamped backup is made on every
  save and the DNS cache is flushed. Saving needs an administrator copy (one click).
- **Environment & PATH** — user and system variables with a table editor; PATH opens
  as an ordered list with duplicate / missing-folder markers, move up / down, and
  "remove duplicates / missing". Keeps expandable (`%VAR%`) values, backs up all
  variables first, and tells running programs about the change. System variables
  need an administrator copy.
- **Network adapters** — every adapter's IPv4 / IPv6, mask, gateway, DNS servers,
  DHCP server, MAC, link speed and MTU, with copy buttons.
- **DNS lookup** — A, AAAA, MX, NS, TXT, CNAME, PTR and SRV via the system resolver
  or a server you pick (Cloudflare, Google, Quad9 or your own).
- **Traceroute** — hop-by-hop ICMP route with three probes per hop, host names,
  and a stop button. No administrator rights needed.

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

**Image**
- **Image to Base64** — drop, pick or paste an image: data URI, plain Base64, CSS
  or `<img>` output; paste Base64 to preview and save the picture back.
- **Image to ICO** — build a multi-size `.ico` (16-256 px) from PNG, JPG, SVG or
  WebP with fit / crop / background options. Runs entirely in the browser.

**Developer** · JWT decoder · JSON formatter · Timestamp converter · Number base converter

**Home** — the app opens on a Home page with your **Recent** and **Most used** tools, an all-tools grid and links to magestica.com and GitHub. Usage is counted only in the app window's local storage on your computer; nothing is sent anywhere.

**Sidebar groups** — in every skin except Fluent, click a group heading (Disk, Network, ...) to fold or unfold it; the state is remembered and the group of the tool you open reopens itself.

**Skins** — the *Skin* dropdown in the top bar (or *View > Skin*) switches the look: **Fluent** (default, Windows 11 look with full-colour Settings-style icons, follows your
light / dark setting and accent colour), **Aero** (Windows 7 Explorer look: glass bars, glossy colour icons, glossy
buttons, green progress bar; always light) and **Magestica** (the magestica.com look: warm white, huge tight type with a burgundy line, pill buttons, hairline cards; light and dark) and **Carbon** (IBM Carbon design: White theme in light mode, Gray 100 in dark; square corners, flat layers, your Windows accent as the interactive colour, Carbon blue if none). Remembered. Skins live in `src/web/skins.css`.

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
cd src
go build -ldflags "-H windowsgui -s -w" -o ..\ToolBox.exe .
```

The `src/rsrc_windows_*.syso` files embed the app icon (`src/app.ico`) and the version
info shown in Explorer's Properties > Details tab (edit `src/versioninfo.json`). The
installer build regenerates them for the release version; to do it by hand:

```
powershell -ExecutionPolicy Bypass -File installer\make-syso.ps1 -Version 2.0.0
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
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 2.0.0
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 2.0.0 -Arch x64
```

### Releases (CI/CD)

GitHub Actions builds every push for all three architectures (`.github/workflows/ci.yml`).
Pushing a version tag builds the installers and publishes a GitHub Release
(`.github/workflows/release.yml`):

```
git tag v2.0.0
git push origin v2.0.0
```

The installers are also published to GitHub Pages, which serves files directly
with no redirect (GitHub Release download links redirect, which the Microsoft Store
rejects). These are the URLs to use as the Store "Package URL":

```
https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-x64.msi
https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-x86.msi
https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-arm64.msi
```

One-time setup: repo **Settings → Pages → Build and deployment → Deploy from a
branch → `gh-pages` / `/ (root)`**.

## How it works

A small Go HTTP server listens on `127.0.0.1` only and serves an embedded
single-page UI (`src/web/`). Windows features (folder picker, Recycle Bin, drive
list, ICMP ping) use the Win32 API directly through `syscall`. Nothing is sent
off your PC, and closing the window quits the app.


### Code layout

| Path | What it holds |
|------|---------------|
| `src/main.go` | start-up, flags, static files, route table |
| `src/folder_api.go`, `src/scan.go`, `src/dupes.go` | Folder Size: scanner, tree, duplicate finder, HTTP handlers |
| `src/diskinfo_windows.go`, `src/diskmeta_windows.go` | Disk info: SMART / NVMe health; maker, controller and DRAM detection |
| `src/process_windows.go`, `src/hosts_windows.go`, `src/env_windows.go` | Process monitor (processes, ports, kill); hosts file editor; environment variables and PATH |
| `src/netinfo_windows.go` | network adapters (GetAdaptersAddresses), DNS lookup, traceroute hops |
| `src/nettools.go`, `src/icmp_windows.go` | Ping, port tester, IP scanner, Telnet |
| `src/winapi_windows.go` | shared Win32 helpers (folder picker, Recycle Bin, launching the window) |
| `src/web/index.html`, `src/web/skins.css` | page shell, styles and the Folder Size UI; Windows 7 Aero and Magestica burgundy skins |
| `src/web/tools.js` | Disk info, network, crypto and developer tool pages |
| `src/web/proc.js`, `src/web/hosts.js`, `src/web/env.js`, `src/web/sysnet.js`, `src/web/image.js` | Process monitor; Hosts editor; Environment and PATH; Adapters, DNS and Traceroute; Image tools |
| `src/web/boot.js` | opens the first tool (loaded last) |
| `src/` | all application code: Go sources, `web/` UI, icon and version resources |
| `installer/`, `store/` | MSI / MSIX build scripts and Microsoft Store listing |
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

### Third-party

The **Aero** skin uses [7.css](https://khang-nd.github.io/7.css) (v0.19.0, MIT, © 2021 Khang Nguyen Duy)
for its Windows 7 buttons, drop-downs and tables. It is bundled in `src/web/vendor/7.css`
(licence in `src/web/vendor/7.css-LICENSE.txt`) and only loaded while that skin is on.
