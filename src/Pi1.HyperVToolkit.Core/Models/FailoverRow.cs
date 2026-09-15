namespace Pi1.HyperVToolkit.Core.Models;

// Failover Simulation outcome kind. Core carries the DECISION; WPF renders
// localized advice. Strictly a calculation — never a real failover.
public enum FailoverAdviceKind
{
    Viable,
    Tight,
    Critical,
    AssignedHigh,
}

// Parity with Show-PiFailoverSimulation per-target rows. Percentages are null
// when target RAM is unknown/zero (PS "" parity).
public sealed record FailoverRow(
    string FailedNode,
    string TargetNode,
    int VMsToMove,
    double MoveDemandGB,
    double MoveAssignedGB,
    int? MoveVCpu,
    double TargetRAMGB,
    double TargetFreeOSGB,
    double AfterDemandGB,
    double? AfterDemandPct,
    double? FreeAfterDemandGB,
    string Status,
    FailoverAdviceKind Advice,
    long MoveDemandBytes = 0,
    long MoveAssignedBytes = 0,
    long TargetRAMBytes = 0,
    long TargetFreeBytes = 0,
    long AfterDemandBytes = 0,
    long? FreeAfterBytes = null);

// Pure-simulation result: per-target rows plus the VM list that would move
// (references into the already-collected VM snapshot, no re-query).
public sealed record FailoverResult(
    IReadOnlyList<FailoverRow> TargetRows,
    IReadOnlyList<VirtualMachineRow> MoveVms);
