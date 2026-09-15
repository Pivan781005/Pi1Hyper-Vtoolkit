namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVMDistributionByOwner: placement rows grouped by
// OwnerNode; vCPU/RAM sums cover RUNNING VMs with HostNode == OwnerNode only.
// HighPriority counts owned groups whose Priority matches "High|3000"
// (PowerShell -match semantics: case-insensitive substring/regex).
public sealed record VmDistributionRow(
    string OwnerNode,
    int VMGroups,
    int RunningVM,
    int? VCpu,
    double AssignedGB,
    double DemandGB,
    double WasteGB,
    int HighPriority,
    long AssignedBytes = 0,
    long DemandBytes = 0,
    long WasteBytes = 0);
