# π1 Hyper-V Toolkit - Export / Settings / Help

function Export-PiLastResultCsv {
    Show-Header "Export posledného výpisu do CSV"
    if ($null -eq $Script:LastResult -or @($Script:LastResult).Count -eq 0) {
        Show-NoResults -Title "Nie je čo exportovať. Najprv spusti nejaký výpis/report."
        Pause-Pi
        return
    }
    $safeName = ($Script:LastResultName -replace '[^\w\-]', '_')
    if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = "Pi1_Report" }
    $defaultPath = Join-Path $env:USERPROFILE ("Desktop\{0}_{1}.csv" -f $safeName, (Get-Date -Format "yyyyMMdd_HHmmss"))
    $path = Read-Host "Zadaj cestu pre CSV alebo nechaj prázdne pre $defaultPath"
    if ([string]::IsNullOrWhiteSpace($path)) { $path = $defaultPath }
    try {
        @($Script:LastResult) | Export-Csv -Path $path -NoTypeInformation -Encoding UTF8 -ErrorAction Stop
        Write-Host ""
        Write-Host "Export hotový:" -ForegroundColor Green
        Write-Host $path
    } catch {
        Write-PiWarn "Export zlyhal."
        Write-Host "Detail: $($_.Exception.Message)" -ForegroundColor DarkGray
    }
    Pause-Pi
}

function Export-PiFullVMReportCsv {
    Show-Header "Export kompletného VM reportu do CSV"
    $rows = Get-PiVMBaseRows
    Set-PiLastResult -Rows $rows -Name "FullVMReport"
    Export-PiLastResultCsv
}

function Show-PiExportMenu {
    do {
        Show-Header "Export"
        Write-Host "1  - Export posledného výpisu do CSV"
        Write-Host "2  - Export kompletného VM reportu do CSV"
        Write-Host "3  - Export HTML (neskôr)"
        Write-Host "4  - Export JSON (neskôr)"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Export-PiLastResultCsv }
            "2" { Export-PiFullVMReportCsv }
            "3" { Write-PiWarn "Funkcia bude doplnená v ďalšej verzii."; Pause-Pi }
            "4" { Write-PiWarn "Funkcia bude doplnená v ďalšej verzii."; Pause-Pi }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

function Show-PiSettings {
    do {
        Show-Header "Settings"
        Write-Host "1  - Zmeniť Scope"
        Write-Host "2  - Prepnúť legendy RAM (aktuálne: $Script:ShowLegend)"
        Write-Host ""
        Write-Host "0  - Späť"
        Write-Host ""
        $choice = Read-Host "Vyber možnosť"
        switch ($choice.ToUpper()) {
            "1" { Show-PiScopeMenu }
            "2" { $Script:ShowLegend = -not $Script:ShowLegend }
            "0" { return }
            default { Write-Host "Neplatná voľba." -ForegroundColor Red; Start-Sleep -Seconds 1 }
        }
    } while ($true)
}

function Show-PiHelp {
    Show-Header "Help"
    Write-PiInfo "Spustenie"
    Write-Host "  Spúšťaj hlavný súbor: .\Pi1-HyperVToolkit.ps1"
    Write-Host "  Moduly v priečinku Modules sa načítajú automaticky."
    Write-Host "  Startup chyby sa ukladajú do priečinka Logs."
    Write-Host ""
    Write-PiInfo "Scope"
    Write-Host "  Určuje, či nástroj číta len lokálny node, všetky cluster nody alebo konkrétny node."
    Write-Host "  Ak VM beží na druhom node, prepni Scope na All cluster nodes."
    Write-Host ""
    Write-PiInfo "Read-only"
    Write-Host "  Táto verzia nič nemení v Hyper-V, clustri ani storage."
    Pause-Pi
}

function Show-PiChangelog {
    Show-Header "Changelog"
    Write-Host "v0.9.4" -ForegroundColor Cyan
    Write-Host "  - prechod na modulárnu štruktúru: Main ps1 + Modules"
    Write-Host "  - Dashboard doplnený o RAM Total/Used/Free"
    Write-Host "  - Node/Host Settings doplnené o FreeRAMGB a RAMUsedPct"
    Write-Host "  - zachované read-only správanie"
    Write-Host ""
    Write-Host "v0.8.x" -ForegroundColor Cyan
    Write-Host "  - VM, Nodes, Cluster, Storage, Networking, Diagnostics, Export"
    Pause-Pi
}

Export-ModuleMember -Function *
