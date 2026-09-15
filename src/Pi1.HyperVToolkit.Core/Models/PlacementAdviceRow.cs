namespace Pi1.HyperVToolkit.Core.Models;

// Placement Advisor recommendation kind. Core carries the SEVERITY DECISION;
// WPF renders localized text (spec N). Parity maps kinds to the reference
// Slovak sentences; live comparison happens on a real cluster.
public enum PlacementAdviceKind
{
    Balanced,
    HighDemand,
    HighAssigned,
    NoPreferredOwners,
}

// Parity with Show-PiPlacementAdvisor rows. RAM aggregates cover RUNNING VMs
// only; percentages are null when node RAM is unknown/zero (PS "" parity).
// The trailing cluster-level row uses Node "Cluster" with empty numerics.
public sealed record PlacementAdviceRow(
    string Severity,
    string Node,
    int? RunningVM,
    int? VCpu,
    double? RAMGB,
    double? FreeRAMGB,
    double? DemandGB,
    double? DemandPct,
    double? AssignedGB,
    double? AssignedPct,
    PlacementAdviceKind Advice,
    int AdviceCount,
    long? RamBytes = null,
    long? FreeBytes = null,
    long? DemandBytes = null,
    long? AssignedBytes = null);
