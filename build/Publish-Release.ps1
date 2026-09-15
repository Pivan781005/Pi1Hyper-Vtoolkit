#Requires -Version 5.1
<#
.SYNOPSIS
  Builds the portable Windows x64 RELEASE CANDIDATE of π1 Hyper-V Toolkit.

.DESCRIPTION
  Reliability over package size: directory publish, self-contained,
  PublishTrimmed=false, PublishSingleFile=false. Versions are stamped via
  MSBuild properties only (repo sources keep their 0.9.4 development
  version; no final 1.0.0 is created). Produces:
    artifacts\release\Pi1-HyperVToolkit-<Version>-win-x64\
    artifacts\release\Pi1-HyperVToolkit-<Version>-win-x64.zip
  plus RELEASE-NOTES.txt, CLUSTER-ACCEPTANCE.txt and SHA256SUMS.txt inside
  the package. The ZIP is extracted to an independent temp directory and the
  EXTRACTED copy is audited. No commit / push / tag / upload / signing.

  Run from anywhere:  powershell -ExecutionPolicy Bypass -File build\Publish-Release.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0-rc.1",
    [string]$AssemblyVersion = "1.0.0.0",
    [string]$FileVersion = "1.0.0.0",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "===== $Name =====" -ForegroundColor Cyan
    & $Body
}

function Invoke-DotNet([string[]]$DotNetArgs) {
    Write-Host ("dotnet " + ($DotNetArgs -join " "))
    & dotnet @DotNetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($DotNetArgs[0]) failed with exit code $LASTEXITCODE."
    }
}

# 1. Locate repo root safely (this script lives in <root>\build\).
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "Pi1.HyperVToolkit.slnx"))) {
    throw "Repo root not found from script location: $PSScriptRoot"
}
Write-Host "Repo root: $repoRoot"

$folderName = "Pi1-HyperVToolkit-$Version-win-x64"
$releaseRoot = Join-Path $repoRoot "artifacts\release"
$publishDir = Join-Path $releaseRoot "_publish"
$stageDir = Join-Path $releaseRoot $folderName
$zipPath = Join-Path $releaseRoot "$folderName.zip"
$verifyRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Pi1RCVerify"

