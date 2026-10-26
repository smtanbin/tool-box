# Microsoft Store listing — English (en-us)

Copy each field into Partner Center → your app → **Store listings → English**.
Character counts are in brackets; the limits are Partner Center's.

---

## Product name *(max 100)*

```
My Tool Box
```

## Description *(10–10,000 characters, ~2,000 used)*

```
My Tool Box puts the everyday IT tools you reach for into one fast, clean Windows app. See where your disk space went, check your drives' health, calculate subnets, scan your network, ping and test ports, and hash, encrypt, encode and generate things, all in one native app, without an account or an internet connection.

DISK
• Folder Size – scan any drive or folder in seconds and see exactly what is using the space. An interactive treemap, pie chart, file-type breakdown, largest-files list, name search with wildcards and a content-based duplicate finder. Open items in File Explorer or export the results to CSV.
• Disk info – drive model, serial number, firmware, interface (NVMe, SATA, USB), capacity, partitions and a clear Good / Caution / Bad health badge, with temperature, power-on hours and the full S.M.A.R.T. attribute table.

NETWORK
• Subnet calculator – network, broadcast, host range, wildcard mask, address class, binary and hex views, and splitting a network into smaller subnets.
• IP scanner – find devices on your network by ping, ARP and optional TCP port checks, with host names, latency and MAC addresses. Export to CSV.
• Ping – continuous ping with a live latency chart plus min, average, max, jitter and packet loss.
• Telnet / ports – test whether TCP ports are open and talk to a service in a raw TCP console.

CRYPTO AND TEXT
• Hash text and files – MD5, SHA-1, SHA-256, SHA-384 and SHA-512, with optional HMAC.
• Encrypt and decrypt – AES-256-GCM with a passphrase.
• Token and UUID generator – UUID v4 and v7, ULID, random tokens and passwords.
• Encoders – Base64, URL, HTML entities, hex and Unicode escapes.

DEVELOPER
• JWT decoder, JSON formatter, timestamp converter and number base converter.

DESIGNED FOR WINDOWS 11
A native-feeling Fluent Design interface that follows your light or dark theme and your Windows accent colour, with an Explorer-style address bar and command bar.

PRIVATE BY DESIGN
Everything runs on your PC. There is no account, no ads, no analytics and no telemetry. The network tools only contact the hosts you type in. Free for personal and business use.
```

## Short description *(max 1,000)*

```
Everyday IT tools for Windows in one app: folder size and disk health, subnet calculator, IP scanner, ping and port tester, hardware info, process monitor, hashing, encryption, UUID and token generator, encoders, JWT, JSON and more. Fast, private and works offline.
```

## What's new in this version *(max 1,500)*

```
Version 3.0.0
• Rebuilt as a native Windows app: faster to start, follows your Windows light or dark theme, and no longer needs a browser window.
• New Home dashboard with live CPU, memory, disk and network, your recent tools, and search (Ctrl+K).
• Folder Size: tree with size bars, treemap, pie chart, file types, largest files, search and duplicate finder.
• Network adapters show a live download and upload graph. Ping handles several hosts at once.
• Hardware info, Temperatures, Process monitor, Hosts file editor and Environment variables.
• Still private and works offline.
```

## Product features *(up to 20, each max 200)*

```
Folder Size: interactive treemap, pie chart, file types, largest files and a duplicate finder
Disk info: drive model, serial, temperature, S.M.A.R.T. table and a Good / Caution / Bad health badge
Subnet calculator with binary and hex views and subnet splitting
IP scanner using ping, ARP and TCP ports, with host names and MAC addresses
Ping with a live latency chart and jitter and packet-loss statistics
Telnet / ports: TCP port tester and raw TCP console
Hash text and files (MD5, SHA-1, SHA-256, SHA-384, SHA-512) with optional HMAC
Encrypt and decrypt text with AES-256-GCM and a passphrase
UUID v4/v7, ULID, token and password generator
Base64, URL, HTML, hex and Unicode encoders, JWT decoder, JSON formatter and timestamp converter
Windows 11 Fluent Design with light and dark themes and your accent colour
Private: no account, no ads, no telemetry, works offline
```

## Search terms *(up to 7, each max 30)*

```
disk space analyzer
folder size
subnet calculator
ip scanner
ping tool
hash generator
uuid generator
```

## Copyright and trademark info *(max 200)*

