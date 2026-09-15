namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiNodeSummary: one row per HostNode present in the VM rows.
// vCPU/RAM sums cover RUNNING VMs only. vCPU is null (empty) when no VM runs;
// Assigned/Demand/Waste round an empty sum to 0, exactly like PowerShell
// [math]::Round($null, 1). Hardware fields are null when the join misses.
public sealed record NodeSummaryRow(
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
    long AssignedBytes = 0,
    long DemandBytes = 0,
    long WasteBytes = 0,
    long? TotalBytes = null,
    long? FreeBytes = null);
