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

**Network** · Subnet calculator · IP scanner · Ping · Telnet / port check

**Crypto & text** · Hash text · Encrypt / decrypt · Token & UUID generator ·
Base64 & encoders

Tools are being added steadily; see the left-hand menu in the app.

## Run

Download or build `ToolBox.exe` and double-click it. It opens in its own app
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
