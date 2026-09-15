namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVMAdapterVlan (scope-aware, read-only).
// OperationMode: Untagged (no explicit setting) / Access / Trunk / Private.
// VLAN IDs are null when the provider reports none.
public sealed record VmVlanRow(
    string HostNode,
    string VMName,
    string VMNetworkAdapterName,
    string OperationMode,
    int? AccessVlanId,
    int? NativeVlanId,
    string AllowedVlanIdList);
