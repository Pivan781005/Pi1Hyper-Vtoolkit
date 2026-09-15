using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Filtering;

/// <summary>
/// Pure VM search/filter helpers. Parity with Find-PiVMByName/ByIP/ByMac and
/// the Show-PiVMList state filter: PowerShell -like "*x*" is case-insensitive
/// substring matching.
/// </summary>
public static class VmRowFilters
{
    public static IReadOnlyList<VirtualMachineRow> ByState(IEnumerable<VirtualMachineRow> rows, string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return rows.ToList();
        }

        return rows.Where(r => string.Equals(r.State, state, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static IReadOnlyList<VirtualMachineRow> ByName(IEnumerable<VirtualMachineRow> rows, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return rows.ToList();
        }

        return rows.Where(r => r.VM.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Searches IPv4 and AllIPs text, like the PowerShell IP search.</summary>
    public static IReadOnlyList<VmNetworkRow> ByIp(IEnumerable<VmNetworkRow> rows, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return rows.ToList();
        }

        return rows.Where(r =>
            r.IPv4.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            r.AllIPs.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// MAC search with separator normalization (both sides), so
    /// 00-15-5D-AA-BB-CC, 00:15:5D:AA:BB:CC and 00155DAABBCC all match.
    /// </summary>
    public static IReadOnlyList<VmNetworkRow> ByMac(IEnumerable<VmNetworkRow> rows, string? text)
    {
        var normalized = NetworkText.NormalizeMac(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return rows.ToList();
        }

        return rows.Where(r =>
            NetworkText.NormalizeMac(r.MacAddress).Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>VMs without any guest IP visible to Hyper-V (empty AllIPs).</summary>
    public static IReadOnlyList<VmNetworkRow> WithoutIp(IEnumerable<VmNetworkRow> rows)
    {
        return rows.Where(r => string.IsNullOrWhiteSpace(r.AllIPs)).ToList();
    }
}
