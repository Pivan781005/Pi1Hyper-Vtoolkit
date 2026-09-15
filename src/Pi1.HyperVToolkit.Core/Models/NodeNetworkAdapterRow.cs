namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// One node network adapter row. Parity with Show-PiNodeNetworkAdapters
/// (Win32_NetworkAdapterConfiguration, IPEnabled=True only).
/// </summary>
public sealed record NodeNetworkAdapterRow(
    string Node,
    string Description,
    string IPv4,
    string MAC,
    string Gateway,
    string DNS,
    bool? DHCP);
