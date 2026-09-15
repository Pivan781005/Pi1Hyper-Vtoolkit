namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Get-PiVMStorageRows (scope-aware: one row per VM hard-disk drive).
// VHD fields are null when inspection fails (PowerShell silent-catch parity:
// VHDType/VHDFormat fall back to "Unknown", sizes stay empty).
public sealed record VmStorageRow(
    string HostNode,
    string VM,
    string State,
    string Controller,
    string VHDFormat,
    string VHDType,
    double? VHDSizeGB,
    double? VHDFileGB,
    string CSV,
    double? CSVFreeGB,
    double? CSVFreePercent,
    string Path,
    long? VhdSizeBytes = null,
    long? VhdFileBytes = null,
    long? CsvFreeBytes = null);
