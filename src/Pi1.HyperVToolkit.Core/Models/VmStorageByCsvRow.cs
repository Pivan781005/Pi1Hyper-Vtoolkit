namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVMStorageByCSV grouping. Empty CSV name becomes
// "(mimo CSV / nezistené)". Sorted by CSVFreePercent.
public sealed record VmStorageByCsvRow(
    string CSV,
    int VMCount,
    int DiskCount,
    double VHDSizeGB,
    double VHDFileGB,
    double? CSVFreeGB,
    double? CSVFreePercent,
    long VhdSizeBytes = 0,
    long VhdFileBytes = 0,
    long? CsvFreeBytes = null);
