using System.Text.RegularExpressions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Cluster;

// Pure port of Show-PiVMDistributionByOwner: placement rows grouped by
// OwnerNode; vCPU/RAM sums cover RUNNING VMs with HostNode == OwnerNode only.
// HighPriority uses PowerShell -match semantics on "High|3000"
// (case-insensitive substring/regex).
public static partial class VmDistributionCalculator
{
    [GeneratedRegex("High|3000", RegexOptions.IgnoreCase)]
    private static partial Regex HighPriorityRegex();

    public static IReadOnlyList<VmDistributionRow> Build(
        IEnumerable<VmPlacementRow> placementRows,
        IEnumerable<VirtualMachineRow> vmBaseRows)
    {
        var vms = vmBaseRows.ToList();
        var rows = new List<VmDistributionRow>();

        foreach (var group in placementRows.GroupBy(p => p.OwnerNode))
        {
            var node = group.Key;
            var owned = group.ToList();
            var running = vms
                .Where(v =>
                    string.Equals(v.HostNode, node, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase))
                .ToList();

            rows.Add(new VmDistributionRow(
                node,
                owned.Count,
                running.Count,
                running.Count == 0 ? null : running.Sum(v => v.CPU),
                Math.Round(running.Sum(v => v.AssignedGB), 1),
                Math.Round(running.Sum(v => v.DemandGB), 1),
                Math.Round(running.Sum(v => v.WasteGB), 1),
                owned.Count(g => !string.IsNullOrEmpty(g.Priority) && HighPriorityRegex().IsMatch(g.Priority)),
                running.Sum(v => v.AssignedBytes ?? 0),
                running.Sum(v => v.DemandBytes ?? 0),
                running.Sum(v => v.AssignedBytes ?? 0) - running.Sum(v => v.DemandBytes ?? 0)));
        }

        return rows;
    }
}
