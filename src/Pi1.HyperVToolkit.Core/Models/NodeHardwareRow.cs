using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// One node hardware/OS row. Parity with Get-PiNodeHardwareRows.
/// Missing values are null (PowerShell renders them as empty strings;
/// views format null as empty).
/// The *Bytes fields carry raw provider precision for DISPLAY ONLY.
/// </summary>
public sealed record NodeHardwareRow(
    string Node,
    string Manufacturer,
    string Model,
    string OS,
    string Version,
    TimeSpan? Uptime,
    string CPUName,
    int Sockets,
    int? Cores,
    int? LogicalCPU,
    double? RAMGB,
    double? UsedRAMGB,
    double? FreeRAMGB,
    double? RAMUsedPct,
    long? TotalBytes = null,
    long? UsedBytes = null,
    long? FreeBytes = null)
{
    public string UptimeText => PiTimeFormatter.Format(Uptime);
}
