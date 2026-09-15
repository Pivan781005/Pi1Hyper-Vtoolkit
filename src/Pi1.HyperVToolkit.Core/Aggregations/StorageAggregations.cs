using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Aggregations;

// Pure storage aggregations. Parity with Pi1.Storage.psm1 / Pi1.Data.psm1.
// No WMI here: every method folds already-collected rows, so all rules are
// unit-testable without Hyper-V.
public static class StorageAggregations
{
    public const string OutsideCsvLabel = "(mimo CSV / nezistené)";

    /// <summary>
    /// Exact port of Get-PiCsvMatchForPath: null/empty path or null rows yield
    /// null; matching is a case-insensitive prefix test; the LONGEST matching
    /// CSV path wins.
    /// </summary>
    public static CsvRow? FindBestCsvMatch(string? path, IReadOnlyList<CsvRow>? csvRows)
    {
        if (string.IsNullOrWhiteSpace(path) || csvRows is null)
        {
            return null;
        }

        CsvRow? best = null;
        foreach (var csv in csvRows)
        {
            if (string.IsNullOrWhiteSpace(csv.Path))
            {
                continue;
            }

            if (path.StartsWith(csv.Path, StringComparison.OrdinalIgnoreCase))
            {
                if (best is null || csv.Path.Length > best.Path.Length)
                {
                    best = csv;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Exact port of Show-PiPhysicalDiskSummary: groups by MediaType, BusType
    /// (Group-Object naming joins with ", "), sorted by Group.
    /// </summary>
    public static IReadOnlyList<PhysicalDiskSummaryRow> SummarizePhysicalDisks(
        IEnumerable<PhysicalDiskRow> disks)
    {
        return disks
            .GroupBy(d => $"{d.MediaType}, {d.BusType}")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var items = g.ToList();
                return new PhysicalDiskSummaryRow(
                    g.Key,
                    items.Count,
                    Math.Round(items.Sum(d => d.SizeGB), 1),
                    items.Count(d => string.Equals(d.HealthStatus, "Healthy", StringComparison.Ordinal)),
                    items.Count(d => !string.Equals(d.HealthStatus, "Healthy", StringComparison.Ordinal)),
                    items.Count(d => d.CanPool == true),
                    items.Sum(d => d.SizeBytes));
            })
            .ToList();
    }

    /// <summary>
    /// Exact port of Show-PiStorageSummary: ALWAYS four rows in this order.
    /// CSV warns below 15 % (Thresholds.CsvWarningPercent, frozen).
    /// </summary>
    public static IReadOnlyList<StorageSummaryRow> BuildStorageSummary(
        IEnumerable<StoragePoolRow> pools,
        IEnumerable<VirtualDiskRow> virtualDisks,
        IEnumerable<CsvRow> csvRows,
        IEnumerable<StorageJobRow> jobs)
    {
        var poolList = pools.ToList();
        var vdiskList = virtualDisks.ToList();
        var csvList = csvRows.ToList();
        var jobList = jobs.ToList();

        return
        [
            new("Storage Pools", poolList.Count,
                poolList.Any(p => !string.Equals(p.HealthStatus, "Healthy", StringComparison.Ordinal)) ? "Warning" : "OK"),
            new("Virtual Disks", vdiskList.Count,
                vdiskList.Any(v => !string.Equals(v.HealthStatus, "Healthy", StringComparison.Ordinal)) ? "Warning" : "OK"),
            new("CSV", csvList.Count,
                csvList.Any(c => c.FreePercent < Thresholds.CsvWarningPercent) ? "Warning" : "OK"),
            new("Storage Jobs", jobList.Count,
                jobList.Count > 0 ? "Warning" : "None"),
        ];
    }

    /// <summary>
    /// Exact port of Show-PiVMStorageByCSV: groups by CSV (empty becomes the
    /// fixed Slovak label), VMCount counts DISTINCT VMs, GB sums round to one
    /// decimal, free values come from the first row carrying a free percent.
    /// Sorted by CSVFreePercent (nulls last, like PowerShell Sort-Object).
    /// </summary>
    public static IReadOnlyList<VmStorageByCsvRow> GroupVmStorageByCsv(
        IEnumerable<VmStorageRow> rows)
    {
        return rows
            .GroupBy(r => r.CSV ?? string.Empty)
            .Select(g =>
            {
                var items = g.ToList();
                var csvName = string.IsNullOrWhiteSpace(g.Key) ? OutsideCsvLabel : g.Key;
                var first = items.FirstOrDefault(i => i.CSVFreePercent.HasValue);
                return new VmStorageByCsvRow(
                    csvName,
                    items.Select(i => i.VM).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    items.Count,
                    Math.Round(items.Sum(i => i.VHDSizeGB ?? 0), 1),
                    Math.Round(items.Sum(i => i.VHDFileGB ?? 0), 1),
                    first?.CSVFreeGB,
                    first?.CSVFreePercent,
                    items.Sum(i => i.VhdSizeBytes ?? 0),
                    items.Sum(i => i.VhdFileBytes ?? 0),
                    first?.CsvFreeBytes);
            })
            // PowerShell Sort-Object puts the empty ("") free percent first.
            .OrderBy(r => r.CSVFreePercent.HasValue ? 1 : 0)
            .ThenBy(r => r.CSVFreePercent ?? 0)
            .ToList();
    }

    /// <summary>
    /// Parity with Show-PiVMStorageMap sorting: CSVFreePercent, HostNode, VM, Path.
    /// Rows without a CSV (empty percent in PowerShell) sort FIRST, exactly
    /// like PowerShell Sort-Object orders "" before numbers (verified).
    /// </summary>
    public static IReadOnlyList<VmStorageRow> SortStorageMap(IEnumerable<VmStorageRow> rows)
    {
        return rows
            .OrderBy(r => r.CSVFreePercent.HasValue ? 1 : 0)
            .ThenBy(r => r.CSVFreePercent ?? 0)
            .ThenBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Parity with Show-PiVMStorageForSelectedVM: filter by HostNode AND VM,
    /// sorted by Path.
    /// </summary>
    public static IReadOnlyList<VmStorageRow> FilterForVm(
        IEnumerable<VmStorageRow> rows, string hostNode, string vm)
    {
        return rows
            .Where(r =>
                string.Equals(r.HostNode, hostNode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.VM, vm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
