using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Cluster;

// Pure port of Show-PiPlacementAdvisor (formulas frozen, thresholds frozen).
// Inputs are immutable snapshots: running VM base rows, node hardware rows,
// placement rows. Severity precedence is exact: DemandPct > 85 wins over
// AssignedPct > 90. Percentages are null when node RAM is unknown/zero
// (PowerShell "" parity). The trailing cluster-level Info row appears only
// when at least one placement row lacks preferred owners.
public static class PlacementAdvisorCalculator
{
    public static IReadOnlyList<PlacementAdviceRow> Build(
        IEnumerable<VirtualMachineRow> runningVmRows,
        IEnumerable<NodeHardwareRow> hardwareRows,
        IEnumerable<VmPlacementRow>? placementRows = null)
    {
        var vms = runningVmRows.ToList();
        var hwByNode = hardwareRows
            .GroupBy(h => h.Node, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<PlacementAdviceRow>();
        foreach (var group in vms.GroupBy(v => v.HostNode))
        {
            var node = group.Key;
            var items = group.ToList();
            hwByNode.TryGetValue(node, out var hw);

            var ramTotal = hw?.RAMGB ?? 0;
            var demand = Math.Round(items.Sum(v => v.DemandGB), 1);
            var assigned = Math.Round(items.Sum(v => v.AssignedGB), 1);
            double? demandPct = ramTotal > 0 ? Math.Round(demand / ramTotal * 100, 1) : null;
            double? assignedPct = ramTotal > 0 ? Math.Round(assigned / ramTotal * 100, 1) : null;

            var severity = "OK";
            var advice = PlacementAdviceKind.Balanced;
            if (demandPct.HasValue && demandPct.Value > Thresholds.PlacementDemandWarningPercent)
            {
                severity = "Warning";
                advice = PlacementAdviceKind.HighDemand;
            }
            else if (assignedPct.HasValue && assignedPct.Value > Thresholds.PlacementAssignedInfoPercent)
            {
                severity = "Info";
                advice = PlacementAdviceKind.HighAssigned;
            }

            rows.Add(new PlacementAdviceRow(
                severity,
                node,
                items.Count,
                items.Count == 0 ? null : items.Sum(v => v.CPU),
                hw?.RAMGB,
                hw?.FreeRAMGB,
                demand,
                demandPct,
                assigned,
                assignedPct,
                advice,
                0,
                hw?.TotalBytes,
                hw?.FreeBytes,
                items.Sum(v => v.DemandBytes ?? 0),
                items.Sum(v => v.AssignedBytes ?? 0)));
        }

        var placed = (placementRows ?? []).ToList();
        var withoutPreferred = placed.Count(p => p.PreferredOwners.Count == 0);
        if (withoutPreferred > 0)
        {
            rows.Add(new PlacementAdviceRow(
                "Info", "Cluster",
                null, null, null, null, null, null, null, null,
                PlacementAdviceKind.NoPreferredOwners,
                withoutPreferred));
        }

        return rows;
    }
}
