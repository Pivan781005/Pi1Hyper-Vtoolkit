namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Get-PiCSVRows: one row per SharedVolumeInfo partition.
// Path is FriendlyVolumeName. CSV Overview sorts by FreePercent ascending.
// Cluster-wide data; empty when the FailoverClusters capability is missing.
public sealed record CsvRow(
    string Name,
    string State,
    string OwnerNode,
    double SizeGB,
    double FreeGB,
    double UsedGB,
    double FreePercent,
    string Path,
    long SizeBytes = 0,
    long FreeBytes = 0,
    long UsedBytes = 0);
