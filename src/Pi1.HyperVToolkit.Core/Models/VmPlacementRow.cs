namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Get-PiClusterVMPlacementRows: ONE row per VirtualMachine cluster
// group (GroupType == "VirtualMachine"). Owner names are structured
// collections internally; Display joins them as "NODE1, NODE2".
public sealed record VmPlacementRow(
    string VMGroup,
    string State,
    string OwnerNode,
    string Priority,
    IReadOnlyList<string> PreferredOwners,
    IReadOnlyList<string> PossibleOwners,
    string AntiAffinity,
    string AutoFailback,
    string FailbackWindow)
{
    public string PreferredOwnersDisplay => string.Join(", ", PreferredOwners);

    public string PossibleOwnersDisplay => string.Join(", ", PossibleOwners);
}
