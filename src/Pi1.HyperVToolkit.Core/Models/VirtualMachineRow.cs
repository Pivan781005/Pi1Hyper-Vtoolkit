using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// One VM base row. Parity with Get-PiVMBaseRows (Pi1.Data.psm1).
/// A VM with N NICs yields N rows (VM data duplicated); a VM with zero NICs
/// yields one row with empty network fields.
/// Memory values are rounded to one decimal exactly like PowerShell
/// ([math]::Round(x,1)); missing values become 0 (Round($null) parity).
/// The *Bytes fields carry the raw provider precision for DISPLAY ONLY
/// (2-decimal MB/GB/TB); parity comparisons must keep using the GB fields.
/// </summary>
public sealed record VirtualMachineRow(
    string HostNode,
    string VM,
    string State,
    int CPU,
    double AssignedGB,
    double DemandGB,
    double WasteGB,
    string IPv4,
    string MAC,
    string Switch,
    TimeSpan? Uptime,
    bool Dynamic,
    double StartupGB,
    double MinimumGB,
    double MaximumGB,
    long? AssignedBytes = null,
    long? DemandBytes = null,
    long? WasteBytes = null,
    long? StartupBytes = null,
    long? MinimumBytes = null,
    long? MaximumBytes = null)
{
    /// <summary>Parity with Format-PiTimeSpan applied to $vm.Uptime.</summary>
    public string UptimeText => PiTimeFormatter.Format(Uptime);
}
