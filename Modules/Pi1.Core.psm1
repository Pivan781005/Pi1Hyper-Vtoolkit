# π1 Hyper-V Toolkit - Core / UI

$Script:PiVersion = "0.9.4"
$Script:ScopeMode = "Local"
$Script:SelectedNode = $env:COMPUTERNAME
$Script:LastResult = @()
$Script:LastResultName = ""
$Script:LastErrorText = ""
$Script:ShowLegend = $true
$ErrorActionPreference = "Continue"

function Initialize-PiToolkit {
    param([string]$Version = "0.9.4")
    $Script:PiVersion = $Version
}

function Pause-Pi {
    Write-Host ""
    Read-Host "Stlač ENTER pre návrat do menu"
}

function Write-PiInfo { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }
function Write-PiWarn { param([string]$Message) Write-Host $Message -ForegroundColor Yellow }
function Write-PiErrorInfo { param([string]$Message) Write-Host $Message -ForegroundColor Red }

function Get-PiScopeLabel {
    switch ($Script:ScopeMode) {
        "Local"   { return "Local node ($env:COMPUTERNAME)" }
        "Cluster" { return "All cluster nodes" }
        "Node"    { return "Selected node ($Script:SelectedNode)" }
        default   { return "Local node ($env:COMPUTERNAME)" }
    }
}

function Show-Header {
    param([string]$Title = "")

    Clear-Host
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " π1 Hyper-V Toolkit" -ForegroundColor Cyan
    Write-Host " Verzia $Script:PiVersion" -ForegroundColor DarkCyan
    Write-Host " Host: $env:COMPUTERNAME" -ForegroundColor DarkGray
    Write-Host " Scope: $(Get-PiScopeLabel)" -ForegroundColor DarkGray
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""

    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        Write-Host $Title -ForegroundColor Yellow
        Write-Host ""
    }
}

function Show-PiConsoleWidthHint {
    param([int]$RecommendedWidth = 160)
    try {
        $width = $Host.UI.RawUI.WindowSize.Width
        if ($width -lt $RecommendedWidth) {
            Write-Host ""
            Write-PiWarn "Odporúčanie: pre tento výpis je vhodné maximalizované PowerShell okno."
            Write-Host ("Aktuálna šírka konzoly: {0} znakov, odporúčané: {1}+ znakov." -f $width, $RecommendedWidth) -ForegroundColor DarkGray
        }
    } catch {}
}

function Show-NoResults {
    param(
        [string]$Title = "Neboli nájdené žiadne výsledky.",
        [string[]]$Hints = @()
    )

    Write-Host ""
    Write-PiWarn $Title

    if ($Hints -and $Hints.Count -gt 0) {
        Write-Host ""
        Write-PiInfo "Skontroluj:"
        foreach ($hint in $Hints) {
            Write-Host "  - $hint" -ForegroundColor Gray
        }
    }
}

function Invoke-PiSafe {
    param(
        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock,
        [string]$ErrorMessage = "Operácia zlyhala."
    )

    try {
        $result = & $ScriptBlock
        return @($result)
    } catch {
        $Script:LastErrorText = $_.Exception.Message
        Write-PiWarn $ErrorMessage
        Write-Host "Detail: $($Script:LastErrorText)" -ForegroundColor DarkGray
        return @()
    }
}

function Set-PiLastResult {
    param([object[]]$Rows, [string]$Name)
    if ($null -eq $Rows) { $Script:LastResult = @() } else { $Script:LastResult = @($Rows) }
    $Script:LastResultName = $Name
}

