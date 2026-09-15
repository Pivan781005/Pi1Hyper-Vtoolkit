using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using static Pi1.HyperVToolkit.Core.Aggregations.DashboardCalculator;

namespace Pi1.HyperVToolkit.Tests;

public sealed class DashboardCalculatorTests
{
    private static VirtualMachineRow Vm(string host, string name, string state, double waste = 1, int cpu = 2,
        double assigned = 4, double demand = 3) =>
        new(host, name, state, cpu, assigned, demand, waste, "", "", "", null, false, 4, 1, 8,
            Gb(assigned), Gb(demand), Gb(waste), Gb(4), Gb(1), Gb(8));

    private static NodeHardwareRow Hw(string node, double ram = 32, double used = 16, double free = 16,
        double pct = 50, int logical = 8) =>
        new(node, "M", "M", "OS", "1.0", null, "CPU", 1, 4, logical, ram, used, free, pct,
            Gb(ram), Gb(used), Gb(free));

    private static long Gb(double gigabytes) => (long)(gigabytes * 1024 * 1024 * 1024);

    private static CsvRow Csv(string name, double pct) =>
        new(name, "Online", "N1", 100, pct, 100 - pct, pct, $"C:\\{name}");

    private static StorageJobRow Job(string name) => new(name, "Running", "", 10, 1, 2, null);

    private static ClusterSnapshot Cluster(string name, params (string, string)[] nodes) =>
        new(name, nodes);

