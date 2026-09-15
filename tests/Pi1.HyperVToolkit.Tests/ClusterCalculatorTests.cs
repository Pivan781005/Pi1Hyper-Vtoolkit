using Pi1.HyperVToolkit.Core.Cluster;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ClusterCalculatorTests
{
    private static VirtualMachineRow Vm(string host, string state, int cpu = 2,
        double assigned = 4, double demand = 3) =>
        new(host, "VM-" + host + "-" + Guid.NewGuid().ToString("N")[..4], state, cpu,
            assigned, demand, assigned - demand, "", "", "", null, false, 4, 1, 8);

    private static VmPlacementRow Placement(string group, string owner, string priority = "Medium",
        string preferred = "", string possible = "") =>
        new(group, "Online", owner, priority,
            string.IsNullOrEmpty(preferred) ? [] : preferred.Split(", "),
            string.IsNullOrEmpty(possible) ? [] : possible.Split(", "),
            "", "", "");

    // ---- Distribution ----

    [Fact]
    public void Distribution_GroupsByOwner_RunningOnlySums()
    {
        var placement = new[]
        {
            Placement("G1", "N1", "High"), Placement("G2", "N1", "Low"), Placement("G3", "N2", "3000"),
        };
        var vms = new[]
        {
            Vm("N1", "Running", cpu: 2, assigned: 4, demand: 3),
            Vm("N1", "Running", cpu: 4, assigned: 8, demand: 6),
            Vm("N1", "Off", cpu: 16, assigned: 64, demand: 64),
            Vm("N2", "Running", cpu: 1, assigned: 2, demand: 1),
        };
        var rows = VmDistributionCalculator.Build(placement, vms);
        Assert.Equal(2, rows.Count);

        var n1 = Assert.Single(rows, r => r.OwnerNode == "N1");
        Assert.Equal(2, n1.VMGroups);
        Assert.Equal(2, n1.RunningVM);
        Assert.Equal(6, n1.VCpu);
        Assert.Equal(12, n1.AssignedGB);
        Assert.Equal(9, n1.DemandGB);
        Assert.Equal(3, n1.WasteGB);
        Assert.Equal(1, n1.HighPriority); // only "High", not "Low".

        var n2 = Assert.Single(rows, r => r.OwnerNode == "N2");
        Assert.Equal(1, n2.VMGroups);
        Assert.Equal(1, n2.RunningVM);
        Assert.Equal(1, n2.HighPriority); // numeric "3000" form counts.
    }

    [Fact]
    public void Distribution_NoRunningVms_NullVCpuZeroSums()
    {
        var rows = VmDistributionCalculator.Build(
            [Placement("G1", "N9")], [Vm("N9", "Off")]);
        var row = Assert.Single(rows);
        Assert.Equal(0, row.RunningVM);
        Assert.Null(row.VCpu);
        Assert.Equal(0, row.AssignedGB);
    }

    [Fact]
    public void Distribution_NonVmGroup_StillCountsWhenPlaced()
    {
        // Grouping is purely by OwnerNode; VM-ness was filtered upstream.
        var rows = VmDistributionCalculator.Build([Placement("G1", "N1")], []);
        Assert.Single(rows);
    }

    // ---- Placement Advisor ----

    private static NodeHardwareRow Hw(string node, double ram) =>
        new(node, "", "", "", "", null, "", 0, null, null, ram, null, null, null);

    [Fact]
    public void Advisor_BalancedNode_OK()
    {
        var rows = PlacementAdvisorCalculator.Build(
            [Vm("N1", "Running", assigned: 40, demand: 30)], [Hw("N1", 100)], []);
        var row = Assert.Single(rows);
        Assert.Equal("OK", row.Severity);
        Assert.Equal(PlacementAdviceKind.Balanced, row.Advice);
        Assert.Equal(30.0, row.DemandPct);
        Assert.Equal(40.0, row.AssignedPct);
    }

    [Theory]
    [InlineData(85.0, "OK")]
    [InlineData(85.1, "Warning")]
    public void Advisor_DemandBoundary(double demand, string expected)
    {
        var rows = PlacementAdvisorCalculator.Build(
            [Vm("N1", "Running", assigned: 10, demand: demand)], [Hw("N1", 100)], []);
        var row = Assert.Single(rows);
        Assert.Equal(expected, row.Severity);
        if (expected == "Warning")
        {
            Assert.Equal(PlacementAdviceKind.HighDemand, row.Advice);
        }
    }

    [Theory]
    [InlineData(90.0, "OK")]
    [InlineData(90.1, "Info")]
    public void Advisor_AssignedBoundary(double assigned, string expected)
    {
        var rows = PlacementAdvisorCalculator.Build(
            [Vm("N1", "Running", assigned: assigned, demand: 10)], [Hw("N1", 100)], []);
        var row = Assert.Single(rows);
        Assert.Equal(expected, row.Severity);
        if (expected == "Info")
        {
            Assert.Equal(PlacementAdviceKind.HighAssigned, row.Advice);
        }
    }

    [Fact]
    public void Advisor_DemandWinsOverAssigned()
    {
        var rows = PlacementAdvisorCalculator.Build(
            [Vm("N1", "Running", assigned: 95, demand: 90)], [Hw("N1", 100)], []);
        var row = Assert.Single(rows);
        Assert.Equal("Warning", row.Severity);
        Assert.Equal(PlacementAdviceKind.HighDemand, row.Advice);
    }

    [Fact]
    public void Advisor_UnknownRam_NullPcts_Ok()
    {
        var rows = PlacementAdvisorCalculator.Build([Vm("N9", "Running")], []);
        var row = Assert.Single(rows);
        Assert.Null(row.DemandPct);
        Assert.Null(row.AssignedPct);
        Assert.Equal("OK", row.Severity);
    }

    [Fact]
    public void Advisor_MissingPreferredOwners_ClusterInfoRow()
    {
        var placement = new[]
        {
            Placement("G1", "N1", preferred: "N1, N2"),
            Placement("G2", "N1"),
            Placement("G3", "N2"),
        };
        var rows = PlacementAdvisorCalculator.Build([Vm("N1", "Running")], [Hw("N1", 100)], placement);
        Assert.Equal(2, rows.Count);
        var info = Assert.Single(rows, r => r.Node == "Cluster");
        Assert.Equal("Info", info.Severity);
        Assert.Equal(PlacementAdviceKind.NoPreferredOwners, info.Advice);
        Assert.Equal(2, info.AdviceCount);
        Assert.Null(info.DemandGB);
    }

    [Fact]
    public void Advisor_NoPlacement_AllPreferred_NoClusterRow()
    {
        var rows = PlacementAdvisorCalculator.Build(
            [Vm("N1", "Running")], [Hw("N1", 100)], [Placement("G1", "N1", preferred: "N1")]);
        Assert.Single(rows);
    }

    // ---- Failover Simulation ----

    [Fact]
    public void Failover_ViableTarget_OK()
    {
        var result = FailoverSimulator.Simulate(
            "N1", ["N2"],
            [Vm("N1", "Running", assigned: 10, demand: 8), Vm("N2", "Running", assigned: 10, demand: 10)],
            [Hw("N1", 100), Hw("N2", 100)]);
        var row = Assert.Single(result.TargetRows);
        Assert.Equal("N1", row.FailedNode);
        Assert.Equal("N2", row.TargetNode);
        Assert.Equal(1, row.VMsToMove);
        Assert.Equal(8, row.MoveDemandGB);
        Assert.Equal(18.0, row.AfterDemandGB);
        Assert.Equal(18.0, row.AfterDemandPct);
        Assert.Equal("OK", row.Status);
        Assert.Equal(FailoverAdviceKind.Viable, row.Advice);
        Assert.Single(result.MoveVms);
    }

    [Fact]
    public void Failover_CriticalBoundary()
    {
        // afterDemandPct == 95.1 -> Critical; exactly 95.0 -> Warning.
        var critical = FailoverSimulator.Simulate(
            "N1", ["N2"], [Vm("N1", "Running", demand: 95.1)], [Hw("N2", 100)]);
        Assert.Equal("Critical", Assert.Single(critical.TargetRows).Status);

        var warning = FailoverSimulator.Simulate(
            "N1", ["N2"], [Vm("N1", "Running", demand: 95.0)], [Hw("N2", 100)]);
        var warningRow = Assert.Single(warning.TargetRows);
        Assert.Equal("Warning", warningRow.Status);
        Assert.Equal(FailoverAdviceKind.Tight, warningRow.Advice);
    }

    [Fact]
    public void Failover_AssignedHigh_Info()
    {
        // afterAssignedPct == 96 (> 95) with demand low: Info, not Warning.
        var result = FailoverSimulator.Simulate(
            "N1", ["N2"],
            [Vm("N1", "Running", assigned: 96, demand: 10)],
            [Hw("N1", 100), Hw("N2", 100)]);
        var row = Assert.Single(result.TargetRows);
        Assert.Equal(10.0, row.AfterDemandPct); // demand low: assigned band decides
        Assert.Equal("Info", row.Status);
        Assert.Equal(FailoverAdviceKind.AssignedHigh, row.Advice);
    }

    [Fact]
    public void Failover_SourceWithoutRunningVms_ZeroMove()
    {
        var result = FailoverSimulator.Simulate(
            "N1", ["N2"], [Vm("N1", "Off")], [Hw("N1", 100), Hw("N2", 100)]);
        var row = Assert.Single(result.TargetRows);
        Assert.Equal(0, row.VMsToMove);
        Assert.Equal(0, row.MoveDemandGB);
        Assert.Null(row.MoveVCpu);
        Assert.Equal("OK", row.Status);
        Assert.Empty(result.MoveVms);
    }

    [Fact]
    public void Failover_UnknownTargetRam_NullPcts()
    {
        var result = FailoverSimulator.Simulate(
            "N1", ["N9"], [Vm("N1", "Running", demand: 5)], [Hw("N1", 100)]);
        var row = Assert.Single(result.TargetRows);
        Assert.Equal(0, row.TargetRAMGB);
        Assert.Null(row.AfterDemandPct);
        Assert.Null(row.FreeAfterDemandGB);
        Assert.Equal("OK", row.Status);
    }

    [Fact]
    public void Failover_ExcludesFailedNode_SkipsSelf()
    {
        var result = FailoverSimulator.Simulate(
            "N1", ["N1", "N2"], [Vm("N1", "Running")], [Hw("N1", 100), Hw("N2", 100)]);
        Assert.Single(result.TargetRows);
        Assert.Equal("N2", result.TargetRows[0].TargetNode);
    }

    // ---- Health Score ----

    private static ClusterNodeRow Node(string name, string state) =>
        new(name, state, "", "1", "");

    private static ClusterResourceRow Resource(string name, string state) =>
        new(name, state, "G", "Virtual Machine", "N1");

    private static CsvRow Csv(double pct) =>
        new("CSV1", "Online", "N1", 100, pct, 100 - pct, pct, "C:\\CSV1");

    private static StorageJobRow Job() => new("J", "Running", "", 1, 1, 1, null);

    private static QuorumInfo Quorum() => new("Node Majority", "");

    private (List<ClusterNodeRow> Nodes, List<ClusterResourceRow> Resources) HealthyBase() =>
        ([Node("N1", "Up"), Node("N2", "Up")], [Resource("R1", "Online")]);

    [Fact]
    public void HealthScore_PerfectCluster_100()
    {
        var (nodes, resources) = HealthyBase();
        var result = HealthScoreCalculator.Evaluate(
            nodes, resources, [Csv(50)], [], [Quorum()], []);
        Assert.Equal(100, result.Score);
        Assert.Equal(6, result.Checks.Count);
        Assert.All(result.Checks, c => Assert.Equal("OK", c.Status));
    }

    [Fact]
    public void HealthScore_NodesDown_Minus30_Critical()
    {
        var (nodes, resources) = HealthyBase();
        nodes.Add(Node("N3", "Down"));
        var result = HealthScoreCalculator.Evaluate(
            nodes, resources, [Csv(50)], [], [Quorum()], []);
        Assert.Equal(70, result.Score);
        var check = Assert.Single(result.Checks, c => c.Area == "Nodes");
        Assert.Equal("Critical", check.Status);
        Assert.Equal("2/3 Up", check.Detail);
    }

    [Fact]
    public void HealthScore_AllDeductions_Cumulative_Floor()
    {
        var result = HealthScoreCalculator.Evaluate(
            [Node("N1", "Down")],
            [Resource("R1", "Failed")],
            [Csv(5)],
            [Job()],
            [],
            [Vm("N1", "Running", assigned: 4, demand: 5)]);
        // 100 - 30 - 20 - 15 - 10 - 10 - 5 = 10.
        Assert.Equal(10, result.Score);
        Assert.Equal("Warning", Assert.Single(result.Checks, c => c.Area == "Resources").Status);
        Assert.Equal("1 CSV below 15%", Assert.Single(result.Checks, c => c.Area == "CSV Capacity").Detail);
        Assert.Equal("1 active/listed", Assert.Single(result.Checks, c => c.Area == "Storage Jobs").Detail);
        Assert.Equal("Unknown", Assert.Single(result.Checks, c => c.Area == "Quorum/Witness").Detail);
        Assert.Equal("Info", Assert.Single(result.Checks, c => c.Area == "VM Memory Pressure").Status);
    }

    [Fact]
    public void HealthScore_DeductionsApplyOncePerCategory()
    {
        // Three down nodes still deduct only 30 (not 90); two bad resources
        // still deduct only 20. Minimum achievable total is therefore 10,
        // and the <0 floor guard stays as defensive parity.
        var result = HealthScoreCalculator.Evaluate(
            [Node("N1", "Down"), Node("N2", "Down"), Node("N3", "Down")],
            [Resource("R1", "Failed"), Resource("R2", "Pending")],
            [Csv(1), Csv(2)],
            [Job(), Job()],
            [],
            [Vm("N1", "Running", assigned: 1, demand: 2), Vm("N2", "Running", assigned: 1, demand: 2)]);
        Assert.Equal(10, result.Score);
        Assert.True(result.Score >= 0);
    }

    [Fact]
    public void HealthScore_OfflineResources_NotBad()
    {
        var (nodes, _) = HealthyBase();
        var result = HealthScoreCalculator.Evaluate(
            nodes, [Resource("R1", "Offline")], [Csv(50)], [], [Quorum()], []);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void HealthScore_CsvBoundary()
    {
        var (nodes, resources) = HealthyBase();
        var ok = HealthScoreCalculator.Evaluate(nodes, resources, [Csv(15.0)], [], [Quorum()], []);
        Assert.Equal(100, ok.Score);
        var warn = HealthScoreCalculator.Evaluate(nodes, resources, [Csv(14.9)], [], [Quorum()], []);
        Assert.Equal(85, warn.Score);
    }

    [Fact]
    public void HealthScore_QuorumTypeShownWhenPresent()
    {
        var (nodes, resources) = HealthyBase();
        var result = HealthScoreCalculator.Evaluate(
            nodes, resources, [Csv(50)], [], [new QuorumInfo("Node and Disk Majority", "Disk Q")], []);
        Assert.Equal("Node and Disk Majority", Assert.Single(result.Checks, c => c.Area == "Quorum/Witness").Detail);
    }
}