function Write-PiTable {
    param(
        [AllowNull()][object[]]$Rows,
        [string[]]$Columns = @(),
        [string]$NoResultsMessage = "Neboli nájdené žiadne výsledky.",
        [string[]]$Hints = @(),
        [switch]$SaveAsLastResult,
        [string]$ResultName = "LastResult"
    )

    $safeRows = @()
    if ($null -ne $Rows) { $safeRows = @($Rows) }

    if ($SaveAsLastResult) {
        Set-PiLastResult -Rows $safeRows -Name $ResultName
    }

    if ($safeRows.Count -eq 0) {
        Show-NoResults -Title $NoResultsMessage -Hints $Hints
        return
    }

    if (-not $Columns -or $Columns.Count -eq 0) {
        $Columns = $safeRows[0].PSObject.Properties.Name
    }

    $widths = @{}
    foreach ($col in $Columns) {
        $max = $col.Length
        foreach ($row in $safeRows) {
            $text = [string]$row.$col
            if ($text.Length -gt $max) { $max = $text.Length }
        }
        $widths[$col] = [Math]::Min([Math]::Max($max, 4), 40)
    }

    foreach ($col in $Columns) {
        Write-Host (($col.PadRight($widths[$col] + 2))) -NoNewline -ForegroundColor White
    }
    Write-Host ""

    foreach ($col in $Columns) {
        Write-Host (("-" * $widths[$col]).PadRight($widths[$col] + 2)) -NoNewline -ForegroundColor DarkGray
    }
    Write-Host ""

    foreach ($row in $safeRows) {
        foreach ($col in $Columns) {
            $value = [string]$row.$col
            if ($value.Length -gt $widths[$col]) {
                $value = $value.Substring(0, $widths[$col] - 1) + "…"
            }

            $color = "Gray"

            if ($col -match "State|Status|Health|ClusterHealth|Witness") {
                if ($value -match "Running|Online|Up|Healthy|OK|No Issues|None|True") { $color = "Green" }
                elseif ($value -match "Off|Offline|Down|Unknown|False") { $color = "DarkGray" }
                elseif ($value -match "Warning|Degraded|Repair|Rebalance|Info") { $color = "Yellow" }
                elseif ($value -match "Failed|Unhealthy|Error|Critical") { $color = "Red" }
                else { $color = "Gray" }
            }
            elseif ($col -eq "WasteGB") {
                $num = 0.0
                if ([double]::TryParse(($value -replace ",","."), [ref]$num)) {
                    if ($num -lt 0) { $color = "Red" }
                    elseif ($num -gt 8) { $color = "Yellow" }
                    else { $color = "Green" }
                }
            }
            elseif ($col -match "FreePercent|RAMUsedPct|AssignedPct|DemandPct") {
                $num = 0.0
                if ([double]::TryParse(($value -replace ",","."), [ref]$num)) {
                    if ($num -gt 85 -and $col -match "RAMUsedPct|AssignedPct|DemandPct") { $color = "Red" }
                    elseif ($num -gt 70 -and $col -match "RAMUsedPct|AssignedPct|DemandPct") { $color = "Yellow" }
                    elseif ($num -lt 10 -and $col -eq "FreePercent") { $color = "Red" }
                    elseif ($num -lt 20 -and $col -eq "FreePercent") { $color = "Yellow" }
                    else { $color = "Green" }
                }
            }
            elseif ($col -match "DemandGB|Percent|Progress|FreeRAMGB") { $color = "Cyan" }
            elseif ($col -match "HostNode|Node|VM|VMName|Name|OwnerNode|CSV") { $color = "White" }

            Write-Host ($value.PadRight($widths[$col] + 2)) -NoNewline -ForegroundColor $color
        }
        Write-Host ""
    }
}

function Format-PiTimeSpan {
    param([AllowNull()][TimeSpan]$Time)
    if ($null -eq $Time) { return "-" }
    if ($Time.TotalSeconds -lt 1) { return "0m" }
    if ($Time.TotalDays -ge 1) { return "{0}d {1}h {2}m" -f [int]$Time.Days, [int]$Time.Hours, [int]$Time.Minutes }
    if ($Time.TotalHours -ge 1) { return "{0}h {1}m" -f [int]$Time.Hours, [int]$Time.Minutes }
    return "{0}m" -f [int]$Time.Minutes
}

function Get-PiIPv4 {
    param([string[]]$IPAddresses)
    if (-not $IPAddresses) { return "" }
    return ($IPAddresses | Where-Object { $_ -match '^\d+\.' }) -join ', '
}

