# π1 Hyper-V Toolkit - Virtual Machines

function Select-PiVM {
    param([string]$Title = "Vyber VM")
    $vms = @(Get-PiVMBaseRows | Sort-Object HostNode, VM)
    if ($vms.Count -eq 0) { Show-NoResults -Title "Neboli nájdené žiadne VM v aktuálnom scope."; return $null }

    do {
        Show-Header $Title
        Write-Host "Vyber VM zo zoznamu." -ForegroundColor Cyan
        Write-Host ""
        $index = 1
        foreach ($vm in $vms) {
            $stateColor = if ($vm.State -eq "Running") { "Green" } elseif ($vm.State -eq "Off") { "DarkGray" } else { "Yellow" }
            $line = "{0,3} - {1,-8} {2,-24} [{3}] {4}" -f $index, $vm.HostNode, $vm.VM, $vm.State, $vm.IPv4
            Write-Host $line -ForegroundColor $stateColor
            $index++
        }
        Write-Host ""
        Write-Host "F  - filtrovať podľa názvu"
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber VM"
        if ($choice -eq "0") { return $null }
        if ($choice.ToUpper() -eq "F") {
            $filter = Read-Host "Zadaj časť názvu VM"
            if (-not [string]::IsNullOrWhiteSpace($filter)) {
                $filtered = @(Get-PiVMBaseRows | Where-Object { $_.VM -like "*$filter*" } | Sort-Object HostNode, VM)
                if ($filtered.Count -gt 0) { $vms = $filtered } else { Write-PiWarn "Žiadna VM nevyhovuje filtru '$filter'."; Start-Sleep -Seconds 1 }
            }
            continue
        }
        $number = 0
        if ([int]::TryParse($choice, [ref]$number)) {
            if ($number -ge 1 -and $number -le $vms.Count) { return $vms[$number - 1] }
        }
        Write-Host "Neplatná voľba." -ForegroundColor Red
        Start-Sleep -Seconds 1
    } while ($true)
}

function Show-PiVMList {
    param([string]$Title, [ValidateSet("All","Running","Off")][string]$Filter="All")
    Show-Header $Title
    $rows = Get-PiVMBaseRows
    if ($Filter -ne "All") { $rows = @($rows | Where-Object State -eq $Filter) }
    $rows = @($rows | Sort-Object HostNode, VM)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","CPU","AssignedGB","DemandGB","WasteGB","IPv4","MAC","Switch","Uptime") -SaveAsLastResult -ResultName $Title
    Write-PiLegendRam
    Pause-Pi
}

function Show-PiAllVM { Show-PiVMList -Title "VM - všetky" -Filter "All" }
function Show-PiRunningVM { Show-PiVMList -Title "VM - zapnuté" -Filter "Running" }
function Show-PiOffVM { Show-PiVMList -Title "VM - vypnuté" -Filter "Off" }

function Show-PiVMSummary {
    Show-Header "VM Summary"
    $vms = Get-PiVMBaseRows
    $running = @($vms | Where-Object State -eq "Running")
    $off = @($vms | Where-Object State -eq "Off")
    $rows = @(
        [PSCustomObject]@{ Metric="Total VM"; Value=@($vms).Count }
        [PSCustomObject]@{ Metric="Running"; Value=$running.Count }
        [PSCustomObject]@{ Metric="Off"; Value=$off.Count }
        [PSCustomObject]@{ Metric="AssignedGB Running"; Value=[math]::Round(($running | Measure-Object AssignedGB -Sum).Sum,1) }
        [PSCustomObject]@{ Metric="DemandGB Running"; Value=[math]::Round(($running | Measure-Object DemandGB -Sum).Sum,1) }
        [PSCustomObject]@{ Metric="WasteGB Running"; Value=[math]::Round(($running | Measure-Object WasteGB -Sum).Sum,1) }
    )
    Write-PiTable -Rows $rows -Columns @("Metric","Value") -SaveAsLastResult -ResultName "VMSummary"
    Pause-Pi
}

function Find-PiVMByName {
    Show-Header "Vyhľadanie VM podľa názvu"
    $name = Read-Host "Zadaj názov alebo časť názvu VM"
    if ([string]::IsNullOrWhiteSpace($name)) { Show-NoResults -Title "Nebola zadaná hodnota."; Pause-Pi; return }
    $rows = @(Get-PiVMBaseRows | Where-Object { $_.VM -like "*$name*" } | Sort-Object HostNode, VM)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","CPU","AssignedGB","DemandGB","WasteGB","IPv4","MAC","Switch","Uptime") -NoResultsMessage "Nebola nájdená žiadna VM podľa názvu '$name'." -SaveAsLastResult -ResultName "SearchByName"
    Write-PiLegendRam
    Pause-Pi
}

function Find-PiVMByIP {
    Show-Header "Vyhľadanie VM podľa IP adresy"
    $ip = Read-Host "Zadaj IP adresu alebo jej časť"
    if ([string]::IsNullOrWhiteSpace($ip)) { Show-NoResults -Title "Nebola zadaná IP adresa."; Pause-Pi; return }
    $rows = @(Get-PiVMNetworkRows | Where-Object { ($_.AllIPs -like "*$ip*") -or ($_.IPv4 -like "*$ip*") } | Sort-Object HostNode, VMName)
    Write-PiTable -Rows $rows -Columns @("HostNode","VMName","IPv4","MacAddress","SwitchName","AllIPs") -NoResultsMessage "Nebola nájdená žiadna VM s IP alebo časťou IP '$ip'." -SaveAsLastResult -ResultName "SearchByIP"
    Pause-Pi
}

