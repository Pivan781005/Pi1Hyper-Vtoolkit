using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Cluster;

// Pure port of Show-PiFailoverSimulation. STRICTLY a calculation over
// immutable snapshots (placement-independent inputs: explicit failed node,
// explicit target nodes, VM base rows, node hardware rows) — never a real
// failover, never a live query (the reference re-query defect is fixed by
// taking snapshots as parameters). Threshold precedence is exact:
// Demand>95 Critical, else Demand>85 Warning, else Assigned>95 Info.
public static class FailoverSimulator
{
    public static FailoverResult Simulate(
        string failedNode,
        IEnumerable<string> targetNodes,
        IEnumerable<VirtualMachineRow> vmBaseRows,
        IEnumerable<NodeHardwareRow> hardwareRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedNode);

        var vms = vmBaseRows.ToList();
        var hwByNode = hardwareRows
            .GroupBy(h => h.Node, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var failedRunning = vms
            .Where(v =>
                string.Equals(v.HostNode, failedNode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var neededDemand = Math.Round(failedRunning.Sum(v => v.DemandGB), 1);
        var neededAssigned = Math.Round(failedRunning.Sum(v => v.AssignedGB), 1);
        int? neededVcpu = failedRunning.Count == 0 ? null : failedRunning.Sum(v => v.CPU);
        var neededDemandBytes = failedRunning.Sum(v => v.DemandBytes ?? 0);
        var neededAssignedBytes = failedRunning.Sum(v => v.AssignedBytes ?? 0);

        var rows = new List<FailoverRow>();
        foreach (var target in targetNodes.Where(t => !string.Equals(t, failedNode, StringComparison.OrdinalIgnoreCase)))
        {
            hwByNode.TryGetValue(target, out var targetHw);
            var targetRunning = vms
                .Where(v =>
                    string.Equals(v.HostNode, target, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targetDemand = Math.Round(targetRunning.Sum(v => v.DemandGB), 1);
            var targetAssigned = Math.Round(targetRunning.Sum(v => v.AssignedGB), 1);
            var targetRam = targetHw?.RAMGB ?? 0;
            var targetFree = targetHw?.FreeRAMGB ?? 0;
            var targetRamBytes = targetHw?.TotalBytes ?? 0;
            var targetFreeBytes = targetHw?.FreeBytes ?? 0;
            var targetDemandBytes = targetRunning.Sum(v => v.DemandBytes ?? 0);
            var targetAssignedBytes = targetRunning.Sum(v => v.AssignedBytes ?? 0);

            var afterDemand = Math.Round(targetDemand + neededDemand, 1);
            var afterAssigned = Math.Round(targetAssigned + neededAssigned, 1);
            double? afterDemandPct = targetRam > 0 ? Math.Round(afterDemand / targetRam * 100, 1) : null;
            double? afterAssignedPct = targetRam > 0 ? Math.Round(afterAssigned / targetRam * 100, 1) : null;
            double? freeAfter = targetRam > 0 ? Math.Round(targetRam - afterDemand, 1) : null;

            var status = "OK";
            var advice = FailoverAdviceKind.Viable;
            if (targetRam > 0 && afterDemandPct > Thresholds.FailoverDemandCriticalPercent)
            {
                status = "Critical";
                advice = FailoverAdviceKind.Critical;
            }
            else if (targetRam > 0 && afterDemandPct > Thresholds.FailoverDemandWarningPercent)
            {
                status = "Warning";
                advice = FailoverAdviceKind.Tight;
            }
            else if (targetRam > 0 && afterAssignedPct > Thresholds.FailoverAssignedInfoPercent)
            {
                status = "Info";
                advice = FailoverAdviceKind.AssignedHigh;
            }

            rows.Add(new FailoverRow(
                failedNode,
                target,
                failedRunning.Count,
                neededDemand,
                neededAssigned,
                neededVcpu,
                targetRam,
                targetFree,
                afterDemand,
                afterDemandPct,
                freeAfter,
                status,
                advice,
                neededDemandBytes,
                neededAssignedBytes,
                targetRamBytes,
                targetFreeBytes,
                targetDemandBytes + neededDemandBytes,
                targetRam > 0 ? targetRamBytes - (targetDemandBytes + neededDemandBytes) : null));
        }

        return new FailoverResult(rows, failedRunning);
    }
}