```
© 2026 Tanbin (magestica.com). Windows and Microsoft are trademarks of Microsoft Corporation.
```

## Developed by *(max 255)*

```
Tanbin (magestica.com)
```

## Additional license terms *(max 10,000)*

```
My Tool Box is licensed under the Apache License 2.0 with the Commons Clause.

You may use, copy, modify and share My Tool Box for free, for personal or business use. You may not sell it or make money from it, for example by selling copies or charging for hosting or support based on it.

The software is provided "as is", without warranty of any kind.

Full license text: https://github.com/smtanbin/tool-box/blob/main/LICENSE
Privacy policy: https://github.com/smtanbin/tool-box/blob/main/PRIVACY.md
```

---

## Links and category

| Field | Value |
|---|---|
| Privacy policy URL | https://github.com/smtanbin/tool-box/blob/main/PRIVACY.md |
| Website | https://magestica.com/ |
| Support contact | https://github.com/smtanbin/tool-box/issues |
| Category / subcategory | **Developer tools** → Utilities (or **Utilities & tools** → System) |
| Languages | English |

## Images

| Partner Center slot | File |
|---|---|
| Store logo, 1:1 (300×300) | `store/logos/logo-300x300.png` |
| Poster art, 2:3 (720×1080, recommended) | `store/logos/poster-720x1080.png` |
| Poster art, 2:3 (1440×2160, recommended, sharper on large displays) | `store/logos/poster-1440x2160.png` |
| Poster art, 1:1 (1080×1080), optional | `store/logos/poster-1080x1080.png` |
| Screenshots, 16:9 (1600×900, add all four) | `store/screenshots/01-folder-size-light.png` · `02-folder-size-dark.png` · `03-subnet-calculator.png` · `04-token-uuid-generator.png` |

Screenshot captions (optional, max 200 each):
1. `Folder Size: see what is using your disk space with a treemap`
2. `Dark theme that follows Windows`
3. `Subnet calculator with every value one click from the clipboard`
4. `UUID, ULID, token and password generator`

## Notes for certification *(Submission options → Notes for certification, max 2,000)*

```
My Tool Box is a native Windows desktop app (WinUI 3, packaged as MSIX). No sign-in, account or test credentials are required.

INSTALL
Install it from the Store. It installs only this app (no bundled or third-party software) and uninstalls from Settings > Apps > Installed apps.

HOW IT WORKS
The app is self-contained: the .NET and Windows App SDK runtimes are inside the package. It opens no listening network port and has no embedded browser. Nothing is uploaded: there are no accounts, ads, analytics or telemetry. The package declares the runFullTrust capability because the tools read system information (hardware, drives, processes, network adapters) and some can change system settings the user asks them to change (hosts file, environment variables, end a process).

HOW TO TEST (no setup needed)
1. Folder Size: type C:\Program Files in the address bar and press Enter. Explore the tree, treemap and pie chart.
2. Subnet calculator: enter 192.168.1.10/24.
3. Token & UUID: press Generate.
4. Hash text: type any text.
5. Ping: enter 127.0.0.1.
6. Disk info: shows drive details. The S.M.A.R.T. table for SATA drives needs administrator rights; the app offers a "Restart as administrator" button.

Network tools (IP scanner, Ping, Telnet / ports) only contact the hosts the user types in. Files are only changed when the user confirms a delete (moved to the Recycle Bin).

Privacy policy: https://github.com/smtanbin/tool-box/blob/main/PRIVACY.md
Source code: https://github.com/smtanbin/tool-box
```

## Other submission answers

| Question | Answer |
|---|---|
| Price | Free |
| Age rating | Answer **No** to everything: no violence, sexual content, gambling, user-generated content sharing, or purchases; the app does not collect personal data |
| Installer parameters | `/qn /norestart` |
| App type | MSIX bundle (`ToolBox-<version>.msixbundle`: x64, x86 and arm64 in one file) |
| Minimum OS | Windows 10 version 1809 or later |
| Needs a browser | No |
| Code signing | Not needed: the Store signs MSIX packages |
| Capabilities | runFullTrust (reads hardware, drives, processes and network information; edits the hosts file and environment variables when asked) |

Build it with `.\build.ps1 -Msix -Arch x64,x86,arm64` and upload the bundle from `dist` to Partner Center.
The package identity must match Product management -> Product identity (see the README).