function Write-PiLegendRam {
    if (-not $Script:ShowLegend) { return }
    Write-Host ""
    Write-PiInfo "Legenda RAM:"
    Write-Host "  WasteGB > 8 GB  = veľká rezerva / možný kandidát na zníženie RAM" -ForegroundColor Yellow
    Write-Host "  WasteGB < 0 GB  = VM potrebuje viac RAM alebo má vysoký tlak na pamäť" -ForegroundColor Red
    Write-Host "  DemandGB        = aktuálna požiadavka VM podľa Hyper-V" -ForegroundColor Gray
}

function Test-HyperVModule {
    if (-not (Get-Module -ListAvailable -Name Hyper-V)) {
        Write-PiErrorInfo "Hyper-V PowerShell modul nebol nájdený."
        Write-PiWarn "Skript spusti na Hyper-V hostovi alebo na serveri s RSAT/Hyper-V nástrojmi."
        Pause-Pi
        return $false
    }
    Import-Module Hyper-V -ErrorAction SilentlyContinue
    return $true
}

function Test-PiClusterModule {
    if (-not (Get-Module -ListAvailable -Name FailoverClusters)) {
        Write-PiWarn "FailoverClusters modul nebol nájdený."
        Write-PiWarn "Cluster funkcie budú dostupné iba na cluster node alebo serveri s RSAT Failover Clustering Tools."
        return $false
    }
    Import-Module FailoverClusters -ErrorAction SilentlyContinue
    return $true
}

function Get-PiTargetNodes {
    switch ($Script:ScopeMode) {
        "Local" { return @($env:COMPUTERNAME) }
        "Node"  { return @($Script:SelectedNode) }
        "Cluster" {
            if (Test-PiClusterModule) {
                try { return @(Get-ClusterNode -ErrorAction Stop | Select-Object -ExpandProperty Name) }
                catch {
                    $Script:LastErrorText = $_.Exception.Message
                    Write-PiWarn "Nepodarilo sa načítať cluster nody. Použijem lokálny node."
                    return @($env:COMPUTERNAME)
                }
            } else {
                return @($env:COMPUTERNAME)
            }
        }
        default { return @($env:COMPUTERNAME) }
    }
}

function Test-PiNodeExists {
    param([string]$NodeName)
    if ([string]::IsNullOrWhiteSpace($NodeName)) { return $false }
    if (Test-PiClusterModule) {
        try {
            $nodes = @(Get-ClusterNode -ErrorAction Stop | Select-Object -ExpandProperty Name)
            return ($nodes -contains $NodeName)
        } catch { return ($NodeName -ieq $env:COMPUTERNAME) }
    }
    return ($NodeName -ieq $env:COMPUTERNAME)
}

function Select-PiClusterNode {
    param([string]$Title = "Vyber konkrétny node")

    if (-not (Test-PiClusterModule)) {
        Write-PiWarn "Cluster modul nie je dostupný."
        Write-PiWarn "Môžeš použiť len lokálny node: $env:COMPUTERNAME"
        return $env:COMPUTERNAME
    }

    $nodes = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster nody." -ScriptBlock {
        Get-ClusterNode -ErrorAction Stop | Sort-Object Name
    }

    if ($null -eq $nodes -or @($nodes).Count -eq 0) {
        Write-PiWarn "Neboli nájdené cluster nody."
        return $null
    }

    Write-Host ""
    Write-Host $Title -ForegroundColor Yellow
    Write-Host ""

    $index = 1
    foreach ($node in @($nodes)) {
        $state = [string]$node.State
        $color = if ($state -eq "Up") { "Green" } elseif ($state -eq "Down") { "Red" } else { "Yellow" }
        Write-Host ("{0}  - {1} [{2}]" -f $index, $node.Name, $state) -ForegroundColor $color
        $index++
    }

    Write-Host ""
    Write-Host "0  - Späť"
    Write-Host ""

    do {
        $choice = Read-Host "Vyber node číslom"
        if ($choice -eq "0") { return $null }
        $number = 0
        if ([int]::TryParse($choice, [ref]$number)) {
            if ($number -ge 1 -and $number -le @($nodes).Count) {
                return @($nodes)[$number - 1].Name
            }
        }
        Write-Host "Neplatná voľba. Vyber číslo zo zoznamu." -ForegroundColor Red
    } while ($true)
}

