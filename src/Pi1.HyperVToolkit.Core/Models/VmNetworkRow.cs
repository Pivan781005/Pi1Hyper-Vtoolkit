namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>One VM network adapter row. Parity with Get-PiVMNetworkRows.</summary>
public sealed record VmNetworkRow(
    string HostNode,
    string VMName,
    string IPv4,
    string MacAddress,
    string SwitchName,
    string AllIPs);
