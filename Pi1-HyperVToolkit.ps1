<#
.SYNOPSIS
    π1 Hyper-V Toolkit - modular read-only toolkit.

.DESCRIPTION
    Main launcher for π1 Hyper-V Toolkit v0.9.4.
    Spúšťaj tento súbor: .\Pi1-HyperVToolkit.ps1

.NOTES
    Verzia: 0.9.4
    Read-only verzia.
#>

$Script:PiRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Script:PiModulePath = Join-Path $Script:PiRoot "Modules"
$Script:PiLogDir = Join-Path $Script:PiRoot "Logs"
$Script:PiStartupLog = Join-Path $Script:PiLogDir ("startup_{0}.log" -f (Get-Date -Format "yyyyMMdd_HHmmss"))

if (-not (Test-Path $Script:PiLogDir)) {
    New-Item -ItemType Directory -Path $Script:PiLogDir -Force | Out-Null
}

function Write-PiStartupLog {
    param([string]$Message)

    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $Script:PiStartupLog -Value $line -Encoding UTF8
}

function Import-PiModuleSafe {
    param(
        [Parameter(Mandatory=$true)]
        [string]$ModuleFile
    )

    $fullPath = Join-Path $Script:PiModulePath $ModuleFile

    if (-not (Test-Path $fullPath)) {
        Write-Host "Chýba modul: $fullPath" -ForegroundColor Red
        Write-PiStartupLog "MISSING MODULE: $fullPath"
        return $false
    }

    try {
        Import-Module $fullPath -Force -ErrorAction Stop
        Write-PiStartupLog "OK import: $ModuleFile"
        return $true
    } catch {
        Write-Host ""
        Write-Host "Chyba pri načítaní modulu: $ModuleFile" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Log:" -ForegroundColor Cyan
        Write-Host $Script:PiStartupLog

        Write-PiStartupLog "ERROR import: $ModuleFile"
        Write-PiStartupLog $_.Exception.ToString()

        Read-Host "Stlač ENTER pre ukončenie"
        return $false
    }
}

try {
    Write-PiStartupLog "===== π1 Hyper-V Toolkit startup ====="
    Write-PiStartupLog "Root: $Script:PiRoot"
    Write-PiStartupLog "Modules: $Script:PiModulePath"
    Write-PiStartupLog "Host: $env:COMPUTERNAME"
    Write-PiStartupLog "User: $env:USERNAME"
    Write-PiStartupLog "PSVersion: $($PSVersionTable.PSVersion)"

    $moduleFiles = @(
        "Pi1.Core.psm1",
        "Pi1.Data.psm1",
        "Pi1.Dashboard.psm1",
        "Pi1.VM.psm1",
        "Pi1.Nodes.psm1",
        "Pi1.Cluster.psm1",
        "Pi1.Storage.psm1",
        "Pi1.Networking.psm1",
        "Pi1.Diagnostics.psm1",
        "Pi1.Export.psm1"
    )

    foreach ($moduleFile in $moduleFiles) {
        $ok = Import-PiModuleSafe -ModuleFile $moduleFile
        if (-not $ok) {
            exit 1
        }
    }

    try {
        Initialize-PiToolkit -Version "0.9.4"
        Write-PiStartupLog "OK Initialize-PiToolkit"
    } catch {
        Write-Host ""
        Write-Host "Chyba pri inicializácii toolkitu." -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        Write-PiStartupLog "ERROR Initialize-PiToolkit"
        Write-PiStartupLog $_.Exception.ToString()
        Read-Host "Stlač ENTER pre ukončenie"
        exit 1
    }

    try {
        $hvOk = Test-HyperVModule
        Write-PiStartupLog "Test-HyperVModule result: $hvOk"

        if ($hvOk) {
            Show-PiStartupScope
            Show-PiMainMenu
        } else {
            Write-PiStartupLog "Hyper-V module unavailable"
            Read-Host "Stlač ENTER pre ukončenie"
        }
    } catch {
        Write-Host ""
        Write-Host "Chyba počas štartu aplikácie." -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Log:" -ForegroundColor Cyan
        Write-Host $Script:PiStartupLog

        Write-PiStartupLog "ERROR runtime startup"
        Write-PiStartupLog $_.Exception.ToString()

        Read-Host "Stlač ENTER pre ukončenie"
        exit 1
    }
} catch {
    Write-Host ""
    Write-Host "Neočekávaná chyba pri štarte." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Log:" -ForegroundColor Cyan
    Write-Host $Script:PiStartupLog

    try {
        Write-PiStartupLog "FATAL startup"
        Write-PiStartupLog $_.Exception.ToString()
    } catch {}

    Read-Host "Stlač ENTER pre ukončenie"
    exit 1
}
