namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVMSwitches (scope-aware fan-out over target nodes).
// SwitchType: External / Internal / Private (native derivation, PARTIAL until
// elevated live parity). AllowManagementOS is null when not determinable.
public sealed record VmSwitchRow(
    string HostNode,
    string Name,
    string SwitchType,
    bool? AllowManagementOS,
    string NetAdapterInterfaceDescription);
