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
My Tool Box puts the everyday IT tools you reach for into one fast, clean Windows app. See where your disk space went, check your drives' health, calculate subnets, scan your network, ping and test ports, and hash, encrypt, encode and generate things, all without a browser tab, an account or an internet connection.

DISK
• Folder Size – scan any drive or folder in seconds and see exactly what is using the space. An interactive treemap, pie chart, file-type breakdown, largest-files list, name search with wildcards and a content-based duplicate finder. Move files to the Recycle Bin, open them in File Explorer or export the results to CSV.
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
Everyday IT tools for Windows in one app: folder size and disk health, subnet calculator, IP scanner, ping and port tester, hashing, encryption, UUID and token generator, encoders, JWT, JSON and more. Fast, private and works offline.
```

## What's new in this version *(max 1,500)*

```
Version 2.0.0
• New Environment variables and PATH editor: user and system variables, a PATH list with duplicate and missing-folder checks, automatic backup before every change.
• New Network adapters page: IP, mask, gateway, DNS, DHCP, MAC and link speed for every adapter.
• New DNS lookup (A, AAAA, MX, NS, TXT, CNAME, PTR, SRV, with your own DNS server) and Traceroute with live hops.
• Also in this release line: Process monitor with kill and port owners, Hosts file editor, Disk info with maker, controller and DRAM-less detection, Image to Base64 and Image to ICO, and in-app confirmation dialogs.
• Two new skins in the Skin dropdown: Aero (Windows 7 Explorer style) and Magestica (burgundy).
• Tidier internals and the same private, offline design.
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
My Tool Box is a standalone Windows desktop app (MSI installer, per-user, no administrator rights needed). No sign-in, account or test credentials are required.

INSTALL
Silent install: msiexec /i ToolBoxSetup-2.0.0-x64.msi /qn /norestart
The installer adds Desktop and Start menu shortcuts and one entry in Settings > Apps > Installed apps named "My Tool Box". It installs only this app (no bundled or third-party software) and uninstalls cleanly from there.

HOW IT WORKS
The app starts a small local web server bound to 127.0.0.1 only (not reachable from other computers) and shows its interface in a Microsoft Edge or Google Chrome app window, both built into Windows. Closing the window exits the app. Nothing is uploaded: there are no accounts, ads, analytics or telemetry.

HOW TO TEST (no setup needed)
1. Folder Size: type C:\Program Files in the address bar and press Enter. Explore the treemap and pie chart.
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
| App type | MSI (one package each for x64, x86 and arm64) |
| Minimum OS | Windows 10 version 1809 or later |
| Needs Microsoft Edge or Chrome | Yes, built into Windows 10/11 |
| Code signing | Required by the Store for MSI/EXE packages. Sign each installer before submitting. |
| Package URL x64 | `https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-x64.msi` |
| Package URL x86 | `https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-x86.msi` |
| Package URL arm64 | `https://smtanbin.github.io/tool-box/downloads/2.0.0/ToolBoxSetup-2.0.0-arm64.msi` |

The Package URL must **not redirect**. GitHub Release links redirect (HTTP 302), so use
the GitHub Pages URLs above (needs Settings → Pages → `gh-pages` branch, root).
