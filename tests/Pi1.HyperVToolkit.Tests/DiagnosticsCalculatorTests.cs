using Pi1.HyperVToolkit.Core.Diagnostics;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

// Pure deterministic semantics for Pi1.Diagnostics.psm1 (frozen, no cluster
// needed). Operators are exact: Waste < 0, Waste > 16, Waste > 8 (strict);
// CSV FreePercent < 20; Resources State != Online. Thresholds are reused
// from Core, never duplicated.
public sealed class DiagnosticsCalculatorTests
{
    private static VirtualMachineRow Vm(
        string vm, double waste, string state = "Running", string host = "N1",
        double assigned = 10, double demand = 5) =>
        new(host, vm, state, 2, assigned, demand, waste, "", "", "", null, false, assigned, 0, assigned);

    private static CsvRow Csv(string name, double freePercent) =>
        new(name, "Online", "N1", 100.0, freePercent, 100.0 - freePercent, freePercent, $"C:\\{name}");

    private static ClusterResourceRow Res(string name, string state, string group = "G1") =>
        new(name, state, group, "Virtual Machine", "N1");

    // ---- Advisor thresholds / operators ----

    [Theory]
    [InlineData(7.9, false)]
    [InlineData(8.0, false)]
    [InlineData(8.1, true)]
    public void Advisor_EightGbBoundary(double waste, bool expected)
    {
        var rows = AdvisorCalculator.Build([Vm("APP1", waste)]);
        Assert.Equal(expected, rows.Count == 1);
        if (expected)
        {
            Assert.Equal("Info", rows[0].Severity);
            Assert.Equal(AdvisorRule.Reserve, rows[0].Rule);
        }
    }

