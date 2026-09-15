# π1 Hyper-V Toolkit

A read-only Windows desktop toolkit for Hyper-V and Microsoft Failover Cluster inventory, diagnostics and reporting.

Native C# / .NET 10 WPF application for Windows x64, with Slovak and English UI.

Current version: **1.0.0-rc.2**

Status: **Release Candidate** — real Failover Cluster acceptance testing is still pending. This is not a final/stable 1.0.0.

## Features

- **Dashboard** — cluster, node, VM and storage overview
- **Virtual Machines** — overview, running / off, memory, networking, VMs without IP, checkpoints, selected VM detail
- **Nodes** — hardware overview, capacity/load, local volumes, network adapters, Hyper-V host settings
- **Failover Cluster** — nodes, roles, resources, networks, quorum, witness, events, VM ownership, preferred owners, VM distribution, placement advisor, failover simulation, health score, CSV information
- **Storage** — pools, virtual disks, physical disks, volumes, CSV, VM storage mapping, storage jobs
- **Networking** — virtual switches, VM adapters, VLAN
- **Diagnostics / Advisor** — RAM advisor, CSV low-free-space diagnostic, cluster resources not Online
- **Export** — CSV, HTML and JSON reports of the current visible report plus a full VM report

## Read-only design

The application is designed for inventory, diagnostics and reporting.

It does NOT intentionally perform:

- VM start/stop/restart
- migration
- checkpoint creation/removal
- cluster failover
- cluster configuration changes
- network changes
- storage changes

Allowed writes are only:

- application settings
- application logs
- explicitly exported report files

Administrator privileges are required for infrastructure discovery.

## Requirements

- Windows x64
- Administrator privileges
- Hyper-V components where Hyper-V information is required
- Microsoft Failover Cluster capability where cluster information is required

Release builds are self-contained: .NET 10 does NOT need to be installed separately for the packaged release.

## Download

Use [GitHub Releases](../../releases) and download:

```text
Pi1-HyperVToolkit-1.0.0-rc.2-win-x64.zip
```

Basic usage:

1. Download the ZIP
2. Extract it
3. Run `Pi1.HyperVToolkit.exe`
4. Accept the UAC prompt

### Current RC checksums (1.0.0-rc.2)

ZIP SHA256:

```text
EFAAF6371278A35E8C05CA8C5B7488745DAD08DD9E1F6CCA3F058710F7803E07
```

EXE SHA256:

```text
8C2834C309BADD9030FE838E7EB4A7DABA2531654918FD3AA0177C6821B27A2F
```

## Application data

Runtime data lives under:

```text
%LocalAppData%\Pi1\HyperVToolkit\
```

Logs are stored below that application data location.

## Languages

- Slovak
- English

The language can be switched from the application Settings.

## Build from source

Requirements:

- .NET 10 SDK
- Windows with a suitable Windows development environment (WPF)

```powershell
dotnet restore
dotnet build .\Pi1.HyperVToolkit.slnx
dotnet test .\Pi1.HyperVToolkit.slnx
```

Release packaging (creates a self-contained win-x64 package under `artifacts\release`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Publish-Release.ps1 -Version 1.0.0-rc.2
```

## Project structure

```text
src/
    Pi1.HyperVToolkit              WPF application
    Pi1.HyperVToolkit.Core         models, calculations, report schemas, localization
    Pi1.HyperVToolkit.Infrastructure   CIM/WMI collectors, scope, storage helpers
tests/
    Pi1.HyperVToolkit.Tests        automated test suite
build/
    release tooling (Publish-Release.ps1)
Modules/
Pi1-HyperVToolkit.ps1
README.txt
```

`Modules/` and `Pi1-HyperVToolkit.ps1` are the immutable legacy/reference PowerShell implementation used during the C# migration and parity validation. They are not executed by the new application.

## Test status

- 742 / 742 automated tests passing
- 0 errors
- 0 warnings

Real Failover Cluster live acceptance testing of the Release Candidate is pending.