    [Fact]
    public void Dashboard_ExactlyElevenRows_InOrder()
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("CLU", ("N1", "Up")), "Lokálny uzol", [], [], [], []);
        Assert.Equal(
            ["Cluster", "Scope", "Nodes", "VM Running", "VM Off", "RAM Total",
             "RAM Used", "RAM Free", "CSV", "Storage Jobs", "Warnings"],
            rows.Select(r => r.Area));
    }

    [Fact]
    public void Dashboard_AllOk_NoWarnings()
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("CLU", ("N1", "Up")),
            "Lokálny uzol",
            [Vm("N1", "A", "Running"), Vm("N1", "B", "Off")],
            [Hw("N1")],
            [Csv("C1", 50)],
            []);
        Assert.Equal("OK", rows[0].Status); // Cluster
        Assert.Equal("CLU", rows[0].Value);
        Assert.Equal("Info", rows[1].Status); // Scope
        Assert.Equal("Lokálny uzol", rows[1].Value);
        Assert.Equal("OK", rows[2].Status); // Nodes 1/1
        Assert.Equal("1/1 Online", rows[2].Value);
        Assert.Equal("1", rows[3].Value); // Running
        Assert.Equal("OK", rows[3].Status);
        Assert.Equal("1", rows[4].Value); // Off
        Assert.Equal("Info", rows[4].Status);
        Assert.Equal("32.00 GB", rows[5].Value);
        Assert.Equal("16.00 GB (50.0 %)", rows[6].Value);
        Assert.Equal("OK", rows[6].Status);
        Assert.Equal("16.00 GB", rows[7].Value);
        Assert.Equal("OK", rows[8].Status); // CSV
        Assert.Equal("None", rows[9].Status); // no jobs
        Assert.Equal("0", rows[9].Value);
        Assert.Equal("OK", rows[10].Status); // Warnings
        Assert.Equal("0", rows[10].Value);
    }

    [Fact]
    public void Dashboard_WarningsAreCategoryCount_MaxFour()
    {
        // 10 low CSVs + 10 high-waste VMs + 3 negative + 5 jobs => still 4.
        var vms = Enumerable.Range(0, 10).Select(i => Vm("N1", $"H{i}", "Running", waste: 9))
            .Concat(Enumerable.Range(0, 3).Select(i => Vm("N1", $"N{i}", "Running", waste: -1)))
            .ToList();
        var csvs = Enumerable.Range(0, 10).Select(i => Csv($"C{i}", 5)).ToList();
        var jobs = Enumerable.Range(0, 5).Select(i => Job($"J{i}")).ToList();
        var rows = DashboardCalculator.BuildDashboard(Cluster("C", ("N1", "Up")), "S", vms, [Hw("N1")], csvs, jobs);
        Assert.Equal("4", rows[10].Value);
        Assert.Equal("Warning", rows[10].Status);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, "0")]
    [InlineData(1, 0, 0, 0, "1")]
    [InlineData(0, 1, 0, 0, "1")]
    [InlineData(0, 0, 1, 0, "1")]
    [InlineData(0, 0, 0, 1, "1")]
    [InlineData(1, 1, 1, 1, "4")]
    public void Dashboard_WarningCategories_AddUp(int csv, int neg, int waste, int jobs, string expected)
    {
        var vms = new List<VirtualMachineRow>();
        if (neg > 0)
        {
            vms.Add(Vm("N1", "NEG", "Running", waste: -0.5));
        }

        if (waste > 0)
        {
            vms.Add(Vm("N1", "BIG", "Running", waste: 9));
        }

        var csvs = csv > 0 ? new[] { Csv("C", 5) } : Array.Empty<CsvRow>();
        var jobList = jobs > 0 ? new[] { Job("J") } : Array.Empty<StorageJobRow>();
        var rows = DashboardCalculator.BuildDashboard(Cluster("C", ("N1", "Up")), "S", vms, [Hw("N1")], csvs, jobList);
        Assert.Equal(expected, rows[10].Value);
    }

    [Fact]
    public void Dashboard_RamThreshold_Boundary()
    {
        var ok = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [], [Hw("N1", ram: 100, used: 85, free: 15, pct: 85)], [], []);
        Assert.Equal("OK", ok[6].Status); // ==85 is OK, only >85 warns.
        Assert.Equal("16.00 GB (50.0 %)", DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [], [Hw("N1")], [], [])[6].Value);

        var warn = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [], [Hw("N1", ram: 100, used: 85.1, free: 14.9, pct: 85.1)], [], []);
        Assert.Equal("Warning", warn[6].Status);
    }

    [Theory]
    [InlineData(14.9, "Warning")]
    [InlineData(15.0, "OK")]
    public void Dashboard_CsvThreshold_Boundary(double pct, string expected)
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [], [], [Csv("C", pct)], []);
        Assert.Equal(expected, rows[8].Status);
        Assert.Equal(expected == "Warning" ? "Warning" : "OK", rows[10].Status switch { "Warning" => "Warning", _ => "OK" });
    }

    [Theory]
    [InlineData(8.0, 0)] // exactly 8 is NOT high waste.
    [InlineData(8.1, 1)]
    [InlineData(0.0, 0)] // zero is not negative.
    [InlineData(-0.1, 1)]
    public void Dashboard_WasteBoundaries(double waste, int expectedWarnings)
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [Vm("N1", "V", "Running", waste: waste)], [Hw("N1")], [], []);
        Assert.Equal(expectedWarnings.ToString(), rows[10].Value);
    }

    [Fact]
    public void Dashboard_OffVmWaste_Ignored()
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [Vm("N1", "V", "Off", waste: 99)], [Hw("N1")], [], []);
        Assert.Equal("0", rows[10].Value);
    }

    [Fact]
    public void Dashboard_Nodes_MixedStates_Warning()
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("C", ("N1", "Up"), ("N2", "Down")), "S", [], [], [], []);
        Assert.Equal("Warning", rows[2].Status);
        Assert.Equal("1/2 Online", rows[2].Value);
    }

    [Fact]
    public void Dashboard_NoCluster_Na()
    {
        var rows = DashboardCalculator.BuildDashboard(Cluster("", []), "S", [], [], [], []);
        Assert.Equal("N/A", rows[0].Status);
        Assert.Equal(string.Empty, rows[0].Value);
        Assert.Equal("N/A", rows[2].Status);
        Assert.Equal("0/0 Online", rows[2].Value);
    }

    [Fact]
    public void Dashboard_StorageJobs_Warning()
    {
        var rows = DashboardCalculator.BuildDashboard(
            Cluster("", []), "S", [], [], [], [Job("J")]);
        Assert.Equal("Warning", rows[9].Status);
        Assert.Equal("1", rows[9].Value);
        Assert.Equal("1", rows[10].Value);
    }

    // ---- Node summary ----

    [Fact]
    public void NodeSummary_RunningOnlySums()
    {
        var rows = DashboardCalculator.BuildNodeSummary(
            [Vm("N1", "A", "Running", waste: 1, cpu: 2, assigned: 4, demand: 3),
             Vm("N1", "B", "Running", waste: 2, cpu: 4, assigned: 8, demand: 6),
             Vm("N1", "C", "Off", waste: 50, cpu: 16, assigned: 64, demand: 64)],
            [Hw("N1", ram: 32, used: 20, free: 12, pct: 62.5, logical: 8)]);
        var node = Assert.Single(rows);
        Assert.Equal("N1", node.Node);
        Assert.Equal(2, node.RunningVM);
        Assert.Equal(1, node.OffVM);
        Assert.Equal(6, node.VCpu);
        Assert.Equal(8, node.LogicalCPU);
        Assert.Equal(32, node.RAMGB);
        Assert.Equal(12, node.FreeRAMGB);
        Assert.Equal(62.5, node.RAMUsedPct);
        Assert.Equal(12, node.AssignedGB);
        Assert.Equal(9, node.DemandGB);
        Assert.Equal(3, node.WasteGB);
    }

    [Fact]
    public void NodeSummary_NoRunning_NullVCpu_ZeroGb()
    {
        var rows = DashboardCalculator.BuildNodeSummary(
            [Vm("N1", "C", "Off", waste: 50, cpu: 16)], [Hw("N1")]);
        var node = Assert.Single(rows);
        Assert.Null(node.VCpu);
        Assert.Equal(0, node.AssignedGB);
        Assert.Equal(0, node.DemandGB);
        Assert.Equal(0, node.WasteGB);
    }

    [Fact]
    public void NodeSummary_MissingHardware_NullJoin()
    {
        var rows = DashboardCalculator.BuildNodeSummary([Vm("N9", "A", "Running")], []);
        var node = Assert.Single(rows);
        Assert.Null(node.LogicalCPU);
        Assert.Null(node.RAMGB);
    }

    // ---- VM summary: exact 6 rows ----

    [Fact]
    public void VmSummary_ExactSixRows_RunningOnlyRam()
    {
        var rows = DashboardCalculator.BuildVmSummary(
            [Vm("N1", "A", "Running", assigned: 4, demand: 3, waste: 1),
             Vm("N1", "B", "Off", assigned: 64, demand: 64, waste: 0)]);
        Assert.Equal(
            ["Total VM", "Running", "Off", "AssignedGB Running", "DemandGB Running", "WasteGB Running"],
            rows.Select(r => r.Metric));
        Assert.Equal([2, 1, 1, 4, 3, 1], rows.Select(r => r.Value));
    }
}
