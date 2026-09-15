# π1 Hyper-V Toolkit - Dashboard

function Show-PiDashboard {
    Show-Header "Dashboard"

    $nodes = @()
    if (Test-PiClusterModule) {
        $nodes = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster nody." -ScriptBlock { Get-ClusterNode -ErrorAction Stop }
    }

    $clusterName = ""
    if (Test-PiClusterModule) {
        $cluster = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster." -ScriptBlock { Get-Cluster -ErrorAction Stop } | Select-Object -First 1
        if ($cluster) { $clusterName = $cluster.Name }
    }

    $vms = Get-PiVMBaseRows
    $hw = Get-PiNodeHardwareRows
    $csvs = Get-PiCSVRows
    $jobs = Get-PiStorageJobRows

    $runningVm = @($vms | Where-Object {$_.State -eq "Running"}).Count
    $offVm = @($vms | Where-Object {$_.State -eq "Off"}).Count
    $onlineNodes = @($nodes | Where-Object {$_.State -eq "Up"}).Count
    $nodeCount = @($nodes).Count

    $totalRAM = [math]::Round(($hw | Measure-Object RAMGB -Sum).Sum,1)
    $freeRAM = [math]::Round(($hw | Measure-Object FreeRAMGB -Sum).Sum,1)
    $usedRAM = [math]::Round(($hw | Measure-Object UsedRAMGB -Sum).Sum,1)
    $usedPct = if($totalRAM -gt 0){[math]::Round(($usedRAM/$totalRAM)*100,1)}else{0}

    $lowCsv = @($csvs | Where-Object {$_.FreePercent -lt 15})
    $negativeRam = @($vms | Where-Object {$_.State -eq "Running" -and $_.WasteGB -lt 0})
    $highWaste = @($vms | Where-Object {$_.State -eq "Running" -and $_.WasteGB -gt 8})

    $warnings = 0
    if ($lowCsv.Count -gt 0) { $warnings++ }
    if ($negativeRam.Count -gt 0) { $warnings++ }
    if ($highWaste.Count -gt 0) { $warnings++ }
    if (@($jobs).Count -gt 0) { $warnings++ }

    $rows = @(
        [PSCustomObject]@{ Area="Cluster"; Status=if($clusterName){"OK"}else{"N/A"}; Value=$clusterName }
        [PSCustomObject]@{ Area="Scope"; Status="Info"; Value=(Get-PiScopeLabel) }
        [PSCustomObject]@{ Area="Nodes"; Status=if($nodeCount -gt 0 -and $onlineNodes -eq $nodeCount){"OK"}elseif($nodeCount -gt 0){"Warning"}else{"N/A"}; Value=("{0}/{1} Online" -f $onlineNodes,$nodeCount) }
        [PSCustomObject]@{ Area="VM Running"; Status="OK"; Value=$runningVm }
        [PSCustomObject]@{ Area="VM Off"; Status="Info"; Value=$offVm }
        [PSCustomObject]@{ Area="RAM Total"; Status="Info"; Value=("$totalRAM GB") }
        [PSCustomObject]@{ Area="RAM Used"; Status=if($usedPct -gt 85){"Warning"}else{"OK"}; Value=("$usedRAM GB ($usedPct %)") }
        [PSCustomObject]@{ Area="RAM Free"; Status="OK"; Value=("$freeRAM GB") }
        [PSCustomObject]@{ Area="CSV"; Status=if($lowCsv.Count -gt 0){"Warning"}else{"OK"}; Value=@($csvs).Count }
        [PSCustomObject]@{ Area="Storage Jobs"; Status=if(@($jobs).Count -gt 0){"Warning"}else{"None"}; Value=@($jobs).Count }
        [PSCustomObject]@{ Area="Warnings"; Status=if($warnings -gt 0){"Warning"}else{"OK"}; Value=$warnings }
    )

    Write-PiTable -Rows $rows -Columns @("Area","Status","Value") -SaveAsLastResult -ResultName "Dashboard"

    Pause-Pi
}

function Show-PiNodeSummary {
    Show-Header "Node Summary"
    $vms = Get-PiVMBaseRows
    $hw = Get-PiNodeHardwareRows
    $rows = @()
    foreach ($group in ($vms | Group-Object HostNode)) {
        $items = @($group.Group)
        $running = @($items | Where-Object {$_.State -eq "Running"})
        $nodeHw = $hw | Where-Object Node -eq $group.Name | Select-Object -First 1
        $rows += [PSCustomObject]@{
            Node=$group.Name
            RunningVM=$running.Count
            OffVM=@($items|Where-Object {$_.State -eq "Off"}).Count
            vCPU=($running|Measure-Object CPU -Sum).Sum
            LogicalCPU=if($nodeHw){$nodeHw.LogicalCPU}else{""}
            RAMGB=if($nodeHw){$nodeHw.RAMGB}else{""}
            FreeRAMGB=if($nodeHw){$nodeHw.FreeRAMGB}else{""}
            RAMUsedPct=if($nodeHw){$nodeHw.RAMUsedPct}else{""}
            AssignedGB=[math]::Round(($running|Measure-Object AssignedGB -Sum).Sum,1)
            DemandGB=[math]::Round(($running|Measure-Object DemandGB -Sum).Sum,1)
            WasteGB=[math]::Round(($running|Measure-Object WasteGB -Sum).Sum,1)
        }
    }
    Write-PiTable -Rows $rows -Columns @("Node","RunningVM","OffVM","vCPU","LogicalCPU","RAMGB","FreeRAMGB","RAMUsedPct","AssignedGB","DemandGB","WasteGB") -SaveAsLastResult -ResultName "NodeSummary"
    Pause-Pi
}

function Show-PiDashboardMenu {
    do {
        Show-Header "Dashboard"
        Write-Host "1  - Cluster Health / Dashboard"
        Write-Host "2  - Node Summary"
        Write-Host "3  - VM Summary"
        Write-Host "4  - Storage Summary"
        Write-Host "5  - Active Storage Jobs"
        Write-Host "6  - CSV Capacity"
        Write-Host "7  - Advisor"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiDashboard }
            "2" { Show-PiNodeSummary }
            "3" { Show-PiVMSummary }
            "4" { Show-PiStorageSummary }
            "5" { Show-PiStorageJobs }
            "6" { Show-PiCSVOverview }
            "7" { Show-PiAdvisor }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
