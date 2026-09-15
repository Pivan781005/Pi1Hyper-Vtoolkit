# π1 Hyper-V Toolkit - Storage

function Show-PiStorageJobs {
    Show-Header "Storage Jobs"
    $rows = Get-PiStorageJobRows
    Write-PiTable -Rows $rows -Columns @("Name","JobState","JobType","PercentComplete","BytesProcessed","BytesTotal","ElapsedTime") -NoResultsMessage "Neboli nájdené žiadne Storage Jobs. To je dobrý stav, ak nečakáš repair/rebalance." -SaveAsLastResult -ResultName "StorageJobs"
    Pause-Pi
}

function Show-PiCSVOverview {
    Show-Header "Cluster Shared Volumes"
    if (-not (Test-PiClusterModule)) { Pause-Pi; return }
    $rows = @(Get-PiCSVRows | Sort-Object FreePercent)
    Write-PiTable -Rows $rows -Columns @("Name","State","OwnerNode","SizeGB","FreeGB","UsedGB","FreePercent","Path") -SaveAsLastResult -ResultName "CSV"
    Pause-Pi
}

function Show-PiStoragePools {
    Show-Header "Storage Pools"
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Storage Pools." -ScriptBlock {
        Get-StoragePool -ErrorAction Stop | Where-Object {$_.IsPrimordial -eq $false} | Select-Object FriendlyName, HealthStatus, OperationalStatus, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='AllocatedGB';Expression={[math]::Round($_.AllocatedSize/1GB,1)}}, @{Name='FreeGB';Expression={[math]::Round(($_.Size-$_.AllocatedSize)/1GB,1)}} | Sort-Object FriendlyName
    }
    Write-PiTable -Rows $rows -Columns @("FriendlyName","HealthStatus","OperationalStatus","SizeGB","AllocatedGB","FreeGB") -SaveAsLastResult -ResultName "StoragePools"
    Pause-Pi
}

function Show-PiVirtualDisks {
    Show-Header "Virtual Disks"
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Virtual Disks." -ScriptBlock {
        Get-VirtualDisk -ErrorAction Stop | Select-Object FriendlyName, HealthStatus, OperationalStatus, ResiliencySettingName, ProvisioningType, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='AllocatedGB';Expression={[math]::Round($_.AllocatedSize/1GB,1)}} | Sort-Object FriendlyName
    }
    Write-PiTable -Rows $rows -Columns @("FriendlyName","HealthStatus","OperationalStatus","ResiliencySettingName","ProvisioningType","SizeGB","AllocatedGB") -SaveAsLastResult -ResultName "VirtualDisks"
    Pause-Pi
}

function Show-PiPhysicalDisks {
    Show-Header "Physical Disks"
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať fyzické disky." -ScriptBlock {
        Get-PhysicalDisk -ErrorAction Stop | Select-Object FriendlyName, SerialNumber, CanPool, HealthStatus, OperationalStatus, MediaType, BusType, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, Usage | Sort-Object MediaType, BusType, FriendlyName
    }
    Write-PiTable -Rows $rows -Columns @("FriendlyName","MediaType","BusType","SizeGB","HealthStatus","OperationalStatus","CanPool","Usage","SerialNumber") -SaveAsLastResult -ResultName "PhysicalDisks"
    Pause-Pi
}

function Show-PiPhysicalDiskSummary {
    Show-Header "Physical Disk Summary"
    $disks = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať fyzické disky." -ScriptBlock { Get-PhysicalDisk -ErrorAction Stop }
    if (@($disks).Count -eq 0) { Show-NoResults -Title "Neboli nájdené fyzické disky."; Pause-Pi; return }
    $rows = $disks | Group-Object MediaType, BusType | ForEach-Object {
        $items=$_.Group
        [PSCustomObject]@{Group=$_.Name; Count=$items.Count; TotalGB=[math]::Round(($items|Measure-Object Size -Sum).Sum/1GB,1); Healthy=@($items|Where-Object HealthStatus -eq "Healthy").Count; NotHealthy=@($items|Where-Object HealthStatus -ne "Healthy").Count; CanPool=@($items|Where-Object CanPool -eq $true).Count}
    } | Sort-Object Group
    Write-PiTable -Rows $rows -Columns @("Group","Count","TotalGB","Healthy","NotHealthy","CanPool") -SaveAsLastResult -ResultName "PhysicalDiskSummary"
    Pause-Pi
}

function Show-PiVolumes {
    Show-Header "Volumes"
    $rows = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať volumes." -ScriptBlock {
        Get-Volume -ErrorAction Stop | Where-Object {$_.DriveType -eq "Fixed"} | Select-Object DriveLetter, FileSystemLabel, FileSystem, HealthStatus, OperationalStatus, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,1)}}, @{Name='FreeGB';Expression={[math]::Round($_.SizeRemaining/1GB,1)}}, @{Name='FreePercent';Expression={if($_.Size -gt 0){[math]::Round(($_.SizeRemaining/$_.Size)*100,1)}else{0}}} | Sort-Object DriveLetter, FileSystemLabel
    }
    Write-PiTable -Rows $rows -Columns @("DriveLetter","FileSystemLabel","FileSystem","HealthStatus","OperationalStatus","SizeGB","FreeGB","FreePercent") -SaveAsLastResult -ResultName "Volumes"
    Pause-Pi
}

