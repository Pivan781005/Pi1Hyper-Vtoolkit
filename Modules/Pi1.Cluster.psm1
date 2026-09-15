# π1 Hyper-V Toolkit - Cluster

function Show-PiClusterNodes {
    Show-Header "Cluster Nodes"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster nody." -ScriptBlock {
        Get-ClusterNode -ErrorAction Stop | Select-Object Name, State, @{Name='DrainStatus';Expression={$_.DrainStatus}}, @{Name='NodeWeight';Expression={$_.NodeWeight}}, @{Name='FaultDomain';Expression={$_.FaultDomain}} | Sort-Object Name
    }
    Write-PiTable -Rows $rows -Columns @("Name","State","DrainStatus","NodeWeight","FaultDomain") -SaveAsLastResult -ResultName "ClusterNodes"
    Pause-Pi
}

function Show-PiClusterRoles {
    Show-Header "Cluster Roles / Groups"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Cluster Groups." -ScriptBlock {
        Get-ClusterGroup -ErrorAction Stop | Select-Object Name, State, OwnerNode, @{Name='GroupType';Expression={$_.GroupType}}, @{Name='Priority';Expression={$_.Priority}} | Sort-Object OwnerNode, Name
    }
    Write-PiTable -Rows $rows -Columns @("Name","State","OwnerNode","GroupType","Priority") -SaveAsLastResult -ResultName "ClusterRoles"
    Pause-Pi
}

function Show-PiClusterResources {
    Show-Header "Cluster Resources"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Cluster Resources." -ScriptBlock {
        Get-ClusterResource -ErrorAction Stop | Select-Object Name, State, OwnerGroup, ResourceType, OwnerNode | Sort-Object State, OwnerGroup, Name
    }
    Write-PiTable -Rows $rows -Columns @("Name","State","OwnerGroup","ResourceType","OwnerNode") -SaveAsLastResult -ResultName "ClusterResources"
    Pause-Pi
}

function Show-PiClusterNetworks {
    Show-Header "Cluster Networks"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Cluster Networks." -ScriptBlock {
        Get-ClusterNetwork -ErrorAction Stop | Select-Object Name, State, Role, Address, AddressMask, Metric, AutoMetric | Sort-Object Name
    }
    Write-PiTable -Rows $rows -Columns @("Name","State","Role","Address","AddressMask","Metric","AutoMetric") -SaveAsLastResult -ResultName "ClusterNetworks"
    Pause-Pi
}

function Show-PiQuorum {
    Show-Header "Cluster Quorum"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať quorum." -ScriptBlock {
        Get-ClusterQuorum -ErrorAction Stop | Select-Object QuorumType, QuorumResource
    }
    Write-PiTable -Rows $rows -Columns @("QuorumType","QuorumResource") -SaveAsLastResult -ResultName "Quorum"
    Pause-Pi
}

function Show-PiWitness {
    Show-Header "Cluster Witness / Quorum"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = @()
    $quorum = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať quorum." -ScriptBlock { Get-ClusterQuorum -ErrorAction Stop }
    foreach($q in @($quorum)){
        $rows += [PSCustomObject]@{Section="Quorum"; Name="Quorum"; Type=$q.QuorumType; State=""; OwnerGroup=""; OwnerNode=""; Detail=[string]$q.QuorumResource}
    }
    $resources = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať witness resources." -ScriptBlock {
        Get-ClusterResource -ErrorAction Stop | Where-Object { $_.ResourceType -match "Witness" -or $_.Name -match "Witness|Quorum" }
    }
    foreach($res in @($resources)){
        $detailParts=@()
        $params=Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať parametre witness resource $($res.Name)." -ScriptBlock { Get-ClusterParameter -InputObject $res -ErrorAction Stop }
        foreach($p in @($params)){
            if($p.Name -match "Share|Path|Account|Endpoint|Witness|Disk|Cloud|Storage|File"){ $detailParts += ("{0}={1}" -f $p.Name,$p.Value) }
        }
        $rows += [PSCustomObject]@{Section="Resource"; Name=$res.Name; Type=$res.ResourceType; State=$res.State; OwnerGroup=$res.OwnerGroup; OwnerNode=$res.OwnerNode; Detail=($detailParts -join "; ")}
    }
    Write-PiTable -Rows $rows -Columns @("Section","Name","Type","State","OwnerGroup","OwnerNode","Detail") -SaveAsLastResult -ResultName "Witness"
    Pause-Pi
}