Invoke-Step "1. Clean previous release-artifact output" {
    # ONLY this script's own output locations are removed: nothing else in
    # the working tree (sources, bin/obj, Modules) is touched.
    foreach ($dir in @($releaseRoot, $verifyRoot)) {
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $verifyRoot -Force | Out-Null
}

Push-Location -LiteralPath $repoRoot
try {
    Invoke-Step "2. dotnet restore" {
        Invoke-DotNet @("restore", "Pi1.HyperVToolkit.slnx")
    }

    Invoke-Step "3. dotnet build $Configuration" {
        Invoke-DotNet @("build", "Pi1.HyperVToolkit.slnx", "-c", $Configuration, "--no-restore", "-v", "q")
    }

    Invoke-Step "4. dotnet test $Configuration" {
        Invoke-DotNet @("test", "Pi1.HyperVToolkit.slnx", "-c", $Configuration, "--no-build")
    }

    Invoke-Step "5. publish $Runtime self-contained" {
        # Versions are stamped via properties only; Directory.Build.props
        # (0.9.4 dev version) is NOT modified.
        Invoke-DotNet @(
            "publish", "src\Pi1.HyperVToolkit\Pi1.HyperVToolkit.csproj",
            "-c", $Configuration,
            "-r", $Runtime,
            "--self-contained", "true",
            "-p:PublishSingleFile=false",
            "-p:PublishTrimmed=false",
            "-p:Version=$Version",
            "-p:AssemblyVersion=$AssemblyVersion",
            "-p:FileVersion=$FileVersion",
            "-p:InformationalVersion=$Version",
            "-o", $publishDir,
            "-v", "q"
        )
    }
}
finally {
    Pop-Location
}

Invoke-Step "6. Copy clean runtime package (no PDBs)" {
    # PDBs are developer debug symbols, not runtime requirements; the RC
    # stays clean and diagnosable via its log files.
    Get-ChildItem -LiteralPath $publishDir -File -Recurse |
        Where-Object { $_.Extension -ne ".pdb" } |
        ForEach-Object {
            $relative = $_.FullName.Substring($publishDir.Length).TrimStart('\', '/')
            $target = Join-Path $stageDir $relative
            $parent = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Invoke-Step "7. Write RELEASE-NOTES.txt" {
    $notes = @"
π1 Hyper-V Toolkit
$Version

Read-only Hyper-V / Failover Cluster inventory, diagnostics and reporting tool.

Features:
- Dashboard
- Virtual Machines
- Nodes
- Cluster
- Storage
- Networking
- Diagnostics / Advisor
- CSV / HTML / JSON Export

Requirements:
- Windows x64
- Administrator rights (application manifest: requireAdministrator)

Runtime state (per-machine, outside this package):
- %LocalAppData%\Pi1\HyperVToolkit\  (settings, logs; user exports go where chosen)

Package:
- Framework-dependent: NO (self-contained, no system-installed .NET 10 required)
- Single file: NO (directory publish)
- Trimmed: NO
- Signed: NO ([NOT CONFIGURED] - unsigned internal release candidate)

THIS IS A RELEASE CANDIDATE.

Normal/non-cluster regression passed (see test gate in build log).

Real Failover Cluster acceptance is still pending.
Cluster validation is NOT claimed by this package.
"@
    $notes | Set-Content -LiteralPath (Join-Path $stageDir "RELEASE-NOTES.txt") -Encoding UTF8
}

Invoke-Step "8. Write CLUSTER-ACCEPTANCE.txt" {
    $checklist = @"
π1 Hyper-V Toolkit $Version - Failover Cluster acceptance checklist
======================================================================
Run this exact package on a real Failover Cluster node as administrator.
Confirm each item, then record PASS/FAIL per line. Confirm at the end that
no Hyper-V / Cluster / storage / network state was changed by the tool.

 1. Launch RC as administrator (accept the UAC prompt)
 2. Select Cluster scope
 3. Apply scope
 4. Dashboard (11 cards, node/VM/RAM/CSV/jobs overview)
 5. Cluster Nodes (states, drain, weights)
 6. Roles (owner node, group type, priority)
 7. Resources (states, owner groups, types, owners)
 8. Networks (roles, addresses, metrics)
 9. Quorum (type, resource)
10. Witness (sections, detail parameters)
11. Events + ProviderName column
12. VM Ownership (preferred/possible owners, anti-affinity)
13. Preferred Owners (failback settings per group)
14. VM Distribution (per-node VM/vCPU/RAM aggregates)
15. Placement Advisor (demand/assigned advice per node)
16. Failover Simulation (per-target demand math, move list)
17. Health Score (score + check details)
18. CSV (capacity, owners, paths)
19. Storage page (jobs, pools, disks, volumes, VM map)
20. Networking page (switches, adapters, VLAN)
21. Diagnostics page (Advisor, CSV Low Free, Resources Not Online)
22. Current Report export (from several different tabs)
23. Full VM Report export
24. CSV export opens with expected columns
25. HTML export renders standalone (no network)
26. JSON export parses (report/scope/rowCount/rows)
27. SK / EN language switch (labels, headers, scope text)
28. Logs written under %LocalAppData%\Pi1\HyperVToolkit\Logs
29. Confirm NO infrastructure state changed (VMs, cluster,
    storage, network, services, registry untouched)

Special attention (live-mapping verification):
- DrainStatus values
- NodeWeight values
- FaultDomain values
- Network Role / State values
- Quorum type/resource values
- Witness Detail parameters
- Preferred/Possible Owner associations per VM group
- Cluster-wide CSV data (owners, free space)

Result: ______________________  Date: __________  Tester: __________
"@
    $checklist | Set-Content -LiteralPath (Join-Path $stageDir "CLUSTER-ACCEPTANCE.txt") -Encoding UTF8
}

Invoke-Step "9. Write SHA256SUMS.txt (deterministic sorted paths)" {
    $entries = Get-ChildItem -LiteralPath $stageDir -File -Recurse |
        Sort-Object { $_.FullName.Substring($stageDir.Length) } |
        ForEach-Object {
            $relative = $_.FullName.Substring($stageDir.Length).TrimStart('\', '/') -replace '\\', '/'
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    $entries | Set-Content -LiteralPath (Join-Path $stageDir "SHA256SUMS.txt") -Encoding UTF8
}

Invoke-Step "10. Create ZIP (exactly one top-level folder)" {
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path $stageDir -DestinationPath $zipPath -CompressionLevel Optimal
}

$script:exeHash = ""
$script:zipHash = ""
$script:zipSize = 0
Invoke-Step "11. EXE and ZIP SHA256" {
    # NOTE: script: scope — Invoke-Step runs the body in a child scope.
    $script:exeHash = (Get-FileHash -LiteralPath (Join-Path $stageDir "Pi1.HyperVToolkit.exe") -Algorithm SHA256).Hash
    $script:zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $script:zipSize = (Get-Item -LiteralPath $zipPath).Length
    Write-Host "EXE : $script:exeHash"
    Write-Host "ZIP : $script:zipHash ($script:zipSize bytes)"
}

Invoke-Step "12. Extract ZIP to independent verification directory" {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyRoot -Force
}

Invoke-Step "13. Audit the EXTRACTED copy" {
    $extracted = Join-Path $verifyRoot $folderName
    if (-not (Test-Path -LiteralPath $extracted)) {
        throw "Verification failed: top-level folder '$folderName' missing after extract."
    }
    $topLevel = Get-ChildItem -LiteralPath $verifyRoot -Force
    if (@($topLevel).Count -ne 1 -or -not $topLevel[0].PSIsContainer) {
        throw "Verification failed: ZIP must contain exactly one top-level folder."
    }

    foreach ($required in @("Pi1.HyperVToolkit.exe", "RELEASE-NOTES.txt", "CLUSTER-ACCEPTANCE.txt", "SHA256SUMS.txt")) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $required))) {
            throw "Verification failed: required file '$required' missing in package."
        }
    }
    foreach ($marker in @("coreclr.dll", "Pi1.HyperVToolkit.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $marker))) {
            throw "Verification failed: runtime dependency '$marker' missing (self-contained check)."
        }
    }

    $leakPatterns = @(
        "*.Tests.dll", "xunit*", "testhost*",
        "*.psm1", "*.ps1",
        "*.cs", "*.xaml",
        "README.txt"
    )
    foreach ($pattern in $leakPatterns) {
        $hits = Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
        if ($hits) {
            throw ("Verification failed: forbidden file pattern '{0}' leaked: {1}" -f $pattern, (($hits | Select-Object -First 3 -ExpandProperty FullName) -join "; "))
        }
    }
    foreach ($dirName in @("obj", ".git")) {
        if (Get-ChildItem -LiteralPath $extracted -Recurse -Directory -Filter $dirName -ErrorAction SilentlyContinue) {
            throw "Verification failed: directory '$dirName' leaked into package."
        }
    }
    if (Get-ChildItem -LiteralPath $extracted -Recurse -Directory | Where-Object { $_.Name -eq "Debug" }) {
        throw "Verification failed: a 'Debug' directory leaked into package."
    }

    # Developer paths / secrets must not appear in the shipped text docs.
    $docHits = Select-String -LiteralPath (Join-Path $extracted "RELEASE-NOTES.txt"), (Join-Path $extracted "CLUSTER-ACCEPTANCE.txt"), (Join-Path $extracted "SHA256SUMS.txt") `
        -Pattern 'C:\\Users\\Pivan|API[_-]?KEY|secret|password' -SimpleMatch:$false -ErrorAction SilentlyContinue
    if ($docHits) {
        throw "Verification failed: developer path/secret text leaked into package docs."
    }

    # EXE metadata / version evidence (no execution, no elevation needed).
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $extracted "Pi1.HyperVToolkit.exe"))
    Write-Host ("ProductVersion : " + $versionInfo.ProductVersion)
    Write-Host ("FileVersion    : " + $versionInfo.FileVersion)
    $assemblyVersion = ([System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $extracted "Pi1.HyperVToolkit.dll"))).Version
    Write-Host ("AssemblyVersion: " + $assemblyVersion)
    if ($versionInfo.FileVersion -ne $FileVersion) {
        throw "Verification failed: FileVersion '$($versionInfo.FileVersion)' is not the RC-stamped '$FileVersion'."
    }
    if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion) -or -not $versionInfo.ProductVersion.Contains($Version)) {
        throw "Verification failed: ProductVersion '$($versionInfo.ProductVersion)' does not contain '$Version'."
    }

    # UAC manifest evidence at binary level (no execution, no elevation):
    # the embedded application manifest must requireAdministrator.
    $exeBytes = [System.IO.File]::ReadAllBytes((Join-Path $extracted "Pi1.HyperVToolkit.exe"))
    $exeText = [System.Text.Encoding]::UTF8.GetString($exeBytes)
    if ($exeText -notmatch 'level="requireAdministrator"') {
        throw "Verification failed: embedded manifest does not contain requireAdministrator."
    }
    Write-Host "UAC manifest : requireAdministrator (embedded)"
    Write-Host "Package audit: PASS"
}

Invoke-Step "14. Final artifact paths and hashes" {
    Write-Host "Package dir : $stageDir"
    Write-Host "ZIP path    : $zipPath ($script:zipSize bytes)"
    Write-Host "EXE SHA256  : $script:exeHash"
    Write-Host "ZIP SHA256  : $script:zipHash"
    Write-Host "Verify dir  : $(Join-Path $verifyRoot $folderName)"
}