function Find-PiVMByMac {
    Show-Header "Vyhľadanie VM podľa MAC adresy"
    $mac = Read-Host "Zadaj MAC adresu alebo jej časť"
    if ([string]::IsNullOrWhiteSpace($mac)) { Show-NoResults -Title "Nebola zadaná MAC adresa."; Pause-Pi; return }
    $normalized = $mac -replace "[-:\. ]", ""
    $rows = @(Get-PiVMNetworkRows | Where-Object { $_.MacAddress -like "*$normalized*" } | Sort-Object HostNode, VMName)
    Write-PiTable -Rows $rows -Columns @("HostNode","VMName","IPv4","MacAddress","SwitchName","AllIPs") -NoResultsMessage "Nebola nájdená žiadna VM s MAC adresou '$normalized'." -SaveAsLastResult -ResultName "SearchByMAC"
    Pause-Pi
}

function Show-PiVMDetail {
    $selected = Select-PiVM -Title "Detail VM - výber"
    if ($null -eq $selected) { return }
    Show-Header ("Detail VM - {0}" -f $selected.VM)
    $rows = @(Get-PiVMBaseRows | Where-Object { $_.HostNode -eq $selected.HostNode -and $_.VM -eq $selected.VM } | Sort-Object HostNode, VM)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","CPU","AssignedGB","DemandGB","WasteGB","Dynamic","StartupGB","MinimumGB","MaximumGB","IPv4","MAC","Switch","Uptime") -SaveAsLastResult -ResultName "VMDetail"
    Pause-Pi
}

function Show-PiVMNetwork {
    Show-Header "VM Network Adapters"
    $rows = @(Get-PiVMNetworkRows | Sort-Object HostNode, VMName)
    Write-PiTable -Rows $rows -Columns @("HostNode","VMName","IPv4","MacAddress","SwitchName","AllIPs") -SaveAsLastResult -ResultName "VMNetwork"
    Pause-Pi
}

function Show-PiVMWithoutIP {
    Show-Header "VM bez IP adresy z pohľadu Hyper-V"
    $rows = @(Get-PiVMNetworkRows | Where-Object {[string]::IsNullOrWhiteSpace($_.AllIPs)} | Sort-Object HostNode, VMName)
    Write-PiTable -Rows $rows -Columns @("HostNode","VMName","MacAddress","SwitchName") -NoResultsMessage "V aktuálnom scope neboli nájdené VM bez IP adresy." -SaveAsLastResult -ResultName "VMWithoutIP"
    Pause-Pi
}

function Show-PiDynamicMemory {
    Show-Header "Dynamic Memory"
    $rows = @(Get-PiVMBaseRows | Sort-Object HostNode, VM)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","Dynamic","StartupGB","MinimumGB","MaximumGB","AssignedGB","DemandGB","WasteGB") -SaveAsLastResult -ResultName "DynamicMemory"
    Write-PiLegendRam
    Pause-Pi
}

function Show-PiCheckpoints {
    Show-Header "Checkpoints"
    $rows = @()
    foreach ($node in (Get-PiTargetNodes)) {
        $cps = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať checkpointy z node $node." -ScriptBlock {
            Get-VM -ComputerName $node | Get-VMSnapshot -ErrorAction SilentlyContinue
        }
        foreach ($cp in @($cps)) {
            $rows += [PSCustomObject]@{ HostNode=$node; VM=$cp.VMName; Name=$cp.Name; Created=$cp.CreationTime; Type=$cp.SnapshotType }
        }
    }
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","Name","Created","Type") -NoResultsMessage "Neboli nájdené žiadne VM checkpointy." -SaveAsLastResult -ResultName "Checkpoints"
    Pause-Pi
}

function Show-PiVMMenu {
    do {
        Show-Header "Virtual Machines"
        Write-Host "1  - Všetky VM"
        Write-Host "2  - Zapnuté VM"
        Write-Host "3  - Vypnuté VM"
        Write-Host ""
        Write-Host "4  - Vyhľadať podľa názvu"
        Write-Host "5  - Vyhľadať podľa IP"
        Write-Host "6  - Vyhľadať podľa MAC"
        Write-Host ""
        Write-Host "7  - Detail VM"
        Write-Host "8  - VM Network Adapters"
        Write-Host "9  - Dynamic Memory"
        Write-Host "10 - Checkpoints"
        Write-Host "11 - VM bez IP"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiAllVM }
            "2" { Show-PiRunningVM }
            "3" { Show-PiOffVM }
            "4" { Find-PiVMByName }
            "5" { Find-PiVMByIP }
            "6" { Find-PiVMByMac }
            "7" { Show-PiVMDetail }
            "8" { Show-PiVMNetwork }
            "9" { Show-PiDynamicMemory }
            "10" { Show-PiCheckpoints }
            "11" { Show-PiVMWithoutIP }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
