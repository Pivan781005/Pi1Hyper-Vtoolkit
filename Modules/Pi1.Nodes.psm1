# π1 Hyper-V Toolkit - Nodes

function Show-PiNodeHardwareOverview {
    Show-Header "Node Hardware / OS - Overview"

    Show-PiConsoleWidthHint -RecommendedWidth 140

    $base = @(Get-PiNodeHardwareRows | Sort-Object Node)
    $rows = @()

    foreach ($r in $base) {
        $rows += [PSCustomObject]@{
            Node       = $r.Node
            Vendor     = $r.Manufacturer
            Model      = $r.Model
            OS         = ($r.OS -replace "Microsoft Windows Server ","WinSrv ")
            Build      = $r.Version
            Uptime     = $r.Uptime
            CPU        = $r.LogicalCPU
            Cores      = $r.Cores
            RAMGB      = $r.RAMGB
            FreeRAMGB  = $r.FreeRAMGB
            RAMUsedPct = $r.RAMUsedPct
        }
    }

    Write-PiTable -Rows $rows -Columns @("Node","Vendor","Model","OS","Build","Uptime","CPU","Cores","RAMGB","FreeRAMGB","RAMUsedPct") `
        -NoResultsMessage "Nepodarilo sa načítať hardvérové informácie o nodoch." `
        -Hints @("skontroluj Scope", "skontroluj oprávnenia", "remote CIM/WMI musí byť dostupné") `
        -SaveAsLastResult -ResultName "NodeHardwareOverview"

    Write-Host ""
    Write-PiInfo "Legenda:"
    Write-Host "  Vendor     = Manufacturer"
    Write-Host "  CPU        = Logical CPU / Logical processors"
    Write-Host "  RAMGB      = Total visible memory"
    Write-Host "  FreeRAMGB  = aktuálne voľná RAM podľa OS"
    Write-Host "  RAMUsedPct = percento použitej RAM podľa OS"

    Pause-Pi
}

function Show-PiNodeHardwareDetailAll {
    Show-Header "Node Hardware / OS - Detail"

    $rows = @(Get-PiNodeHardwareRows | Sort-Object Node)

    if ($rows.Count -eq 0) {
        Show-NoResults -Title "Nepodarilo sa načítať hardvérové informácie o nodoch."
        Pause-Pi
        return
    }

    Set-PiLastResult -Rows $rows -Name "NodeHardwareDetail"

    foreach ($r in $rows) {
        Write-Host "============================================================" -ForegroundColor DarkCyan
        Write-Host $r.Node -ForegroundColor Cyan
        Write-Host "============================================================" -ForegroundColor DarkCyan

        Write-Host ("Manufacturer          : {0}" -f $r.Manufacturer)
        Write-Host ("Model                 : {0}" -f $r.Model)
        Write-Host ("OS                    : {0}" -f $r.OS)
        Write-Host ("Version / Build       : {0}" -f $r.Version)
        Write-Host ("Uptime                : {0}" -f $r.Uptime)
        Write-Host ""

        Write-Host "CPU:" -ForegroundColor Yellow
        Write-Host ("  Sockets             : {0}" -f $r.Sockets)
        Write-Host ("  Cores               : {0}" -f $r.Cores)
        Write-Host ("  Logical processors  : {0}" -f $r.LogicalCPU)
        Write-Host ("  CPU name            : {0}" -f $r.CPUName)
        Write-Host ""

        Write-Host "RAM:" -ForegroundColor Yellow
        Write-Host ("  Total               : {0} GB" -f $r.RAMGB)
        Write-Host ("  Free                : {0} GB" -f $r.FreeRAMGB)
        Write-Host ("  Used                : {0} %" -f $r.RAMUsedPct)
        Write-Host ""
    }

    Pause-Pi
}

function Show-PiNodeHardware {
    do {
        Show-Header "Node Hardware / OS"
        Write-Host "1  - Overview tabuľka pre všetky nody"
        Write-Host "2  - Detail pod sebou pre všetky nody"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"

        switch ($choice.ToUpper()) {
            "1" { Show-PiNodeHardwareOverview }
            "2" { Show-PiNodeHardwareDetailAll }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

function Show-PiNodeVMCapacity {
    Show-Header "Node VM Capacity / Load"
    $vms = @(Get-PiVMBaseRows)
    $hw = @(Get-PiNodeHardwareRows)
    $rows = @()
    foreach ($group in ($vms | Group-Object HostNode)) {
        $items=@($group.Group)
        $running=@($items|Where-Object State -eq "Running")
        $nodeHw=$hw|Where-Object Node -eq $group.Name|Select-Object -First 1
        $assigned=[math]::Round(($running|Measure-Object AssignedGB -Sum).Sum,1)
        $demand=[math]::Round(($running|Measure-Object DemandGB -Sum).Sum,1)
        $waste=[math]::Round(($running|Measure-Object WasteGB -Sum).Sum,1)
        $ramTotal=if($nodeHw -and $nodeHw.RAMGB -ne ""){[double]$nodeHw.RAMGB}else{0}
        $rows += [PSCustomObject]@{
            Node=$group.Name; RunningVM=$running.Count; OffVM=@($items|Where-Object State -eq "Off").Count
            vCPU=($running|Measure-Object CPU -Sum).Sum; LogicalCPU=if($nodeHw){$nodeHw.LogicalCPU}else{""}
            RAMGB=if($nodeHw){$nodeHw.RAMGB}else{""}
            FreeRAMGB=if($nodeHw){$nodeHw.FreeRAMGB}else{""}
            RAMUsedPct=if($nodeHw){$nodeHw.RAMUsedPct}else{""}
            AssignedGB=$assigned; DemandGB=$demand; WasteGB=$waste
            AssignedPct=if($ramTotal -gt 0){[math]::Round(($assigned/$ramTotal)*100,1)}else{""}
            DemandPct=if($ramTotal -gt 0){[math]::Round(($demand/$ramTotal)*100,1)}else{""}
        }
    }
    Write-PiTable -Rows $rows -Columns @("Node","RunningVM","OffVM","vCPU","LogicalCPU","RAMGB","FreeRAMGB","RAMUsedPct","AssignedGB","DemandGB","WasteGB","AssignedPct","DemandPct") -SaveAsLastResult -ResultName "NodeVMCapacity"
    Pause-Pi
}

function Show-PiNodeVolumes {
    Show-Header "Node Local Volumes"
    $rows=@()
    foreach($node in (Get-PiTargetNodes)){
        $vols=Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať lokálne volumes z node $node." -ScriptBlock {
            Get-CimInstance -ClassName Win32_LogicalDisk -ComputerName $node -Filter "DriveType=3" -ErrorAction Stop
        }
        foreach($v in @($vols)){
            $rows += [PSCustomObject]@{
                Node=$node; Drive=$v.DeviceID; Label=$v.VolumeName; FileSystem=$v.FileSystem
                SizeGB=[math]::Round($v.Size/1GB,1); FreeGB=[math]::Round($v.FreeSpace/1GB,1)
                FreePercent=if($v.Size -gt 0){[math]::Round(($v.FreeSpace/$v.Size)*100,1)}else{0}
            }
        }
    }
    Write-PiTable -Rows $rows -Columns @("Node","Drive","Label","FileSystem","SizeGB","FreeGB","FreePercent") -SaveAsLastResult -ResultName "NodeVolumes"
    Pause-Pi
}

function Show-PiNodeNetworkAdapters {
    Show-Header "Node Network Adapters"
    $rows=@()
    foreach($node in (Get-PiTargetNodes)){
        $adapters=Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať sieťové adaptéry z node $node." -ScriptBlock {
            Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -ComputerName $node -Filter "IPEnabled=True" -ErrorAction Stop
        }
        foreach($a in @($adapters)){
            $rows += [PSCustomObject]@{
                Node=$node; Description=$a.Description; MAC=$a.MACAddress
                IPv4=(($a.IPAddress|Where-Object {$_ -match '^\d+\.'}) -join ", ")
                Gateway=($a.DefaultIPGateway -join ", "); DNS=($a.DNSServerSearchOrder -join ", "); DHCP=$a.DHCPEnabled
            }
        }
    }
    Write-PiTable -Rows $rows -Columns @("Node","Description","IPv4","MAC","Gateway","DNS","DHCP") -SaveAsLastResult -ResultName "NodeNetworkAdapters"
    Pause-Pi
}

function Show-PiVMHostSettingsOverview {
    Show-Header "Hyper-V Host Settings - Overview"
    Show-PiConsoleWidthHint -RecommendedWidth 140
    $rows = @(Get-PiVMHostSettingsRows | Sort-Object Node)
    Write-PiTable -Rows $rows -Columns @("Node","CPU","RAMGB","FreeRAMGB","RAMUsedPct","NUMA","Migration","MaxMig","Enhanced","VMPath","VHDPath") -SaveAsLastResult -ResultName "VMHostSettingsOverview"
    Write-Host ""
    Write-PiInfo "Legenda:"
    Write-Host "  CPU        = LogicalProcessorCount"
    Write-Host "  RAMGB      = MemoryCapacityGB"
    Write-Host "  FreeRAMGB  = aktuálna voľná RAM podľa OS"
    Write-Host "  RAMUsedPct = percento použitej RAM podľa OS"
    Write-Host "  NUMA       = NumaSpanningEnabled"
    Write-Host "  Migration  = VirtualMachineMigrationEnabled"
    Write-Host "  MaxMig     = MaximumVirtualMachineMigrations"
    Write-Host "  Enhanced   = EnableEnhancedSessionMode"
    Write-Host "  VMPath     = VirtualMachinePath"
    Write-Host "  VHDPath    = VirtualHardDiskPath"
    Pause-Pi
}

function Show-PiVMHostSettingsDetailAll {
    Show-Header "Hyper-V Host Settings - Detail"
    $rows = @(Get-PiVMHostSettingsRows | Sort-Object Node)
    if($rows.Count -eq 0){Show-NoResults -Title "Nepodarilo sa načítať Hyper-V host settings."; Pause-Pi; return}
    Set-PiLastResult -Rows $rows -Name "VMHostSettingsDetail"
    foreach($r in $rows){
        Write-Host "============================================================" -ForegroundColor DarkCyan
        Write-Host $r.Node -ForegroundColor Cyan
        Write-Host "============================================================" -ForegroundColor DarkCyan
        Write-Host ("CPU / Logical processors     : {0}" -f $r.LogicalProcessorCount)
        Write-Host ("Memory capacity              : {0} GB" -f $r.MemoryCapacityGB)
        Write-Host ("Free memory                  : {0} GB" -f $r.FreeRAMGB)
        Write-Host ("Memory used                  : {0} %" -f $r.RAMUsedPct)
        Write-Host ("NUMA spanning enabled        : {0}" -f $r.NumaSpanningEnabled)
        Write-Host ("Live Migration enabled       : {0}" -f $r.VirtualMachineMigrationEnabled)
        Write-Host ("Max simultaneous migrations  : {0}" -f $r.MaximumVirtualMachineMigrations)
        Write-Host ("Enhanced Session Mode        : {0}" -f $r.EnableEnhancedSessionMode)
        Write-Host ""
        Write-Host "Virtual Machine Path:" -ForegroundColor Yellow
        Write-Host ("  {0}" -f $r.VirtualMachinePath)
        Write-Host ""
        Write-Host "Virtual Hard Disk Path:" -ForegroundColor Yellow
        Write-Host ("  {0}" -f $r.VirtualHardDiskPath)
        Write-Host ""
    }
    Pause-Pi
}

function Show-PiVMHostSettings {
    do {
        Show-Header "Hyper-V Host Settings"
        Write-Host "1  - Overview tabuľka pre všetky nody"
        Write-Host "2  - Detail pod sebou pre všetky nody"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch($choice.ToUpper()){
            "1"{Show-PiVMHostSettingsOverview}
            "2"{Show-PiVMHostSettingsDetailAll}
            "0"{return}
            default{Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1}
        }
    } while($true)
}

function Show-PiNodeMenu {
    do {
        Show-Header "Nodes"
        Write-Host "1  - Node Hardware / OS"
        Write-Host "2  - Node VM Capacity / Load"
        Write-Host "3  - Node Local Volumes"
        Write-Host "4  - Node Network Adapters"
        Write-Host "5  - Hyper-V Host Settings"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiNodeHardware }
            "2" { Show-PiNodeVMCapacity }
            "3" { Show-PiNodeVolumes }
            "4" { Show-PiNodeNetworkAdapters }
            "5" { Show-PiVMHostSettings }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
