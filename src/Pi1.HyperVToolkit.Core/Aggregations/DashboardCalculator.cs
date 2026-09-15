using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Aggregations;

// Pure dashboard aggregations. Exact ports of Show-PiDashboard,
// Show-PiNodeSummary (Pi1.Dashboard.psm1) and Show-PiVMSummary (Pi1.VM.psm1).
// Thresholds frozen: RAM warn >85, CSV warn <15, Waste high >8, negative <0.
public static class DashboardCalculator
{
    /// <summary>
    /// Minimal cluster snapshot for the dashboard. Nodes carry (Name, State)
    /// with State "Up" for online nodes; ClusterName is empty when unknown.
    /// </summary>
    public sealed record ClusterSnapshot(string ClusterName, IReadOnlyList<(string Name, string State)> Nodes);

    /// <summary>
    /// Exact port of Show-PiDashboard: ALWAYS 11 semantic rows in this order.
    /// Warnings is a CATEGORY count 0..4 (low CSV, negative RAM, high waste,
    /// storage jobs) — never an affected-object count.
    /// Area names stay canonical English (views localize); RAM Value strings
    /// render from raw byte sums via DataSize (2-decimal MB/GB/TB).
    /// </summary>
    public static IReadOnlyList<DashboardRow> BuildDashboard(
        ClusterSnapshot cluster,
        string scopeLabel,
        IEnumerable<VirtualMachineRow> vmRows,
        IEnumerable<NodeHardwareRow> hardwareRows,
        IEnumerable<CsvRow> csvRows,
        IEnumerable<StorageJobRow> jobs,
        string onlineSuffix = "Online")
    {
        var vms = vmRows.ToList();
        var hw = hardwareRows.ToList();
        var csvs = csvRows.ToList();
        var jobList = jobs.ToList();

        var runningVm = vms.Count(v => string.Equals(v.State, "Running", StringComparison.Ordinal));
        var offVm = vms.Count(v => string.Equals(v.State, "Off", StringComparison.Ordinal));
        var onlineNodes = cluster.Nodes.Count(n => string.Equals(n.State, "Up", StringComparison.Ordinal));
        var nodeCount = cluster.Nodes.Count;

        var totalRAM = Math.Round(hw.Sum(h => h.RAMGB ?? 0), 1);
        var freeRAM = Math.Round(hw.Sum(h => h.FreeRAMGB ?? 0), 1);
        var usedRAM = Math.Round(hw.Sum(h => h.UsedRAMGB ?? 0), 1);
        var usedPct = totalRAM > 0 ? Math.Round(usedRAM / totalRAM * 100, 1) : 0;

        var totalBytes = hw.Sum(h => h.TotalBytes ?? 0);
        var freeBytes = hw.Sum(h => h.FreeBytes ?? 0);
        var usedBytes = hw.Sum(h => h.UsedBytes ?? 0);

        var lowCsvCount = csvs.Count(c => c.FreePercent < Thresholds.CsvWarningPercent);
        var negativeRamCount = vms.Count(v =>
            string.Equals(v.State, "Running", StringComparison.Ordinal) && v.WasteGB < 0);
        var highWasteCount = vms.Count(v =>
            string.Equals(v.State, "Running", StringComparison.Ordinal) && v.WasteGB > Thresholds.WasteYellowGb);

        var warnings = 0;
        if (lowCsvCount > 0)
        {
            warnings++;
        }

        if (negativeRamCount > 0)
        {
            warnings++;
        }

        if (highWasteCount > 0)
        {
            warnings++;
        }

        if (jobList.Count > 0)
        {
            warnings++;
        }

        string NodesStatus() =>
            nodeCount > 0 && onlineNodes == nodeCount ? "OK"
            : nodeCount > 0 ? "Warning" : "N/A";

        return
        [
            new("Cluster", string.IsNullOrEmpty(cluster.ClusterName) ? "N/A" : "OK", cluster.ClusterName),
            new("Scope", "Info", scopeLabel),
            new("Nodes", NodesStatus(), $"{onlineNodes}/{nodeCount} {onlineSuffix}"),
            new("VM Running", "OK", runningVm.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("VM Off", "Info", offVm.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("RAM Total", "Info", DataSize.FormatBytes(totalBytes)),
            new("RAM Used", usedPct > Thresholds.RamUsedRedPercent ? "Warning" : "OK",
                $"{DataSize.FormatBytes(usedBytes)} ({FormatPct(usedPct)})"),
            new("RAM Free", "OK", DataSize.FormatBytes(freeBytes)),
            new("CSV", lowCsvCount > 0 ? "Warning" : "OK",
                csvs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Storage Jobs", jobList.Count > 0 ? "Warning" : "None",
                jobList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Warnings", warnings > 0 ? "Warning" : "OK",
                warnings.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// Exact port of Show-PiNodeSummary: one row per HostNode present in the
    /// VM rows; vCPU/RAM sums cover RUNNING VMs only; hardware comes from the
    /// join on Node. vCPU is null when nothing runs (Measure-Object $null
    /// parity); Assigned/Demand/Waste round an empty sum to 0.
    /// </summary>
    public static IReadOnlyList<NodeSummaryRow> BuildNodeSummary(
        IEnumerable<VirtualMachineRow> vmRows,
        IEnumerable<NodeHardwareRow> hardwareRows)
    {
        var hwByNode = hardwareRows
            .GroupBy(h => h.Node, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<NodeSummaryRow>();
        foreach (var group in vmRows.GroupBy(v => v.HostNode))
        {
            var items = group.ToList();
            var running = items
                .Where(v => string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase))
                .ToList();
            hwByNode.TryGetValue(group.Key, out var nodeHw);

            rows.Add(new NodeSummaryRow(
                group.Key,
                running.Count,
                items.Count(v => string.Equals(v.State, "Off", StringComparison.OrdinalIgnoreCase)),
                running.Count == 0 ? null : running.Sum(v => v.CPU),
                nodeHw?.LogicalCPU,
                nodeHw?.RAMGB,
                nodeHw?.FreeRAMGB,
                nodeHw?.RAMUsedPct,
                Math.Round(running.Sum(v => v.AssignedGB), 1),
                Math.Round(running.Sum(v => v.DemandGB), 1),
                Math.Round(running.Sum(v => v.WasteGB), 1),
                running.Sum(v => v.AssignedBytes ?? 0),
                running.Sum(v => v.DemandBytes ?? 0),
                running.Sum(v => v.AssignedBytes ?? 0) - running.Sum(v => v.DemandBytes ?? 0),
                nodeHw?.TotalBytes,
                nodeHw?.FreeBytes));
        }

        return rows;
    }

    /// <summary>
    /// Exact port of Show-PiVMSummary: ALWAYS six rows in this order; RAM
    /// aggregates cover RUNNING VMs only.
    /// </summary>
    public static IReadOnlyList<VmSummaryRow> BuildVmSummary(IEnumerable<VirtualMachineRow> vmRows)
    {
        var vms = vmRows.ToList();
        var running = vms.Where(v => string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase)).ToList();
        var off = vms.Where(v => string.Equals(v.State, "Off", StringComparison.OrdinalIgnoreCase)).ToList();

        var assignedBytes = running.Sum(v => v.AssignedBytes ?? 0);
        var demandBytes = running.Sum(v => v.DemandBytes ?? 0);

        return
        [
            new("Total VM", vms.Count),
            new("Running", running.Count),
            new("Off", off.Count),
            new("AssignedGB Running", Math.Round(running.Sum(v => v.AssignedGB), 1), assignedBytes),
            new("DemandGB Running", Math.Round(running.Sum(v => v.DemandGB), 1), demandBytes),
            new("WasteGB Running", Math.Round(running.Sum(v => v.WasteGB), 1), assignedBytes - demandBytes),
        ];
    }

    private static string FormatPct(double value) =>
        value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %";
}
