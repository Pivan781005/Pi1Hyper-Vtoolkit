# π1 Hyper-V Toolkit - Shared data collectors

function Get-PiVMBaseRows {
    $rows = @()
    foreach ($node in (Get-PiTargetNodes)) {
        $vms = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VM z node $node." -ScriptBlock {
            Get-VM -ComputerName $node -ErrorAction Stop
        }

        foreach ($vm in @($vms)) {
            $nics = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať sieťové adaptéry VM $($vm.Name) na node $node." -ScriptBlock {
                Get-VMNetworkAdapter -ComputerName $node -VMName $vm.Name -ErrorAction Stop
            }

            if (@($nics).Count -eq 0) {
                $rows += [PSCustomObject]@{
                    HostNode=$node; VM=$vm.Name; State=[string]$vm.State; CPU=$vm.ProcessorCount
                    AssignedGB=[math]::Round($vm.MemoryAssigned/1GB,1)
                    DemandGB=[math]::Round($vm.MemoryDemand/1GB,1)
                    WasteGB=[math]::Round(($vm.MemoryAssigned-$vm.MemoryDemand)/1GB,1)
                    IPv4=""; MAC=""; Switch=""; Uptime=(Format-PiTimeSpan $vm.Uptime)
                    Dynamic=$vm.DynamicMemoryEnabled
                    StartupGB=[math]::Round($vm.MemoryStartup/1GB,1)
                    MinimumGB=[math]::Round($vm.MemoryMinimum/1GB,1)
                    MaximumGB=[math]::Round($vm.MemoryMaximum/1GB,1)
                }
            } else {
                foreach ($nic in @($nics)) {
                    $rows += [PSCustomObject]@{
                        HostNode=$node; VM=$vm.Name; State=[string]$vm.State; CPU=$vm.ProcessorCount
                        AssignedGB=[math]::Round($vm.MemoryAssigned/1GB,1)
                        DemandGB=[math]::Round($vm.MemoryDemand/1GB,1)
                        WasteGB=[math]::Round(($vm.MemoryAssigned-$vm.MemoryDemand)/1GB,1)
                        IPv4=(Get-PiIPv4 $nic.IPAddresses); MAC=$nic.MacAddress; Switch=$nic.SwitchName
                        Uptime=(Format-PiTimeSpan $vm.Uptime)
                        Dynamic=$vm.DynamicMemoryEnabled
                        StartupGB=[math]::Round($vm.MemoryStartup/1GB,1)
                        MinimumGB=[math]::Round($vm.MemoryMinimum/1GB,1)
                        MaximumGB=[math]::Round($vm.MemoryMaximum/1GB,1)
                    }
                }
            }
        }
    }
    return @($rows)
}

function Get-PiVMNetworkRows {
    $rows = @()
    foreach ($node in (Get-PiTargetNodes)) {
        $nics = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VM sieťové adaptéry z node $node." -ScriptBlock {
            Get-VMNetworkAdapter -ComputerName $node -All -ErrorAction Stop
        }
        foreach ($nic in @($nics)) {
            $rows += [PSCustomObject]@{
                HostNode=$node; VMName=$nic.VMName; IPv4=(Get-PiIPv4 $nic.IPAddresses)
                MacAddress=$nic.MacAddress; SwitchName=$nic.SwitchName; AllIPs=($nic.IPAddresses -join ', ')
            }
        }
    }
    return @($rows)
}

