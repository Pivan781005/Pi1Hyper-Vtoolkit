namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// One local volume row. Parity with Show-PiNodeVolumes
/// (Win32_LogicalDisk, DriveType=3 only — no network drives).
/// </summary>
public sealed record NodeVolumeRow(
    string Node,
    string Drive,
    string Label,
    string FileSystem,
    double SizeGB,
    double FreeGB,
    double FreePercent,
    long SizeBytes = 0,
    long FreeBytes = 0);
