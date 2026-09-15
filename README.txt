π1 Hyper-V Toolkit v0.9.4

Spustenie:
1. Rozbaľ ZIP napr. do C:\Install\Pi1_HyperV_Toolkit_v0.9.4
2. Otvor PowerShell ako Administrátor
3. Prejdi do priečinka:
   cd C:\Install\Pi1_HyperV_Toolkit_v0.9.4
4. Spusti:
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   .\Pi1-HyperVToolkit.ps1

Dôležité:
- Spúšťa sa len Pi1-HyperVToolkit.ps1.
- Priečinok Modules nechaj vedľa hlavného PS1 súboru.
- Táto verzia je read-only.
- Najlepšie funguje v maximalizovanom PowerShell okne.

Zmena v0.9.4:
- Nodes > Node Hardware / OS má Overview a Detail zobrazenie.

Zmena v0.9.4:
- Cluster > Placement / Ownership.
- VM OwnerNode, PreferredOwners, PossibleOwners, Distribution, Placement Advisor.

Zmena v0.9.4:
- Oprava Get-ClusterOwnerNode výstupu.
- Cluster > Failover / Health.
- Failover Simulation a Cluster Health Score s odporúčaniami.

Zmena v0.9.4:
- Startup error handling a logovanie do priečinka Logs.
- Pri chybe počas štartu sa aplikácia zastaví a zobrazí detail.
