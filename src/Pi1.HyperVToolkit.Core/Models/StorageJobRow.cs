namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Get-PiStorageJobRows (Pi1.Data.psm1): Get-StorageJob, LOCAL only.
// NOTE: MSFT_StorageJob has no JobType property in WMI (verified against the
// live Storage module schema); the PowerShell Select-Object of the missing
// property yields an empty value, so C# maps JobType to empty as well.
public sealed record StorageJobRow(
    string Name,
    string JobState,
    string JobType,
    int? PercentComplete,
    ulong? BytesProcessed,
    ulong? BytesTotal,
    TimeSpan? ElapsedTime)
{
    public string ElapsedText => ElapsedTime?.ToString() ?? "-";
}
