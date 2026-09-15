using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Calculations;

/// <summary>
/// Exact port of Show-PiNodeVMCapacity aggregation (Pi1.Nodes.psm1).
/// Running VMs only for vCPU/Assigned/Demand/Waste; OffVM counts State "Off"
/// exactly; percentages divide by node RAM and are null when RAM is unknown.
/// </summary>
public static class NodeCapacityCalculator
{
    public static NodeCapacityRow Compute(string node, IEnumerable<VirtualMachineRow> vmRows, NodeHardwareRow? hardware)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        var items = vmRows.Where(v => string.Equals(v.HostNode, node, StringComparison.OrdinalIgnoreCase)).ToList();
        var running = items.Where(v => string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase)).ToList();

        var assigned = Math.Round(running.Sum(v => v.AssignedGB), 1);
        var demand = Math.Round(running.Sum(v => v.DemandGB), 1);
        var waste = Math.Round(running.Sum(v => v.WasteGB), 1);
        var assignedBytes = running.Sum(v => v.AssignedBytes ?? 0);
        var demandBytes = running.Sum(v => v.DemandBytes ?? 0);
        var wasteBytes = assignedBytes - demandBytes;
        var ramTotal = hardware?.RAMGB ?? 0;

        return new NodeCapacityRow(
            Node: node,
            RunningVM: running.Count,
            OffVM: items.Count(v => string.Equals(v.State, "Off", StringComparison.OrdinalIgnoreCase)),
            VCpu: running.Count == 0 ? null : running.Sum(v => v.CPU),
            LogicalCPU: hardware?.LogicalCPU,
            RAMGB: hardware?.RAMGB,
            FreeRAMGB: hardware?.FreeRAMGB,
            RAMUsedPct: hardware?.RAMUsedPct,
            AssignedGB: assigned,
            DemandGB: demand,
            WasteGB: waste,
            AssignedPct: ramTotal > 0 ? Math.Round(assigned / ramTotal * 100, 1) : null,
            DemandPct: ramTotal > 0 ? Math.Round(demand / ramTotal * 100, 1) : null,
            AssignedBytes: assignedBytes,
            DemandBytes: demandBytes,
            WasteBytes: wasteBytes,
            TotalBytes: hardware?.TotalBytes,
            FreeBytes: hardware?.FreeBytes);
    }
}