function Show-PiClusterEvents {
    Show-Header "Cluster Events"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster eventy." -ScriptBlock {
        Get-WinEvent -LogName "Microsoft-Windows-FailoverClustering/Operational" -MaxEvents 30 -ErrorAction Stop | Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message
    }
    Write-PiTable -Rows $rows -Columns @("TimeCreated","Id","LevelDisplayName","Message") -SaveAsLastResult -ResultName "ClusterEvents"
    Pause-Pi
}



function Convert-PiClusterOwnerNodeListToNames {
    param([object]$OwnerNodeResult)

    $names = @()

    foreach ($item in @($OwnerNodeResult)) {
        if ($null -eq $item) { continue }

        if ($item -is [string]) {
            if (-not [string]::IsNullOrWhiteSpace($item)) { $names += $item }
            continue
        }

        $propNames = @($item.PSObject.Properties.Name)

        foreach ($candidate in @("Name","NodeName","OwnerNode","ClusterNode","Node")) {
            if ($propNames -contains $candidate) {
                $value = $item.$candidate
                if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
                    $names += [string]$value
                }
            }
        }

        if ($propNames -contains "ClusterObject") {
            $co = $item.ClusterObject
            if ($null -ne $co) {
                $coProps = @($co.PSObject.Properties.Name)
                if ($coProps -contains "Name") { $names += [string]$co.Name }
            }
        }

        if ($names.Count -eq 0) {
            $text = [string]$item
            if (-not [string]::IsNullOrWhiteSpace($text) -and $text -notmatch "ClusterOwnerNodeList") {
                $names += $text
            }
        }
    }

    return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Get-PiClusterOwnerNodeNamesSafe {
    param(
        [string]$GroupName,
        [object]$Resource
    )

    try {
        if ($null -ne $Resource) {
            $result = $Resource | Get-ClusterOwnerNode -ErrorAction Stop
            return @(Convert-PiClusterOwnerNodeListToNames -OwnerNodeResult $result)
        }

        if (-not [string]::IsNullOrWhiteSpace($GroupName)) {
            $result = Get-ClusterOwnerNode -Group $GroupName -ErrorAction Stop
            return @(Convert-PiClusterOwnerNodeListToNames -OwnerNodeResult $result)
        }
    } catch {
        return @()
    }

    return @()
}


function Get-PiClusterVMPlacementRows {
    if (-not (Test-PiClusterModule)) { return @() }

    $rows = @()

    $groups = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VM Cluster Groups." -ScriptBlock {
        Get-ClusterGroup -ErrorAction Stop |
            Where-Object { $_.GroupType -eq "VirtualMachine" } |
            Sort-Object OwnerNode, Name
    }

    foreach ($g in @($groups)) {
        $preferredOwners = ""
        $possibleOwners = ""
        $antiAffinity = ""
        $autoFailback = ""
        $failbackWindow = ""
        $ownerNodes = @(Get-PiClusterOwnerNodeNamesSafe -GroupName $g.Name)
        $preferredOwners = ($ownerNodes -join ", ")
        try {
            $resources = @(Get-ClusterResource -Group $g.Name -ErrorAction Stop)
            $vmResource = $resources | Where-Object { $_.ResourceType -eq "Virtual Machine" } | Select-Object -First 1

            if ($vmResource) {
                $possibleNodes = @(Get-PiClusterOwnerNodeNamesSafe -Resource $vmResource)
                $possibleOwners = ($possibleNodes -join ", ")
            }
        } catch {
            $possibleOwners = ""
        }

        try {
            if ($g.PSObject.Properties.Name -contains "AntiAffinityClassNames") {
                $antiAffinity = ($g.AntiAffinityClassNames -join ", ")
            }
        } catch {
            $antiAffinity = ""
        }

        try {
            if ($g.PSObject.Properties.Name -contains "AutoFailbackType") {
                $autoFailback = [string]$g.AutoFailbackType
            }
        } catch {
            $autoFailback = ""
        }

        try {
            $start = if ($g.PSObject.Properties.Name -contains "FailbackWindowStart") { $g.FailbackWindowStart } else { "" }
            $end = if ($g.PSObject.Properties.Name -contains "FailbackWindowEnd") { $g.FailbackWindowEnd } else { "" }
            if ($start -ne "" -or $end -ne "") {
                $failbackWindow = ("{0}-{1}" -f $start, $end)
            }
        } catch {
            $failbackWindow = ""
        }

        $rows += [PSCustomObject]@{
            VMGroup         = $g.Name
            State           = $g.State
            OwnerNode       = $g.OwnerNode
            Priority        = $g.Priority
            PreferredOwners = $preferredOwners
            PossibleOwners  = $possibleOwners
            AntiAffinity    = $antiAffinity
            AutoFailback    = $autoFailback
            FailbackWindow  = $failbackWindow
        }
    }

    return @($rows)
}