function Show-PiStorageSummary {
    Show-Header "Storage Summary"
    $pools = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Storage Pools." -ScriptBlock { Get-StoragePool -ErrorAction Stop | Where-Object {$_.IsPrimordial -eq $false} }
    $vdisks = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Virtual Disks." -ScriptBlock { Get-VirtualDisk -ErrorAction Stop }
    $csvs = Get-PiCSVRows
    $jobs = Get-PiStorageJobRows
    $rows = @(
        [PSCustomObject]@{ Area="Storage Pools"; Count=@($pools).Count; Status=if(@($pools|Where-Object {$_.HealthStatus -ne "Healthy"}).Count -gt 0){"Warning"}else{"OK"} }
        [PSCustomObject]@{ Area="Virtual Disks"; Count=@($vdisks).Count; Status=if(@($vdisks|Where-Object {$_.HealthStatus -ne "Healthy"}).Count -gt 0){"Warning"}else{"OK"} }
        [PSCustomObject]@{ Area="CSV"; Count=@($csvs).Count; Status=if(@($csvs|Where-Object {$_.FreePercent -lt 15}).Count -gt 0){"Warning"}else{"OK"} }
        [PSCustomObject]@{ Area="Storage Jobs"; Count=@($jobs).Count; Status=if(@($jobs).Count -gt 0){"Warning"}else{"None"} }
    )
    Write-PiTable -Rows $rows -Columns @("Area","Count","Status") -SaveAsLastResult -ResultName "StorageSummary"
    Pause-Pi
}

function Show-PiVMStorageMap {
    Show-Header "VM Storage Map - VM disky a CSV/Volume"
    $rows = @(Get-PiVMStorageRows | Sort-Object CSVFreePercent, HostNode, VM, Path)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","VHDSizeGB","VHDFileGB","VHDType","CSV","CSVFreeGB","CSVFreePercent","Path") -SaveAsLastResult -ResultName "VMStorageMap"
    Pause-Pi
}

function Show-PiVMStorageByCSV {
    Show-Header "VM Storage podľa CSV"
    $rows = @(Get-PiVMStorageRows)
    if ($rows.Count -eq 0) { Show-NoResults -Title "Neboli nájdené žiadne VM disky."; Pause-Pi; return }
    $grouped = $rows | Group-Object CSV | ForEach-Object {
        $items=$_.Group
        $csvName=if([string]::IsNullOrWhiteSpace($_.Name)){"(mimo CSV / nezistené)"}else{$_.Name}
        $first=$items|Where-Object {$_.CSVFreePercent -ne ""}|Select-Object -First 1
        [PSCustomObject]@{CSV=$csvName; VMCount=@($items|Select-Object -ExpandProperty VM -Unique).Count; DiskCount=$items.Count; VHDSizeGB=[math]::Round(($items|Measure-Object VHDSizeGB -Sum).Sum,1); VHDFileGB=[math]::Round(($items|Measure-Object VHDFileGB -Sum).Sum,1); CSVFreeGB=if($first){$first.CSVFreeGB}else{""}; CSVFreePercent=if($first){$first.CSVFreePercent}else{""}}
    } | Sort-Object CSVFreePercent
    Write-PiTable -Rows @($grouped) -Columns @("CSV","VMCount","DiskCount","VHDSizeGB","VHDFileGB","CSVFreeGB","CSVFreePercent") -SaveAsLastResult -ResultName "VMStorageByCSV"
    Pause-Pi
}

function Show-PiVMStorageForSelectedVM {
    $selected = Select-PiVM -Title "VM Storage - výber VM"
    if($null -eq $selected){return}
    Show-Header ("VM Storage - {0}" -f $selected.VM)
    $rows = @(Get-PiVMStorageRows | Where-Object { $_.HostNode -eq $selected.HostNode -and $_.VM -eq $selected.VM } | Sort-Object Path)
    Write-PiTable -Rows $rows -Columns @("HostNode","VM","State","Controller","VHDFormat","VHDType","VHDSizeGB","VHDFileGB","CSV","CSVFreeGB","CSVFreePercent","Path") -SaveAsLastResult -ResultName "VMStorageDetail"
    Pause-Pi
}

function Show-PiStorageMenu {
    do {
        Show-Header "Storage"
        Write-Host "1  - Storage Jobs"
        Write-Host "2  - Storage Summary"
        Write-Host "3  - Storage Pools"
        Write-Host "4  - Virtual Disks"
        Write-Host "5  - Physical Disks"
        Write-Host "6  - Physical Disk Summary (MediaType / BusType)"
        Write-Host "7  - CSV Capacity"
        Write-Host "8  - Volumes"
        Write-Host ""
        Write-Host "9  - VM Storage Map"
        Write-Host "10 - VM Storage podľa CSV"
        Write-Host "11 - VM Storage pre vybranú VM"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiStorageJobs }
            "2" { Show-PiStorageSummary }
            "3" { Show-PiStoragePools }
            "4" { Show-PiVirtualDisks }
            "5" { Show-PiPhysicalDisks }
            "6" { Show-PiPhysicalDiskSummary }
            "7" { Show-PiCSVOverview }
            "8" { Show-PiVolumes }
            "9" { Show-PiVMStorageMap }
            "10" { Show-PiVMStorageByCSV }
            "11" { Show-PiVMStorageForSelectedVM }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
