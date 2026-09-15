using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Read-only Windows/node inventory via CIM (root/cimv2).
/// Works through CIM even where Hyper-V is unavailable.
/// </summary>
public interface ISystemInformationService
{
    /// <summary>
    /// Parity with Get-PiNodeHardwareRows (ComputerSystem + OperatingSystem + Processor).
    /// </summary>
    Task<IReadOnlyList<NodeResult<NodeHardwareRow>>> GetNodeHardwareAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>Parity with Show-PiNodeVolumes (Win32_LogicalDisk, DriveType=3).</summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>> GetNodeVolumesAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parity with Show-PiNodeNetworkAdapters
    /// (Win32_NetworkAdapterConfiguration, IPEnabled=True).
    /// </summary>
    Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>> GetNodeAdaptersAsync(
        IReadOnlyList<string> nodes, CancellationToken cancellationToken = default);
}
