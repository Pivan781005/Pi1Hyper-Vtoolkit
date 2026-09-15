namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>Hyper-V host settings row. Parity with Get-PiVMHostSettingsRows.
/// The *Bytes fields carry raw precision for DISPLAY ONLY.</summary>
public sealed record HostSettingsRow(
    string Node,
    int? LogicalProcessorCount,
    double? MemoryCapacityGB,
    double? FreeRAMGB,
    double? RAMUsedPct,
    bool? NumaSpanningEnabled,
    bool? MigrationEnabled,
    int? MaxMigrations,
    bool? EnhancedSessionMode,
    string VirtualMachinePath,
    string VirtualHardDiskPath,
    long? MemoryCapacityBytes = null,
    long? FreeBytes = null);