function Get-PiNodeHardwareRows {
    $rows = @()
    foreach ($node in (Get-PiTargetNodes)) {
        $cs = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať ComputerSystem z node $node." -ScriptBlock {
            Get-CimInstance -ClassName Win32_ComputerSystem -ComputerName $node -ErrorAction Stop
        } | Select-Object -First 1
        $os = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať OperatingSystem z node $node." -ScriptBlock {
            Get-CimInstance -ClassName Win32_OperatingSystem -ComputerName $node -ErrorAction Stop
        } | Select-Object -First 1
        $cpu = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať CPU z node $node." -ScriptBlock {
            Get-CimInstance -ClassName Win32_Processor -ComputerName $node -ErrorAction Stop
        }

        $totalGB = if ($os) { [math]::Round($os.TotalVisibleMemorySize/1MB,1) } elseif ($cs) { [math]::Round($cs.TotalPhysicalMemory/1GB,1) } else { "" }
        $freeGB = if ($os) { [math]::Round($os.FreePhysicalMemory/1MB,1) } else { "" }
        $usedGB = if ($os) { [math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB,1) } else { "" }
        $usedPct = if ($os -and $os.TotalVisibleMemorySize -gt 0) { [math]::Round((($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/$os.TotalVisibleMemorySize)*100,1) } else { "" }

        $rows += [PSCustomObject]@{
            Node=$node
            Manufacturer=if($cs){$cs.Manufacturer}else{""}
            Model=if($cs){$cs.Model}else{""}
            OS=if($os){$os.Caption}else{""}
            Version=if($os){$os.Version}else{""}
            Uptime=if($os){Format-PiTimeSpan ((Get-Date)-$os.LastBootUpTime)}else{""}
            CPUName=if($cpu){(($cpu|Select-Object -ExpandProperty Name -Unique)-join ", ")}else{""}
            Sockets=@($cpu).Count
            Cores=if($cpu){($cpu|Measure-Object NumberOfCores -Sum).Sum}else{""}
            LogicalCPU=if($cpu){($cpu|Measure-Object NumberOfLogicalProcessors -Sum).Sum}else{""}
            RAMGB=$totalGB
            UsedRAMGB=$usedGB
            FreeRAMGB=$freeGB
            RAMUsedPct=$usedPct
        }
    }
    return @($rows)
}

function Get-PiVMHostSettingsRows {
    $rows = @()
    foreach ($node in (Get-PiTargetNodes)) {
        $hostInfo = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Hyper-V host settings z node $node." -ScriptBlock {
            Get-VMHost -ComputerName $node -ErrorAction Stop
        } | Select-Object -First 1
        if ($hostInfo) {
            $hw = Get-PiNodeHardwareRows | Where-Object Node -eq $node | Select-Object -First 1
            $rows += [PSCustomObject]@{
                Node=$node; CPU=$hostInfo.LogicalProcessorCount
                RAMGB=[math]::Round($hostInfo.MemoryCapacity/1GB,1)
                FreeRAMGB=if($hw){$hw.FreeRAMGB}else{""}
                RAMUsedPct=if($hw){$hw.RAMUsedPct}else{""}
                NUMA=$hostInfo.NumaSpanningEnabled
                Migration=$hostInfo.VirtualMachineMigrationEnabled
                MaxMig=$hostInfo.MaximumVirtualMachineMigrations
                Enhanced=$hostInfo.EnableEnhancedSessionMode
                VMPath=$hostInfo.VirtualMachinePath
                VHDPath=$hostInfo.VirtualHardDiskPath
                LogicalProcessorCount=$hostInfo.LogicalProcessorCount
                MemoryCapacityGB=[math]::Round($hostInfo.MemoryCapacity/1GB,1)
                NumaSpanningEnabled=$hostInfo.NumaSpanningEnabled
                EnableEnhancedSessionMode=$hostInfo.EnableEnhancedSessionMode
                MaximumVirtualMachineMigrations=$hostInfo.MaximumVirtualMachineMigrations
                VirtualMachineMigrationEnabled=$hostInfo.VirtualMachineMigrationEnabled
                VirtualMachinePath=$hostInfo.VirtualMachinePath
                VirtualHardDiskPath=$hostInfo.VirtualHardDiskPath
            }
        }
    }
    return @($rows)
}

