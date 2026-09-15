namespace Pi1.HyperVToolkit.Core.Services;

// Native VHD metadata (Get-VHD parity) without PowerShell hosting.
// Backed by Msvm_ImageManagementService.GetVirtualHardDiskSettingData plus a
// file-size read. A single bad VHD never fails the batch — callers get null
// metadata and keep the Unknown/empty user-visible semantics.
public sealed record VhdMetadata(
    string Path,
    string VhdType,
    string VhdFormat,
    ulong SizeBytes,
    ulong? FileSizeBytes);

public interface IVhdInspector
{
    // Returns null when THIS disk cannot be inspected (missing file, access
    // denied, provider error). Never throws for a single path.
    Task<VhdMetadata?> InspectAsync(
        string node,
        string path,
        CancellationToken cancellationToken = default);
}
