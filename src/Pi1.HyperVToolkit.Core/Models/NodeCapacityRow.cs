namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// One node of the Node VM Capacity view. Parity with Show-PiNodeVMCapacity:
/// aggregates cover Running VMs only, except <see cref="OffVM"/> which counts
/// State "Off" exactly (other states such as Paused/Saved count in neither).
/// Empty sums behave like PowerShell Measure-Object: vCPU is null, memory sums
/// are 0, percentages are null when node RAM is unknown or zero.
/// </summary>
public sealed record NodeCapacityRow(
    string Node,
    int RunningVM,
    int OffVM,
    int? VCpu,
    int? LogicalCPU,
    double? RAMGB,
    double? FreeRAMGB,
    double? RAMUsedPct,
    double AssignedGB,
    double DemandGB,
    double WasteGB,
    double? AssignedPct,
    double? DemandPct,
    long AssignedBytes = 0,
    long DemandBytes = 0,
    long WasteBytes = 0,
    long? TotalBytes = null,
    long? FreeBytes = null);
