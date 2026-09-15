namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVolumes: Get-Volume WHERE DriveType == Fixed.
// Sorted by DriveLetter, FileSystemLabel. LOCAL semantics.
// Distinct from NodeVolumeRow (Win32_LogicalDisk inventory on the Nodes page).
// UniqueId/Path are test-only stable identity (duplicate display keys exist);
// views do not display them and exports must not add them for the test.
public sealed record StorageVolumeRow(
    string DriveLetter,
    string FileSystemLabel,
    string FileSystem,
    string HealthStatus,
    string OperationalStatus,
    double SizeGB,
    double FreeGB,
    double FreePercent,
    string UniqueId = "",
    string Path = "",
    long SizeBytes = 0,
    long FreeBytes = 0);
