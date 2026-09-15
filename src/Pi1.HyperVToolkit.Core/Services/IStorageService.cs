using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

// Read-only local storage inventory over root/Microsoft/Windows/Storage.
// LOCAL semantics by design: Get-PiStorageJobRows ignores Pi scope and every
// other PowerShell storage collector queries the local machine only.
// No scope fan-out here — callers must not pass target nodes.
public interface IStorageService
{
    // Parity with Get-PiStorageJobRows. Empty (not an error) when idle:
    // "No Storage Jobs" is normally a GOOD state.
    Task<IReadOnlyList<StorageJobRow>> GetStorageJobsAsync(
        CancellationToken cancellationToken = default);

    // Parity with Show-PiStoragePools (non-primordial only, by FriendlyName).
    Task<IReadOnlyList<StoragePoolRow>> GetStoragePoolsAsync(
        CancellationToken cancellationToken = default);

    // Parity with Show-PiVirtualDisks (by FriendlyName).
    Task<IReadOnlyList<VirtualDiskRow>> GetVirtualDisksAsync(
        CancellationToken cancellationToken = default);

    // Parity with Show-PiPhysicalDisks (by MediaType, BusType, FriendlyName).
    Task<IReadOnlyList<PhysicalDiskRow>> GetPhysicalDisksAsync(
        CancellationToken cancellationToken = default);

    // Parity with Show-PiVolumes (DriveType Fixed only).
    Task<IReadOnlyList<StorageVolumeRow>> GetVolumesAsync(
        CancellationToken cancellationToken = default);

    // Parity with Get-PiCSVRows. Empty when the FailoverClusters capability
    // (root/MSCluster) is unavailable — expected on non-cluster hosts, NOT an error.
    Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(
        CancellationToken cancellationToken = default);
}
