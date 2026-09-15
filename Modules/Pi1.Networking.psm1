# π1 Hyper-V Toolkit - Networking

function Show-PiVMSwitches {
    Show-Header "Virtual Switches"
    $rows=@()
    foreach($node in (Get-PiTargetNodes)){
        $switches=Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VMSwitch z node $node." -ScriptBlock { Get-VMSwitch -ComputerName $node -ErrorAction Stop }
        foreach($sw in @($switches)){
            $rows += [PSCustomObject]@{HostNode=$node; Name=$sw.Name; SwitchType=$sw.SwitchType; NetAdapterInterfaceDescription=$sw.NetAdapterInterfaceDescription; AllowManagementOS=$sw.AllowManagementOS}
        }
    }
    Write-PiTable -Rows $rows -Columns @("HostNode","Name","SwitchType","AllowManagementOS","NetAdapterInterfaceDescription") -SaveAsLastResult -ResultName "VMSwitches"
    Pause-Pi
}

function Show-PiVMAdapterVlan {
    Show-Header "VM Adapter VLAN"
    $rows=@()
    foreach($node in (Get-PiTargetNodes)){
        $vlans=Invoke-PiSafe -ErrorMessage "Nepodarilo sa načítať VLAN konfiguráciu z node $node." -ScriptBlock { Get-VMNetworkAdapterVlan -ComputerName $node -ErrorAction Stop }
        foreach($v in @($vlans)){
            $rows += [PSCustomObject]@{HostNode=$node; VMName=$v.VMName; VMNetworkAdapterName=$v.VMNetworkAdapterName; OperationMode=$v.OperationMode; AccessVlanId=$v.AccessVlanId; NativeVlanId=$v.NativeVlanId; AllowedVlanIdList=$v.AllowedVlanIdList}
        }
    }
    Write-PiTable -Rows $rows -Columns @("HostNode","VMName","VMNetworkAdapterName","OperationMode","AccessVlanId","NativeVlanId","AllowedVlanIdList") -SaveAsLastResult -ResultName "VMAdapterVLAN"
    Pause-Pi
}

function Show-PiNetworkingMenu {
    do {
        Show-Header "Networking"
        Write-Host "1  - Virtual Switches"
        Write-Host "2  - VM Adapters"
        Write-Host "3  - VM Adapter VLAN"
        Write-Host "4  - Search by IP"
        Write-Host "5  - Search by MAC"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiVMSwitches }
            "2" { Show-PiVMNetwork }
            "3" { Show-PiVMAdapterVlan }
            "4" { Find-PiVMByIP }
            "5" { Find-PiVMByMac }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

Export-ModuleMember -Function *
