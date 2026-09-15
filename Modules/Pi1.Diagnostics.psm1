# π1 Hyper-V Toolkit - Diagnostics / Advisor

function Show-PiAdvisor {
    Show-Header "Advisor"
    $vms = @(Get-PiVMBaseRows | Where-Object {$_.State -eq "Running"})
    $rows=@()
    foreach($vm in $vms){
        $severity="OK"; $recommendation="OK"
        if($vm.WasteGB -lt 0){$severity="Warning"; $recommendation="Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie."}
        elseif($vm.WasteGB -gt 16){$severity="Info"; $recommendation="Veľká RAM rezerva. Kandidát na zníženie po sledovaní."}
        elseif($vm.WasteGB -gt 8){$severity="Info"; $recommendation="RAM rezerva > 8 GB. Sledovať."}
        if($vm.VM -match "SQL|VEEAM|EXCHANGE|FORTI|FAZ"){ if($severity -eq "Info"){$recommendation="Špecifický workload. Neznižovať bez dlhšieho sledovania."} }
        if($severity -ne "OK"){
            $rows += [PSCustomObject]@{Severity=$severity; HostNode=$vm.HostNode; VM=$vm.VM; AssignedGB=$vm.AssignedGB; DemandGB=$vm.DemandGB; WasteGB=$vm.WasteGB; Recommendation=$recommendation}
        }
    }
    Write-PiTable -Rows $rows -Columns @("Severity","HostNode","VM","AssignedGB","DemandGB","WasteGB","Recommendation") -NoResultsMessage "Advisor nenašiel žiadne RAM upozornenia v aktuálnom scope." -SaveAsLastResult -ResultName "Advisor"
    Pause-Pi
}

function Show-PiCsvLowFree {
    Show-Header "CSV nízke voľné miesto"
    $rows = @(Get-PiCSVRows | Where-Object {$_.FreePercent -lt 20} | Sort-Object FreePercent)
    Write-PiTable -Rows $rows -Columns @("Name","State","OwnerNode","SizeGB","FreeGB","UsedGB","FreePercent","Path") -NoResultsMessage "Žiadne CSV nemá menej ako 20 % voľného miesta." -SaveAsLastResult -ResultName "CSVLowFree"
    Pause-Pi
}

function Show-PiClusterResourcesNotOnline {
    Show-Header "Cluster Resources not Online"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Cluster Resources." -ScriptBlock {
        Get-ClusterResource -ErrorAction Stop | Where-Object {$_.State -ne "Online"} | Select-Object Name, State, OwnerGroup, ResourceType, OwnerNode | Sort-Object State, OwnerGroup, Name
    }
    Write-PiTable -Rows $rows -Columns @("Name","State","OwnerGroup","ResourceType","OwnerNode") -NoResultsMessage "Všetky cluster resources sú Online alebo neboli nájdené žiadne problematické resources." -SaveAsLastResult -ResultName "ResourcesNotOnline"
    Pause-Pi
}

function Show-PiDiagnosticsMenu {
    do {
        Show-Header "Diagnostics"
        Write-Host "1  - Advisor"
        Write-Host "2  - RAM Waste / Pressure"
        Write-Host "3  - VM bez IP"
        Write-Host "4  - Checkpoints"
        Write-Host "5  - CSV nízke voľné miesto"
        Write-Host "6  - Storage Jobs"
        Write-Host "7  - Cluster Resources not Online"
        Write-Host "8  - Placement Advisor"
        Write-Host "9  - Failover Simulation"
        Write-Host "10 - Cluster Health Score"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiAdvisor }
            "2" { Show-PiDynamicMemory }
            "3" { Show-PiVMWithoutIP }
            "4" { Show-PiCheckpoints }
            "5" { Show-PiCsvLowFree }
            "6" { Show-PiStorageJobs }
            "7" { Show-PiClusterResourcesNotOnline }
            "8" { Show-PiPlacementAdvisor }
            "9" { Show-PiFailoverSimulation }
            "10" { Show-PiClusterHealthScore }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