function Show-PiClusterVMOwnership {
    Show-Header "VM Ownership / Placement"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $rows = @(Get-PiClusterVMPlacementRows)

    Write-PiTable -Rows $rows -Columns @("VMGroup","State","OwnerNode","Priority","PreferredOwners","PossibleOwners","AntiAffinity") `
        -NoResultsMessage "Neboli nájdené VM Cluster Groups." `
        -Hints @("táto funkcia dáva zmysel na Failover Clusteri", "skontroluj, či VM sú clusterované role") `
        -SaveAsLastResult -ResultName "VMOwnershipPlacement"

    Write-Host ""
    Write-PiInfo "Poznámka:"
    Write-Host "OwnerNode je aktuálny vlastník role."
    Write-Host "PreferredOwners/PossibleOwners závisia od cluster konfigurácie a dostupných owner-node informácií."
    Pause-Pi
}

function Show-PiClusterPreferredOwners {
    Show-Header "Preferred / Possible Owners"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $rows = @(Get-PiClusterVMPlacementRows | Select-Object VMGroup, OwnerNode, PreferredOwners, PossibleOwners, AutoFailback, FailbackWindow | Sort-Object OwnerNode, VMGroup)

    Write-PiTable -Rows $rows -Columns @("VMGroup","OwnerNode","PreferredOwners","PossibleOwners","AutoFailback","FailbackWindow") `
        -NoResultsMessage "Neboli nájdené Preferred/Possible owner informácie." `
        -SaveAsLastResult -ResultName "PreferredPossibleOwners"

    Pause-Pi
}

function Show-PiVMDistributionByOwner {
    Show-Header "VM Distribution by Owner Node"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $placement = @(Get-PiClusterVMPlacementRows)
    $vmRows = @(Get-PiVMBaseRows)

    $rows = @()
    foreach ($group in ($placement | Group-Object OwnerNode)) {
        $node = [string]$group.Name
        $ownedGroups = @($group.Group)
        $nodeVmRows = @($vmRows | Where-Object { $_.HostNode -eq $node -and $_.State -eq "Running" })

        $rows += [PSCustomObject]@{
            OwnerNode   = $node
            VMGroups    = $ownedGroups.Count
            RunningVM   = $nodeVmRows.Count
            vCPU        = ($nodeVmRows | Measure-Object CPU -Sum).Sum
            AssignedGB  = [math]::Round(($nodeVmRows | Measure-Object AssignedGB -Sum).Sum, 1)
            DemandGB    = [math]::Round(($nodeVmRows | Measure-Object DemandGB -Sum).Sum, 1)
            WasteGB     = [math]::Round(($nodeVmRows | Measure-Object WasteGB -Sum).Sum, 1)
            HighPriority = @($ownedGroups | Where-Object { $_.Priority -match "High|3000" }).Count
        }
    }

    Write-PiTable -Rows $rows -Columns @("OwnerNode","VMGroups","RunningVM","vCPU","AssignedGB","DemandGB","WasteGB","HighPriority") `
        -NoResultsMessage "Nebolo možné zostaviť distribúciu VM podľa OwnerNode." `
        -SaveAsLastResult -ResultName "VMDistributionByOwner"

    Pause-Pi
}

function Show-PiPlacementAdvisor {
    Show-Header "Placement Advisor"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $placement = @(Get-PiClusterVMPlacementRows)
    $vmRows = @(Get-PiVMBaseRows | Where-Object { $_.State -eq "Running" })
    $nodeHw = @(Get-PiNodeHardwareRows)

    $rows = @()

    foreach ($group in ($vmRows | Group-Object HostNode)) {
        $node = [string]$group.Name
        $items = @($group.Group)
        $hw = $nodeHw | Where-Object { $_.Node -eq $node } | Select-Object -First 1

        $ramTotal = if ($hw -and $hw.RAMGB -ne "") { [double]$hw.RAMGB } else { 0 }
        $demand = [math]::Round(($items | Measure-Object DemandGB -Sum).Sum, 1)
        $assigned = [math]::Round(($items | Measure-Object AssignedGB -Sum).Sum, 1)
        $demandPct = if ($ramTotal -gt 0) { [math]::Round(($demand / $ramTotal) * 100, 1) } else { "" }
        $assignedPct = if ($ramTotal -gt 0) { [math]::Round(($assigned / $ramTotal) * 100, 1) } else { "" }

        $severity = "OK"
        $recommendation = "Vyzerá vyvážene."

        if ($demandPct -ne "" -and $demandPct -gt 85) {
            $severity = "Warning"
            $recommendation = "Demand RAM je vysoký. Skontroluj failover kapacitu druhého node."
        } elseif ($assignedPct -ne "" -and $assignedPct -gt 90) {
            $severity = "Info"
            $recommendation = "Assigned RAM je vysoký, ale Demand môže byť v poriadku. Sledovať."
        }

        $rows += [PSCustomObject]@{
            Severity    = $severity
            Node        = $node
            RunningVM   = $items.Count
            vCPU        = ($items | Measure-Object CPU -Sum).Sum
            RAMGB       = if ($hw) { $hw.RAMGB } else { "" }
            FreeRAMGB   = if ($hw) { $hw.FreeRAMGB } else { "" }
            DemandGB    = $demand
            DemandPct   = $demandPct
            AssignedGB  = $assigned
            AssignedPct = $assignedPct
            Recommendation = $recommendation
        }
    }

    $vmWithoutPreferred = @($placement | Where-Object { [string]::IsNullOrWhiteSpace($_.PreferredOwners) })
    if ($vmWithoutPreferred.Count -gt 0) {
        $rows += [PSCustomObject]@{
            Severity    = "Info"
            Node        = "Cluster"
            RunningVM   = ""
            vCPU        = ""
            RAMGB       = ""
            FreeRAMGB   = ""
            DemandGB    = ""
            DemandPct   = ""
            AssignedGB  = ""
            AssignedPct = ""
            Recommendation = "$($vmWithoutPreferred.Count) VM role nemá zistených PreferredOwners."
        }
    }

    Write-PiTable -Rows $rows -Columns @("Severity","Node","RunningVM","vCPU","RAMGB","FreeRAMGB","DemandGB","DemandPct","AssignedGB","AssignedPct","Recommendation") `
        -NoResultsMessage "Placement Advisor nenašiel žiadne údaje." `
        -SaveAsLastResult -ResultName "PlacementAdvisor"

    Write-Host ""
    Write-PiInfo "Poznámka:"
    Write-Host "Advisor zatiaľ iba číta a upozorňuje. Nič nemení v clustri."
    Write-Host "Pre presné failover plánovanie treba brať do úvahy aj rezervu pre host OS, storage a konkrétne workloady."
    Pause-Pi
}


function Show-PiFailoverSimulation {
    Show-Header "Failover Simulation"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $failedNode = Select-PiClusterNode -Title "Vyber node, ktorého výpadok chceš simulovať"
    if ([string]::IsNullOrWhiteSpace($failedNode)) { return }

    Show-Header ("Failover Simulation - výpadok {0}" -f $failedNode)

    $allNodes = @(Get-PiTargetNodes)
    if ($Script:ScopeMode -ne "Cluster") {
        $allNodes = @(Get-ClusterNode -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    }

    $targetNodes = @($allNodes | Where-Object { $_ -ne $failedNode })

    if ($targetNodes.Count -eq 0) {
        Show-NoResults -Title "Nie je dostupný iný node na simuláciu failoveru."
        Pause-Pi
        return
    }

    $vms = @(Get-PiVMBaseRows)
    $failedNodeRunning = @($vms | Where-Object { $_.HostNode -eq $failedNode -and $_.State -eq "Running" })

    if ($failedNodeRunning.Count -eq 0) {
        Show-NoResults -Title "Na vybranom node nebežia žiadne VM podľa aktuálneho scope."
        Pause-Pi
        return
    }

    $nodeHw = @(Get-PiNodeHardwareRows)
    $neededDemand = [math]::Round(($failedNodeRunning | Measure-Object DemandGB -Sum).Sum, 1)
    $neededAssigned = [math]::Round(($failedNodeRunning | Measure-Object AssignedGB -Sum).Sum, 1)
    $neededVcpu = ($failedNodeRunning | Measure-Object CPU -Sum).Sum

    $rows = @()

    foreach ($target in $targetNodes) {
        $targetHw = $nodeHw | Where-Object { $_.Node -eq $target } | Select-Object -First 1
        $targetRunning = @($vms | Where-Object { $_.HostNode -eq $target -and $_.State -eq "Running" })

        $targetDemand = [math]::Round(($targetRunning | Measure-Object DemandGB -Sum).Sum, 1)
        $targetAssigned = [math]::Round(($targetRunning | Measure-Object AssignedGB -Sum).Sum, 1)
        $targetRam = if ($targetHw -and $targetHw.RAMGB -ne "") { [double]$targetHw.RAMGB } else { 0 }
        $targetFreeOs = if ($targetHw -and $targetHw.FreeRAMGB -ne "") { [double]$targetHw.FreeRAMGB } else { 0 }

        $afterDemand = [math]::Round($targetDemand + $neededDemand, 1)
        $afterAssigned = [math]::Round($targetAssigned + $neededAssigned, 1)
        $afterDemandPct = if ($targetRam -gt 0) { [math]::Round(($afterDemand / $targetRam) * 100, 1) } else { "" }
        $afterAssignedPct = if ($targetRam -gt 0) { [math]::Round(($afterAssigned / $targetRam) * 100, 1) } else { "" }
        $freeAfterDemand = if ($targetRam -gt 0) { [math]::Round($targetRam - $afterDemand, 1) } else { "" }

        $status = "OK"
        $advice = "Failover podľa Demand RAM vyzerá priechodne."

        if ($targetRam -gt 0 -and $afterDemandPct -gt 95) {
            $status = "Critical"
            $advice = "Po failoveri by Demand RAM prekročil bezpečnú hranicu. Nutné znížiť RAM, presunúť VM alebo navýšiť kapacitu."
        } elseif ($targetRam -gt 0 -and $afterDemandPct -gt 85) {
            $status = "Warning"
            $advice = "Po failoveri bude RAM veľmi tesná. Odporúčané optimalizovať RAM alebo rozloženie VM."
        } elseif ($targetRam -gt 0 -and $afterAssignedPct -gt 95) {
            $status = "Info"
            $advice = "Assigned RAM bude vysoká, ale Demand môže byť OK. Skontroluj dynamickú RAM a reálnu záťaž."
        }

        $rows += [PSCustomObject]@{
            FailedNode       = $failedNode
            TargetNode       = $target
            VMsToMove        = $failedNodeRunning.Count
            MoveDemandGB     = $neededDemand
            MoveAssignedGB   = $neededAssigned
            MovevCPU         = $neededVcpu
            TargetRAMGB      = $targetRam
            TargetFreeOSGB   = $targetFreeOs
            AfterDemandGB    = $afterDemand
            AfterDemandPct   = $afterDemandPct
            FreeAfterDemandGB = $freeAfterDemand
            Status           = $status
            Advice           = $advice
        }
    }

    Write-PiTable -Rows $rows -Columns @("FailedNode","TargetNode","VMsToMove","MoveDemandGB","MoveAssignedGB","MovevCPU","TargetRAMGB","TargetFreeOSGB","AfterDemandGB","AfterDemandPct","FreeAfterDemandGB","Status","Advice") `
        -NoResultsMessage "Nepodarilo sa zostaviť failover simuláciu." `
        -SaveAsLastResult -ResultName "FailoverSimulation"

    Write-Host ""
    Write-PiInfo "VM, ktoré by sa presúvali z $failedNode:"
    $vmList = $failedNodeRunning | Sort-Object DemandGB -Descending | Select-Object VM, CPU, AssignedGB, DemandGB, WasteGB, IPv4
    Write-PiTable -Rows $vmList -Columns @("VM","CPU","AssignedGB","DemandGB","WasteGB","IPv4") `
        -NoResultsMessage "Zoznam VM je prázdny."

    Write-Host ""
    Write-PiInfo "Čo robiť pri probléme:"
    Write-Host "  1. Najprv sleduj DemandGB, nie iba AssignedGB."
    Write-Host "  2. Kandidátom na úpravu RAM sú VM s vysokým WasteGB."
    Write-Host "  3. Kritické workloady ako SQL/Veeam/Exchange neupravuj bez dlhšieho sledovania."
    Write-Host "  4. Skontroluj, či VM môžu bežať na druhom node a či majú dostupné CSV/storage."
    Write-Host "  5. Ak je failover tesný, zváž presun menej kritických VM na iný node alebo navýšenie RAM."
    Write-Host "  6. Po úpravách vždy over Dashboard, Node Summary, Storage Jobs a CSV voľné miesto."
    Pause-Pi
}

