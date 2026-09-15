using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Read-only Hyper-V inventory. Native WMI (root/virtualization/v2) in Phase 3;
/// no PowerShell hosting. Every method fans out over <paramref name="nodes"/>
/// with bounded concurrency, keeps partial results and reports per-node errors.
/// </summary>
public interface IHyperVService
{
    /// <summary>Parity with Get-PiVMBaseRows (one row per VM per NIC).</summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>Parity with Get-PiVMNetworkRows.</summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>Parity with Show-PiCheckpoints (read-only inventory, no create/delete).</summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parity with Show-PiVMSwitches: one row per virtual switch, fanned out
    /// over <paramref name="nodes"/> with partial per-node results.
    /// </summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>> GetVirtualSwitchesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parity with Show-PiVMAdapterVlan (read-only): one row per CURRENT VM
    /// network adapter port; historical/snapshot settings never become rows.
    /// </summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>> GetVmAdapterVlansAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parity with Get-PiVMStorageRows: one row per VM hard-disk drive with
    /// native VHD inspection and the longest-prefix CSV join. The caller
    /// supplies <paramref name="csvRows"/> collected ONCE per refresh so the
    /// join never re-queries cluster state per VM or per disk.
    /// </summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>> GetVmStorageAsync(
        IReadOnlyList<string> nodes,
        IReadOnlyList<CsvRow> csvRows,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Parity with Get-PiVMHostSettingsRows, without its N+1 defect: caller passes
    /// node hardware collected once per refresh in <paramref name="hardwareByNode"/>
    /// for the FreeRAMGB/RAMUsedPct join.
    /// </summary>
    Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
        IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
        CancellationToken cancellationToken = default);
}
