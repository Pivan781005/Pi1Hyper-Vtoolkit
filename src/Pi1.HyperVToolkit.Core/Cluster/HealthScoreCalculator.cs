using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Core.Cluster;

// Pure port of Show-PiClusterHealthScore. EVERY deduction frozen:
// nodes not-Up -30, bad resources -20, low CSV -15, jobs -10, no quorum -10,
// negative-waste running VMs -5; floor 0. Check rows keep exact reference
// Detail text for direct parity. No backend calls.
public static class HealthScoreCalculator
{
    public static HealthScoreResult Evaluate(
        IEnumerable<ClusterNodeRow> nodes,
        IEnumerable<ClusterResourceRow> resources,
        IEnumerable<CsvRow> csvRows,
        IEnumerable<StorageJobRow> jobs,
        IReadOnlyList<QuorumInfo> quorumRows,
        IEnumerable<VirtualMachineRow> runningVmRows)
    {
        var nodeList = nodes.ToList();
        var resourceList = resources.ToList();
        var csvList = csvRows.ToList();
        var jobList = jobs.ToList();
        var quorumList = quorumRows.ToList();

        var score = Thresholds.ScoreStart;
        var checks = new List<HealthCheckRow>();

        var downNodes = nodeList
            .Where(n => !string.Equals(n.State, "Up", StringComparison.Ordinal))
            .ToList();
        if (downNodes.Count > 0)
        {
            score -= Thresholds.ScoreNodesDownDeduction;
        }

        var upCount = nodeList.Count(n => string.Equals(n.State, "Up", StringComparison.Ordinal));
        checks.Add(new HealthCheckRow(
            "Nodes",
            downNodes.Count == 0 ? "OK" : "Critical",
            $"{upCount}/{nodeList.Count} Up"));

        var badResources = resourceList
            .Where(r =>
                !string.Equals(r.State, "Online", StringComparison.Ordinal) &&
                !string.Equals(r.State, "Offline", StringComparison.Ordinal))
            .ToList();
        if (badResources.Count > 0)
        {
            score -= Thresholds.ScoreBadResourcesDeduction;
        }

        checks.Add(new HealthCheckRow(
            "Resources",
            badResources.Count == 0 ? "OK" : "Warning",
            $"{badResources.Count} not OK/pending/failed"));

        var lowCsv = csvList.Where(c => c.FreePercent < Thresholds.CsvWarningPercent).ToList();
        if (lowCsv.Count > 0)
        {
            score -= Thresholds.ScoreLowCsvDeduction;
        }

        checks.Add(new HealthCheckRow(
            "CSV Capacity",
            lowCsv.Count == 0 ? "OK" : "Warning",
            $"{lowCsv.Count} CSV below 15%"));

        if (jobList.Count > 0)
        {
            score -= Thresholds.ScoreStorageJobsDeduction;
        }

        checks.Add(new HealthCheckRow(
            "Storage Jobs",
            jobList.Count == 0 ? "OK" : "Warning",
            $"{jobList.Count} active/listed"));

        // The deduction keys off the quorum QUERY ROW (Invoke-PiSafe success),
        // not value emptiness: a present-but-empty type still counts as OK.
        var hasQuorum = quorumList.Count > 0;
        if (!hasQuorum)
        {
            score -= Thresholds.ScoreNoQuorumDeduction;
        }

        // Reference quirk preserved: Detail shows the quorum TYPE even though
        // the deduction triggers on a missing quorum ROW.
        checks.Add(new HealthCheckRow(
            "Quorum/Witness",
            hasQuorum ? "OK" : "Warning",
            hasQuorum ? quorumList[0].QuorumType : "Unknown"));

        var negative = runningVmRows
            .Where(v => string.Equals(v.State, "Running", StringComparison.Ordinal) && v.WasteGB < 0)
            .ToList();
        if (negative.Count > 0)
        {
            score -= Thresholds.ScoreMemoryPressureDeduction;
        }

        checks.Add(new HealthCheckRow(
            "VM Memory Pressure",
            negative.Count == 0 ? "OK" : "Info",
            $"{negative.Count} VM with Demand > Assigned"));

        if (score < 0)
        {
            score = 0;
        }

        return new HealthScoreResult(score, checks);
    }
}