function Show-PiClusterHealthScore {
    Show-Header "Cluster Health Score"

    if (-not (Test-PiClusterModule)) { Pause-Pi; return }

    $score = 100
    $checks = @()

    $nodes = @(Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster nody." -ScriptBlock { Get-ClusterNode -ErrorAction Stop })
    $downNodes = @($nodes | Where-Object { $_.State -ne "Up" })
    if ($downNodes.Count -gt 0) { $score -= 30 }
    $checks += [PSCustomObject]@{ Area="Nodes"; Status=if($downNodes.Count -eq 0){"OK"}else{"Critical"}; Detail=("{0}/{1} Up" -f (@($nodes|Where-Object State -eq "Up").Count), $nodes.Count) }

    $resources = @(Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať cluster resources." -ScriptBlock { Get-ClusterResource -ErrorAction Stop })
    $badResources = @($resources | Where-Object { $_.State -notin @("Online","Offline") })
    if ($badResources.Count -gt 0) { $score -= 20 }
    $checks += [PSCustomObject]@{ Area="Resources"; Status=if($badResources.Count -eq 0){"OK"}else{"Warning"}; Detail=("$($badResources.Count) not OK/pending/failed") }

    $csvs = @(Get-PiCSVRows)
    $lowCsv = @($csvs | Where-Object { $_.FreePercent -lt 15 })
    if ($lowCsv.Count -gt 0) { $score -= 15 }
    $checks += [PSCustomObject]@{ Area="CSV Capacity"; Status=if($lowCsv.Count -eq 0){"OK"}else{"Warning"}; Detail=("$($lowCsv.Count) CSV below 15%") }

    $jobs = @(Get-PiStorageJobRows)
    if ($jobs.Count -gt 0) { $score -= 10 }
    $checks += [PSCustomObject]@{ Area="Storage Jobs"; Status=if($jobs.Count -eq 0){"OK"}else{"Warning"}; Detail=("$($jobs.Count) active/listed") }

    $quorum = @(Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať quorum." -ScriptBlock { Get-ClusterQuorum -ErrorAction Stop })
    $checks += [PSCustomObject]@{ Area="Quorum/Witness"; Status=if($quorum.Count -gt 0){"OK"}else{"Warning"}; Detail=if($quorum.Count -gt 0){[string]$quorum[0].QuorumType}else{"Unknown"} }
    if ($quorum.Count -eq 0) { $score -= 10 }

    $vms = @(Get-PiVMBaseRows | Where-Object { $_.State -eq "Running" })
    $negative = @($vms | Where-Object { $_.WasteGB -lt 0 })
    if ($negative.Count -gt 0) { $score -= 5 }
    $checks += [PSCustomObject]@{ Area="VM Memory Pressure"; Status=if($negative.Count -eq 0){"OK"}else{"Info"}; Detail=("$($negative.Count) VM with Demand > Assigned") }

    if ($score -lt 0) { $score = 0 }

    Write-PiTable -Rows $checks -Columns @("Area","Status","Detail") -SaveAsLastResult -ResultName "ClusterHealthScore"

    Write-Host ""
    if ($score -ge 90) {
        Write-Host ("Overall Health Score: {0} %" -f $score) -ForegroundColor Green
    } elseif ($score -ge 70) {
        Write-Host ("Overall Health Score: {0} %" -f $score) -ForegroundColor Yellow
    } else {
        Write-Host ("Overall Health Score: {0} %" -f $score) -ForegroundColor Red
    }

    Write-Host ""
    Write-PiInfo "Rady:"
    Write-Host "  - Ak sú Storage Jobs aktívne, nerob veľké presuny VM, kým nedobehnú."
    Write-Host "  - Pri nízkom voľnom mieste na CSV najprv identifikuj VM disky cez Storage > VM Storage Map."
    Write-Host "  - Pri RAM tlaku použi Diagnostics > Advisor a Failover Simulation."
    Write-Host "  - Pri resource problémoch skontroluj Cluster > Resources a Cluster Events."
    Pause-Pi
}

function Show-PiFailoverMenu {
    do {
        Show-Header "Failover / Health"
        Write-Host "1  - Failover Simulation"
        Write-Host "2  - Cluster Health Score"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"

        switch ($choice.ToUpper()) {
            "1" { Show-PiFailoverSimulation }
            "2" { Show-PiClusterHealthScore }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}


function Show-PiClusterPlacementMenu {
    do {
        Show-Header "Cluster Placement / Ownership"
        Write-Host "1  - VM Ownership / Placement"
        Write-Host "2  - Preferred / Possible Owners"
        Write-Host "3  - VM Distribution by Owner Node"
        Write-Host "4  - Placement Advisor"
        Write-Host "5  - Failover Simulation"
        Write-Host "6  - Cluster Health Score"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"

        switch ($choice.ToUpper()) {
            "1" { Show-PiClusterVMOwnership }
            "2" { Show-PiClusterPreferredOwners }
            "3" { Show-PiVMDistributionByOwner }
            "4" { Show-PiPlacementAdvisor }
            "5" { Show-PiFailoverSimulation }
            "6" { Show-PiClusterHealthScore }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}


function Show-PiClusterMenu {
    do {
        Show-Header "Cluster"
        Write-Host "1  - Nodes"
        Write-Host "2  - Roles / Groups"
        Write-Host "3  - Resources"
        Write-Host "4  - Networks"
        Write-Host "5  - Shared Volumes"
        Write-Host "6  - Quorum"
        Write-Host "7  - Witness"
        Write-Host "8  - Events"
        Write-Host "9  - Placement / Ownership"
        Write-Host "10 - Failover / Health"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiClusterNodes }
            "2" { Show-PiClusterRoles }
            "3" { Show-PiClusterResources }
            "4" { Show-PiClusterNetworks }
            "5" { Show-PiCSVOverview }
            "6" { Show-PiQuorum }
            "7" { Show-PiWitness }
            "8" { Show-PiClusterEvents }
            "9" { Show-PiClusterPlacementMenu }
            "10" { Show-PiFailoverMenu }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
