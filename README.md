# My Tool Box

Everyday IT tools for Windows in one native app. Private, offline, free and open source.

Built with **WinUI 3** (Windows App SDK) and **C# / .NET 9**. It follows your Windows light or dark theme. No browser,
no web server, no accounts, no telemetry: everything runs on your PC.

Designed and developed by **Tanbin** · [www.magestica.com](https://www.magestica.com/)

## Tools

| Group | Tools |
|---|---|
| **Disk** | **Folder Size** (tree, treemap, pie chart, file types, largest files, search, duplicates, CSV export) · **Disk info** (health, temperature, S.M.A.R.T. / NVMe log, partitions, maker, controller and DRAM-less check) |
| **System** | **Hardware info** (processor, memory, every GPU / iGPU / NPU, network, live graphs) · **Temperatures** (live graph per sensor) · **Process monitor** (CPU, memory, ports, end task or tree) · **Hosts file** editor · **Environment variables & PATH** |
| **Network** | **Network adapters** (settings and live download / upload graph) · **DNS lookup** · **Ping** (several hosts, live latency graph) · **Traceroute** · **Subnet calculator** (with splitting) · **IP scanner** · **Telnet / ports** |
| **Developer** | Hash · Encrypt / decrypt (AES-256-GCM) · Token & UUID · Base64 & encoders · JSON formatter · JWT decoder · Timestamp · Number base · Image to Base64 · Image to ICO |

The **Home** page shows live CPU, memory, disk and network figures, your recent and most used tools, and a search box (Ctrl+K).

## Install

Get a build from the [Releases](https://github.com/smtanbin/tool-box/releases) page:

* **Installer:** `ToolBoxSetup-<version>-x64.msi` (or `-x86`, `-arm64`). Per-user, no administrator rights, adds Desktop and Start menu shortcuts. Silent install: `msiexec /i ToolBoxSetup-<version>-x64.msi /qn /norestart`.
* **Portable:** `ToolBox-<version>-x64.zip` (or `-x86`, `-arm64`). Unzip anywhere and run `ToolBox.exe`.
* **Microsoft Store:** the `.msixbundle` is the Store package.

Nothing needs to be installed first: the .NET and Windows App SDK runtimes are inside the app. Windows 10 version 1809 or newer.

Some tools need administrator rights to do everything (saving the hosts file, S.M.A.R.T. data for SATA drives, changing system
environment variables). Where that applies the app tells you and offers **Restart as administrator**.

## Build

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download) on Windows. Everything else (Windows App SDK, Windows SDK
build tools) is restored from NuGet.

```powershell
.\build.ps1                                 # build and publish x64 into dist\publish\x64
.\build.ps1 -Test                           # ... and smoke-test it: starts the app and calls every engine function once
.\build.ps1 -Installer                      # ... and make dist\ToolBoxSetup-<version>-x64.msi (installs the WiX tool on first use)
.\build.ps1 -Zip                            # ... and make dist\ToolBox-<version>-x64.zip
.\build.ps1 -Msix                           # ... and make the Microsoft Store MSIX (unsigned; the Store signs it)
.\build.ps1 -Msix -Arch x64,x86,arm64       # one MSIX per architecture plus dist\ToolBox-<version>.msixbundle
.\build.ps1 -Msix -SelfSign                 # a package you can install yourself, signed with a throw-away test certificate
```

The version lives in one place, `Directory.Build.props`. To work on the code open `ToolBox.sln` in Visual Studio 2022 or newer
(with the *Windows application development* workload), or run `dotnet build ToolBox.sln -p:Platform=x64`.

### Installing a self-signed test package

`build.ps1 -Msix -SelfSign` writes `dist\sideload\ToolBox-<version>-x64.msix` and its certificate. Trust the certificate once
(as Administrator), then double-click the package:

```powershell
certutil -addstore TrustedPeople dist\sideload\ToolBox-signing.cer
```

To sign with your own code-signing certificate use `-SignPfx my.pfx -SignPfxPassword ...` instead of `-SelfSign`.

### Microsoft Store

`IdentityName`, `Publisher` and `PublisherDisplayName` (parameters of `build.ps1`, and repository variables
`MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER`, `MSIX_PUBLISHER_DISPLAY_NAME` for the release workflow) must match Partner Center →
Product management → Product identity. Upload `ToolBox-<version>.msixbundle`. Listing text and notes are in [`store`](store).

### Releases

Push a version tag and the [Release workflow](.github/workflows/release.yml) builds x64, x86 and arm64, runs the smoke test
and publishes the zips, MSIX packages, bundle and checksums:

```powershell
git tag v3.0.0
git push origin v3.0.0
```

[CI](.github/workflows/ci.yml) builds and packages all three architectures for every push and pull request.

## Project layout

```
ToolBox.sln
Directory.Build.props        version and company, shared by every project
build.ps1                    build, test, zip and MSIX in one script
packaging/                   MSI installer (WiX), MSIX packaging and the test-certificate helper
store/                       Microsoft Store listing text, logos and screenshots
src/ToolBox/                 the app
  App.xaml, MainWindow.xaml  application, window and navigation menu
  Engine/                    everything that talks to Windows (no UI code)
    Router.cs                request router: pages ask for "/api/..." and C# answers
    Cached.cs                bounded-wait cache used for slow system calls
    Network/                 adapters, DNS, ping, traceroute, port test, IP scan, TCP console
    System/                  processes and ports, hosts, environment, folder scan, live CPU / GPU / temperature
    Hardware/                WMI, CPUID, DXGI, disk SMART / NVMe
  Pages/                     one class per tool, grouped Home, Disk, System, Network, Developer
  Controls/                  charts (line, dual, donut), treemap, wrap panel
  Common/                    shared UI helpers, file pickers, JSON helpers
  Diagnostics/               the opt-in smoke test (TOOLBOX_SELFTEST=1)
  Assets/                    icons
```

Pages never call Windows directly. They ask the engine for data (`Backend.GetAsync("/api/hw/cpu")`) and draw what comes back,
which keeps the UI code short and lets the engine be tested on its own (`build.ps1 -Test`).

### Handy environment variables

| Variable | Effect |
|---|---|
| `TOOLBOX_START=<tag>` | open a tool at start, for example `disk`, `ping`, `hosts` |
| `TOOLBOX_THEME=light` or `dark` | force a theme instead of following Windows |
| `TOOLBOX_SELFTEST=1` | run the engine smoke test and exit; the report is written to `%TEMP%\toolbox-selftest.txt` |

Unhandled errors are written to `%TEMP%\toolbox-crash.log` instead of closing the app silently.

## Privacy

Nothing is collected or sent anywhere. See [PRIVACY.md](PRIVACY.md).

## License

Apache License 2.0 with the **Commons Clause**: free for personal and business use, you may not sell it or make money from it
(paid copies, hosting or support). See [LICENSE](LICENSE).

## Support open source

If My Tool Box saves you time: star the project, [report what is broken or missing](https://github.com/smtanbin/tool-box/issues),
send a fix, or tell a colleague.