function Get-PiCSVRows {
    if (-not (Get-Module -ListAvailable -Name FailoverClusters)) { return @() }
    Import-Module FailoverClusters -ErrorAction SilentlyContinue
    $rows = @()
    $csvs = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Cluster Shared Volumes." -ScriptBlock {
        Get-ClusterSharedVolume -ErrorAction Stop
    }
    foreach ($csv in @($csvs)) {
        foreach ($i in @($csv.SharedVolumeInfo)) {
            $size = $i.Partition.Size
            $free = $i.Partition.FreeSpace
            $rows += [PSCustomObject]@{
                Name=$csv.Name; State=$csv.State; OwnerNode=$csv.OwnerNode
                SizeGB=[math]::Round($size/1GB,1)
                FreeGB=[math]::Round($free/1GB,1)
                UsedGB=[math]::Round(($size-$free)/1GB,1)
                FreePercent=if($size -gt 0){[math]::Round(($free/$size)*100,1)}else{0}
                Path=$i.FriendlyVolumeName
            }
        }
    }
    return @($rows)
}

function Get-PiStorageJobRows {
    $jobs = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať Storage Jobs." -ScriptBlock {
        Get-StorageJob -ErrorAction Stop | Select-Object Name, JobState, JobType, PercentComplete, BytesProcessed, BytesTotal, ElapsedTime
    }
    return @($jobs)
}

function Get-PiCsvMatchForPath {
    param([string]$Path, [object[]]$CsvRows)
    if ([string]::IsNullOrWhiteSpace($Path) -or $null -eq $CsvRows) { return $null }
    $best = $null
    foreach ($csv in @($CsvRows)) {
        if (-not [string]::IsNullOrWhiteSpace($csv.Path)) {
            if ($Path.ToLower().StartsWith(([string]$csv.Path).ToLower())) {
                if ($null -eq $best -or ([string]$csv.Path).Length -gt ([string]$best.Path).Length) { $best = $csv }
            }
        }
    }
    return $best
}

function Get-PiVMStorageRows {
    $rows = @()
    $csvRows = @(Get-PiCSVRows)
    foreach ($node in (Get-PiTargetNodes)) {
        $vms = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VM z node $node." -ScriptBlock { Get-VM -ComputerName $node -ErrorAction Stop }
        foreach ($vm in @($vms)) {
            $drives = Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať disky VM $($vm.Name) na node $node." -ScriptBlock {
                Get-VMHardDiskDrive -ComputerName $node -VMName $vm.Name -ErrorAction Stop
            }
            foreach ($drive in @($drives)) {
                $vhdSizeGB=$null; $vhdFileGB=$null; $vhdType="Unknown"; $vhdFormat="Unknown"
                try {
                    $vhd = Get-VHD -ComputerName $node -Path $drive.Path -ErrorAction Stop
                    $vhdSizeGB=[math]::Round($vhd.Size/1GB,1)
                    $vhdFileGB=[math]::Round($vhd.FileSize/1GB,1)
                    $vhdType=[string]$vhd.VhdType
                    $vhdFormat=[string]$vhd.VhdFormat
                } catch {}
                $csv = Get-PiCsvMatchForPath -Path $drive.Path -CsvRows $csvRows
                $rows += [PSCustomObject]@{
                    HostNode=$node; VM=$vm.Name; State=[string]$vm.State
                    Controller=("{0} {1}:{2}" -f $drive.ControllerType,$drive.ControllerNumber,$drive.ControllerLocation)
                    VHDFormat=$vhdFormat; VHDType=$vhdType; VHDSizeGB=$vhdSizeGB; VHDFileGB=$vhdFileGB
                    CSV=if($csv){$csv.Name}else{""}
                    CSVFreeGB=if($csv){$csv.FreeGB}else{""}
                    CSVFreePercent=if($csv){$csv.FreePercent}else{""}
                    Path=$drive.Path
                }
            }
        }
    }
    return @($rows)
}

Export-ModuleMember -Function *