function Show-PiStartupScope {
    do {
        Show-Header "Prvé nastavenie scope"
        Write-Host "Vyber, odkiaľ má nástroj čítať Hyper-V údaje." -ForegroundColor Cyan
        Write-Host ""
        Write-Host "1  - Local node ($env:COMPUTERNAME)"
        Write-Host "2  - All cluster nodes"
        Write-Host "3  - Vybrať konkrétny node"
        Write-Host ""
        Write-Host "Ak hľadáš VM/IP a nevieš, na ktorom node beží, použi All cluster nodes." -ForegroundColor DarkGray
        Write-Host ""

        $choice = Read-Host "Vyber scope"
        switch ($choice.ToUpper()) {
            "1" { $Script:ScopeMode = "Local"; $Script:SelectedNode = $env:COMPUTERNAME; return }
            "2" {
                if (Test-PiClusterModule) { $Script:ScopeMode = "Cluster" }
                else { Write-PiWarn "Cluster modul nie je dostupný. Scope ostáva Local."; $Script:ScopeMode = "Local"; $Script:SelectedNode = $env:COMPUTERNAME; Start-Sleep -Seconds 2 }
                return
            }
            "3" {
                $node = Select-PiClusterNode -Title "Vyber node pre scope"
                if ([string]::IsNullOrWhiteSpace($node)) {
                    Write-PiWarn "Node nebol vybraný. Vraciaš sa späť na výber scope."
                    Start-Sleep -Seconds 1
                    continue
                } else {
                    $Script:ScopeMode = "Node"
                    $Script:SelectedNode = $node
                    return
                }
            }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

function Show-PiScopeMenu {
    do {
        Show-Header "Scope"
        Write-Host "Aktuálny scope: $(Get-PiScopeLabel)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "1  - Local node ($env:COMPUTERNAME)"
        Write-Host "2  - All cluster nodes"
        Write-Host "3  - Vybrať konkrétny node"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { $Script:ScopeMode = "Local"; $Script:SelectedNode = $env:COMPUTERNAME; return }
            "2" {
                if (Test-PiClusterModule) { $Script:ScopeMode = "Cluster"; return }
                Write-PiWarn "Cluster modul nie je dostupný. Scope ostáva Local."
                $Script:ScopeMode = "Local"; Start-Sleep -Seconds 1; return
            }
            "3" {
                $node = Select-PiClusterNode -Title "Vyber node pre scope"
                if ([string]::IsNullOrWhiteSpace($node)) { Write-PiWarn "Node nebol vybraný. Vraciaš sa späť."; Start-Sleep -Seconds 1; continue }
                $Script:ScopeMode = "Node"; $Script:SelectedNode = $node; return
            }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

function Show-PiMainMenu {
    do {
        Show-Header
        Write-Host "1  - Scope"
        Write-Host "2  - Dashboard"
        Write-Host "3  - Virtual Machines"
        Write-Host "4  - Nodes"
        Write-Host "5  - Cluster"
        Write-Host "6  - Storage"
        Write-Host "7  - Networking"
        Write-Host "8  - Diagnostics / Advisor"
        Write-Host "9  - Export"
        Write-Host "10 - Settings"
        Write-Host ""
        Write-Host "H  - Help"
        Write-Host "C  - Changelog"
        Write-Host "0  - Koniec"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"

        switch ($choice.ToUpper()) {
            "1" { Show-PiScopeMenu }
            "2" { Show-PiDashboardMenu }
            "3" { Show-PiVMMenu }
            "4" { Show-PiNodeMenu }
            "5" { Show-PiClusterMenu }
            "6" { Show-PiStorageMenu }
            "7" { Show-PiNetworkingMenu }
            "8" { Show-PiDiagnosticsMenu }
            "9" { Show-PiExportMenu }
            "10" { Show-PiSettings }
            "H" { Show-PiHelp }
            "C" { Show-PiChangelog }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