    [Theory]
    [InlineData(15.9, AdvisorRule.Reserve)]
    [InlineData(16.0, AdvisorRule.Reserve)]
    [InlineData(16.1, AdvisorRule.LargeReserve)]
    public void Advisor_SixteenGbBoundary(double waste, AdvisorRule expectedRule)
    {
        var rows = AdvisorCalculator.Build([Vm("APP1", waste)]);
        var row = Assert.Single(rows);
        Assert.Equal("Info", row.Severity);
        Assert.Equal(expectedRule, row.Rule);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1.0)]
    [InlineData(-16.5)]
    public void Advisor_NegativeWaste_IsWarningPressure(double waste)
    {
        var row = Assert.Single(AdvisorCalculator.Build([Vm("APP1", waste)]));
        Assert.Equal("Warning", row.Severity);
        Assert.Equal(AdvisorRule.Pressure, row.Rule);
        Assert.Equal(
            "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.",
            AdvisorCalculator.ReferenceRecommendation(row));
    }

    [Fact]
    public void Advisor_ZeroWaste_IsExcluded()
    {
        Assert.Empty(AdvisorCalculator.Build([Vm("APP1", 0.0)]));
    }

    [Fact]
    public void Advisor_OffState_ExcludedEvenWithWaste()
    {
        Assert.Empty(AdvisorCalculator.Build([Vm("APP1", -5.0, "Off")]));
        Assert.Empty(AdvisorCalculator.Build([Vm("APP1", 20.0, "Off")]));
    }

    [Fact]
    public void Advisor_StateMatch_IsCaseInsensitive()
    {
        // PowerShell -eq is case-insensitive.
        Assert.Single(AdvisorCalculator.Build([Vm("APP1", -1.0, "running")]));
        Assert.Single(AdvisorCalculator.Build([Vm("APP1", 9.0, "RUNNING")]));
    }

    [Fact]
    public void Advisor_Precedence_NegativeBeatsLargeReserve()
    {
        // if/elseif chain: a single input row yields at most one finding.
        var rows = AdvisorCalculator.Build([Vm("APP1", -20.0)]);
        var row = Assert.Single(rows);
        Assert.Equal(AdvisorRule.Pressure, row.Rule);
        Assert.Equal("Warning", row.Severity);
    }

    [Fact]
    public void Advisor_OnlyRunningRowsConsidered_OrderPreserved()
    {
        var input = new[]
        {
            Vm("VM-A", 9.0, "Running"),
            Vm("VM-B", 1.0, "Running"),
            Vm("VM-C", -1.0, "Off"),
            Vm("VM-D", 20.0, "Running"),
        };
        var rows = AdvisorCalculator.Build(input);
        Assert.Equal(["VM-A", "VM-D"], rows.Select(r => r.VM));
    }

    [Fact]
    public void Advisor_FieldsPreserved()
    {
        var row = Assert.Single(AdvisorCalculator.Build(
            [new VirtualMachineRow("NODE9", "APP9", "Running", 4, 12.5, 3.5, 9.0,
                "", "", "", null, false, 12.5, 1.0, 16.0)]));
        Assert.Equal("NODE9", row.HostNode);
        Assert.Equal("APP9", row.VM);
        Assert.Equal(12.5, row.AssignedGB);
        Assert.Equal(3.5, row.DemandGB);
        Assert.Equal(9.0, row.WasteGB);
    }

    [Fact]
    public void Advisor_DuplicateBaseRows_YieldDuplicateFindingsParity()
    {
        // Get-PiVMBaseRows yields one row per NIC; Show-PiAdvisor loops rows
        // without dedupe, so identical base rows yield identical findings.
        var dupe = Vm("APP1", 9.0);
        var rows = AdvisorCalculator.Build([dupe, dupe]);
        Assert.Equal(2, rows.Count);
    }

    // ---- Sensitive workload pattern ----

    [Theory]
    [InlineData("SQL", true)]
    [InlineData("sql", true)]
    [InlineData("Sql", true)]
    [InlineData("VEEAM", true)]
    [InlineData("veeam", true)]
    [InlineData("EXCHANGE", true)]
    [InlineData("exchange", true)]
    [InlineData("FORTI", true)]
    [InlineData("forti", true)]
    [InlineData("FAZ", true)]
    [InlineData("faz", true)]
    [InlineData("MYSQL", true)] // contains SQL
    [InlineData("FORTIGATE", true)] // contains FORTI
    [InlineData("EXCHANGE2019", true)]
    [InlineData("APP1", false)]
    [InlineData("ORACLE", false)]
    [InlineData("FA", false)]
    [InlineData("SQLITE-BACKUP", true)]
    public void Advisor_SensitivePattern_IsCaseInsensitiveSubstring(string vmName, bool expected)
    {
        Assert.Equal(expected, AdvisorCalculator.IsSensitiveWorkload(vmName));
    }

    [Fact]
    public void Advisor_Sensitive_OverridesInfoRecommendationOnly()
    {
        var info = Assert.Single(AdvisorCalculator.Build([Vm("SQL01", 9.0)]));
        Assert.Equal("Info", info.Severity);
        Assert.True(info.IsSensitive);
        Assert.Equal(
            "Špecifický workload. Neznižovať bez dlhšieho sledovania.",
            AdvisorCalculator.ReferenceRecommendation(info));

        var large = Assert.Single(AdvisorCalculator.Build([Vm("veeam-proxy", 20.0)]));
        Assert.Equal("Info", large.Severity);
        Assert.True(large.IsSensitive);
        Assert.Equal(
            "Špecifický workload. Neznižovať bez dlhšieho sledovania.",
            AdvisorCalculator.ReferenceRecommendation(large));
    }

    [Fact]
    public void Advisor_Sensitive_DoesNotOverrideWarning()
    {
        var warn = Assert.Single(AdvisorCalculator.Build([Vm("SQL01", -2.0)]));
        Assert.Equal("Warning", warn.Severity);
        Assert.True(warn.IsSensitive);
        Assert.Equal(
            "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.",
            AdvisorCalculator.ReferenceRecommendation(warn));
    }

    [Fact]
    public void Advisor_NonSensitive_KeepsReserveTexts()
    {
        var reserve = Assert.Single(AdvisorCalculator.Build([Vm("APP1", 9.0)]));
        Assert.False(reserve.IsSensitive);
        Assert.Equal("RAM rezerva > 8 GB. Sledovať.", AdvisorCalculator.ReferenceRecommendation(reserve));

        var large = Assert.Single(AdvisorCalculator.Build([Vm("APP1", 20.0)]));
        Assert.False(large.IsSensitive);
        Assert.Equal(
            "Veľká RAM rezerva. Kandidát na zníženie po sledovaní.",
            AdvisorCalculator.ReferenceRecommendation(large));
    }

    [Fact]
    public void Advisor_ReusesFrozenThresholds()
    {
        Assert.Equal(8.0, Thresholds.WasteInfoGb);
        Assert.Equal(16.0, Thresholds.WasteLargeInfoGb);
        Assert.Equal("SQL|VEEAM|EXCHANGE|FORTI|FAZ", Thresholds.SensitiveWorkloadPattern);
    }

    // ---- CSV Low Free ----

    [Theory]
    [InlineData(19.9, true)]
    [InlineData(20.0, false)]
    [InlineData(20.1, false)]
    [InlineData(0.0, true)]
    public void CsvLowFree_Boundary(double freePercent, bool expected)
    {
        var rows = CsvLowFreeCalculator.Filter([Csv("CSV1", freePercent)]);
        Assert.Equal(expected, rows.Count == 1);
    }

    [Fact]
    public void CsvLowFree_MultipleCSVs_SortedByFreePercent()
    {
        var rows = CsvLowFreeCalculator.Filter([
            Csv("C-high", 19.0),
            Csv("C-low", 5.0),
            Csv("C-ok", 50.0),
            Csv("C-mid", 10.0),
        ]);
        Assert.Equal(["C-low", "C-mid", "C-high"], rows.Select(r => r.Name));
    }

    [Fact]
    public void CsvLowFree_NoCsv_Empty()
    {
        Assert.Empty(CsvLowFreeCalculator.Filter([]));
    }

    [Fact]
    public void CsvLowFree_FieldsPreserved()
    {
        var row = Assert.Single(CsvLowFreeCalculator.Filter(
            [new CsvRow("CSV9", "Online", "N2", 200.0, 10.0, 190.0, 5.0, @"C:\ClusterStorage\Vol9")]));
        Assert.Equal("CSV9", row.Name);
        Assert.Equal("N2", row.OwnerNode);
        Assert.Equal(@"C:\ClusterStorage\Vol9", row.Path);
        Assert.Equal(5.0, row.FreePercent);
    }

    [Fact]
    public void CsvLowFree_DoesNotAlterDashboardFifteenPercentRule()
    {
        // Diagnostics lists below 20 %; Dashboard/Health stay at 15 %.
        Assert.Equal(20.0, Thresholds.CsvLowFreeViewPercent);
        Assert.Equal(15.0, Thresholds.CsvWarningPercent);
        Assert.NotEqual(Thresholds.CsvWarningPercent, Thresholds.CsvLowFreeViewPercent);
        // 17 % is a Diagnostics hit but NOT a Dashboard warning.
        Assert.Single(CsvLowFreeCalculator.Filter([Csv("C", 17.0)]));
        Assert.False(17.0 < Thresholds.CsvWarningPercent);
    }

    // ---- Cluster Resources Not Online ----

    [Theory]
    [InlineData("Online", false)]
    [InlineData("online", false)]
    [InlineData("ONLINE", false)]
    [InlineData("Offline", true)]
    [InlineData("Failed", true)]
    [InlineData("Pending", true)]
    [InlineData("Unknown", true)]
    [InlineData("OnlinePending", true)]
    [InlineData("OfflinePending", true)]
    [InlineData("", true)]
    public void ClusterResources_Filter_IncludesEverythingExceptOnline(string state, bool expected)
    {
        var rows = ClusterResourceDiagnostics.FilterNotOnline([Res("R1", state)]);
        Assert.Equal(expected, rows.Count == 1);
    }

    [Fact]
    public void ClusterResources_Offline_IsIncluded_UnlikeHealthScore()
    {
        // Diagnostics shows Offline; Health Score tolerates Offline.
        Assert.Single(ClusterResourceDiagnostics.FilterNotOnline([Res("R1", "Offline")]));
    }

    [Fact]
    public void ClusterResources_NoResources_Empty()
    {
        Assert.Empty(ClusterResourceDiagnostics.FilterNotOnline([]));
    }

    [Fact]
    public void ClusterResources_AllOnline_EmptyHealthy()
    {
        Assert.Empty(ClusterResourceDiagnostics.FilterNotOnline([
            Res("R1", "Online"), Res("R2", "Online"),
        ]));
    }

    [Fact]
    public void ClusterResources_SortOrder_StateOwnerGroupName()
    {
        var rows = ClusterResourceDiagnostics.FilterNotOnline([
            Res("B-res", "Failed", "G2"),
            Res("A-res", "Failed", "G1"),
            Res("C-res", "Offline", "G1"),
            Res("OK-res", "Online", "G1"),
        ]);
        // Sort-Object State, OwnerGroup, Name (alphabetical): Failed/G1, Failed/G2, Offline/G1.
        Assert.Equal(["A-res", "B-res", "C-res"], rows.Select(r => r.Name));
    }

    [Fact]
    public void ClusterResources_FieldsPreserved()
    {
        var row = Assert.Single(ClusterResourceDiagnostics.FilterNotOnline(
            [new ClusterResourceRow("VM Res", "Failed", "VMGroup", "Virtual Machine", "N2")]));
        Assert.Equal("VM Res", row.Name);
        Assert.Equal("Failed", row.State);
        Assert.Equal("VMGroup", row.OwnerGroup);
        Assert.Equal("Virtual Machine", row.ResourceType);
        Assert.Equal("N2", row.OwnerNode);
    }
}
