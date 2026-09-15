namespace Pi1.HyperVToolkit.Core.Utilities;

/// <summary>
/// Frozen thresholds, formulas and limits discovered in Phase 1 (PowerShell v0.9.4).
/// DO NOT retune: functional parity requires the exact values.
/// Numeric formatting elsewhere uses Math.Round(x, 1) (MidpointRounding.ToEven),
/// matching PowerShell [math]::Round(x, 1).
/// </summary>
public static class Thresholds
{
    // Write-PiTable colour rules (Pi1.Core.psm1)
    public const double RamUsedRedPercent = 85.0;
    public const double RamUsedYellowPercent = 70.0;
    public const double FreePercentRed = 10.0;
    public const double FreePercentYellow = 20.0;
    public const double WasteYellowGb = 8.0;

    // Advisor (Pi1.Diagnostics.psm1 / Show-PiAdvisor)
    public const double WasteInfoGb = 8.0;
    public const double WasteLargeInfoGb = 16.0;

    /// <summary>PowerShell: $vm.VM -match "SQL|VEEAM|EXCHANGE|FORTI|FAZ" (case-insensitive).</summary>
    public const string SensitiveWorkloadPattern = "SQL|VEEAM|EXCHANGE|FORTI|FAZ";

    // Dashboard / health CSV rules. NOTE: two different cut-offs exist on purpose:
    // Dashboard + Health Score warn below 15 %, the Diagnostics low-free view lists below 20 %.
    public const double CsvWarningPercent = 15.0;
    public const double CsvLowFreeViewPercent = 20.0;

    // Cluster Health Score deductions (Pi1.Cluster.psm1 / Show-PiClusterHealthScore)
    public const int ScoreStart = 100;
    public const int ScoreNodesDownDeduction = 30;
    public const int ScoreBadResourcesDeduction = 20;
    public const int ScoreLowCsvDeduction = 15;
    public const int ScoreStorageJobsDeduction = 10;
    public const int ScoreNoQuorumDeduction = 10;
    public const int ScoreMemoryPressureDeduction = 5;
    public const int ScoreExcellentBand = 90;
    public const int ScoreGoodBand = 70;

    // Failover Simulation status bands (Show-PiFailoverSimulation)
    public const double FailoverDemandCriticalPercent = 95.0;
    public const double FailoverDemandWarningPercent = 85.0;
    public const double FailoverAssignedInfoPercent = 95.0;

    // Placement Advisor bands (Show-PiPlacementAdvisor)
    public const double PlacementDemandWarningPercent = 85.0;
    public const double PlacementAssignedInfoPercent = 90.0;

    // VM Distribution: Priority -match "High|3000".
    public const string HighPriorityPattern = "High|3000";

    // Cluster Events: Get-WinEvent -MaxEvents 30.
    public const int ClusterEventsMaxCount = 30;

    // Console width hints (140/160 chars) are intentionally NOT reproduced in WPF:
    // DataGrids scroll, resize and show full-value tooltips instead.
}
