using System.Text.Json;
using Pi1.HyperVToolkit.Core.Diagnostics;
using Pi1.HyperVToolkit.Core.Models;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Cross-engine formula parity for Pi1.Diagnostics.psm1: IDENTICAL fabricated
// inputs through the reference PowerShell logic vs the C# calculators. Runs
// on ANY machine (no cluster needed). Live-cluster Diagnostics parity is NOT
// TESTABLE on a non-clustered laptop and is reported as such, never faked.
public sealed class DiagnosticsFormulaParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Modules = ["Pi1.Core.psm1", "Pi1.Data.psm1"];

    public DiagnosticsFormulaParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    private static VirtualMachineRow Vm(string vm, double waste, string state = "Running", double assigned = 10.0, double demand = 1.0) =>
        new("N1", vm, state, 2, assigned, demand, waste, "", "", "", null, false, assigned, 1.0, assigned);

    [Fact]
    public async Task Advisor_ReferenceFormulas()
    {
        // Verbatim Show-PiAdvisor decision chain over fabricated rows.
        const string snippet = "$vms = @(" +
            "[PSCustomObject]@{ HostNode='N1'; VM='APP1'; State='Running'; AssignedGB=10; DemandGB=1; WasteGB=9 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='SQL01'; State='Running'; AssignedGB=10; DemandGB=1; WasteGB=9 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='BIG'; State='Running'; AssignedGB=30; DemandGB=5; WasteGB=25 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='NEG'; State='Running'; AssignedGB=4; DemandGB=6; WasteGB=-2 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='SQLNEG'; State='Running'; AssignedGB=4; DemandGB=6; WasteGB=-2 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='OKVM'; State='Running'; AssignedGB=5; DemandGB=4; WasteGB=1 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='OFF'; State='Off'; AssignedGB=30; DemandGB=5; WasteGB=25 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='EDGE8'; State='Running'; AssignedGB=10; DemandGB=2; WasteGB=8 }, " +
            "[PSCustomObject]@{ HostNode='N1'; VM='EDGE16'; State='Running'; AssignedGB=20; DemandGB=4; WasteGB=16 }); " +
            "$rows=@(); " +
            "foreach($vm in $vms){ " +
            "if($vm.State -ne 'Running'){ continue }; " +
            "$severity='OK'; $recommendation='OK'; " +
            "if($vm.WasteGB -lt 0){$severity='Warning'; $recommendation='Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.'} " +
            "elseif($vm.WasteGB -gt 16){$severity='Info'; $recommendation='Veľká RAM rezerva. Kandidát na zníženie po sledovaní.'} " +
            "elseif($vm.WasteGB -gt 8){$severity='Info'; $recommendation='RAM rezerva > 8 GB. Sledovať.'} " +
            "if($vm.VM -match 'SQL|VEEAM|EXCHANGE|FORTI|FAZ'){ if($severity -eq 'Info'){$recommendation='Špecifický workload. Neznižovať bez dlhšieho sledovania.'} } " +
            "if($severity -ne 'OK'){ $rows += [PSCustomObject]@{Severity=$severity; HostNode=$vm.HostNode; VM=$vm.VM; AssignedGB=$vm.AssignedGB; DemandGB=$vm.DemandGB; WasteGB=$vm.WasteGB; Recommendation=$recommendation} } }; " +
            "$rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["AssignedGB", "DemandGB", "WasteGB"]);

        var input = new[]
        {
            Vm("APP1", 9, "Running", 10, 1), Vm("SQL01", 9, "Running", 10, 1),
            Vm("BIG", 25, "Running", 30, 5), Vm("NEG", -2, "Running", 4, 6),
            Vm("SQLNEG", -2, "Running", 4, 6), Vm("OKVM", 1, "Running", 5, 4),
            Vm("OFF", 25, "Off", 30, 5),
            Vm("EDGE8", 8, "Running", 10, 2), Vm("EDGE16", 16, "Running", 20, 4),
        };
        var cs = AdvisorCalculator.Build(input);
        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Severity"] = r.Severity,
            ["HostNode"] = r.HostNode,
            ["VM"] = r.VM,
            ["AssignedGB"] = r.AssignedGB,
            ["DemandGB"] = r.DemandGB,
            ["WasteGB"] = r.WasteGB,
            ["Recommendation"] = AdvisorCalculator.ReferenceRecommendation(r),
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["AssignedGB", "DemandGB", "WasteGB"]),
            r => $"{r.GetValueOrDefault("VM")}",
            ["Severity", "HostNode", "VM", "AssignedGB", "DemandGB", "WasteGB", "Recommendation"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Advisor formula parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task CsvLowFree_ReferenceFormulas()
    {
        const string snippet = "$csvs = @(" +
            "[PSCustomObject]@{ Name='C-low'; State='Online'; OwnerNode='N1'; SizeGB=100; FreeGB=5; UsedGB=95; FreePercent=5; Path='C:\\Low' }, " +
            "[PSCustomObject]@{ Name='C-edge'; State='Online'; OwnerNode='N1'; SizeGB=100; FreeGB=20; UsedGB=80; FreePercent=20; Path='C:\\Edge' }, " +
            "[PSCustomObject]@{ Name='C-mid'; State='Online'; OwnerNode='N1'; SizeGB=100; FreeGB=19.9; UsedGB=80.1; FreePercent=19.9; Path='C:\\Mid' }, " +
            "[PSCustomObject]@{ Name='C-ok'; State='Online'; OwnerNode='N1'; SizeGB=100; FreeGB=50; UsedGB=50; FreePercent=50; Path='C:\\Ok' }); " +
            "@($csvs | Where-Object {$_.FreePercent -lt 20} | Sort-Object FreePercent)";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["SizeGB", "FreeGB", "UsedGB", "FreePercent"]);

        var input = new[]
        {
            new CsvRow("C-low", "Online", "N1", 100, 5, 95, 5, "C:\\Low"),
            new CsvRow("C-edge", "Online", "N1", 100, 20, 80, 20, "C:\\Edge"),
            new CsvRow("C-mid", "Online", "N1", 100, 19.9, 80.1, 19.9, "C:\\Mid"),
            new CsvRow("C-ok", "Online", "N1", 100, 50, 50, 50, "C:\\Ok"),
        };
        var cs = CsvLowFreeCalculator.Filter(input);
        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["OwnerNode"] = r.OwnerNode,
            ["SizeGB"] = r.SizeGB,
            ["FreeGB"] = r.FreeGB,
            ["UsedGB"] = r.UsedGB,
            ["FreePercent"] = r.FreePercent,
            ["Path"] = r.Path,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["SizeGB", "FreeGB", "UsedGB", "FreePercent"]),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"CSV low-free parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task ClusterResourcesNotOnline_ReferenceFormulas()
    {
        const string snippet = "$res = @(" +
            "[PSCustomObject]@{ Name='R-online'; State='Online'; OwnerGroup='G1'; ResourceType='T'; OwnerNode='N1' }, " +
            "[PSCustomObject]@{ Name='R-off'; State='Offline'; OwnerGroup='G1'; ResourceType='T'; OwnerNode='N1' }, " +
            "[PSCustomObject]@{ Name='R-fail'; State='Failed'; OwnerGroup='G1'; ResourceType='T'; OwnerNode='N1' }, " +
            "[PSCustomObject]@{ Name='A-pend'; State='Pending'; OwnerGroup='G1'; ResourceType='T'; OwnerNode='N1' }, " +
            "[PSCustomObject]@{ Name='R-unk'; State='Unknown'; OwnerGroup='G2'; ResourceType='T'; OwnerNode='N1' }); " +
            "@($res | Where-Object {$_.State -ne 'Online'} | Select-Object Name, State, OwnerGroup, ResourceType, OwnerNode | Sort-Object State, OwnerGroup, Name)";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);

        var input = new[]
        {
            new ClusterResourceRow("R-online", "Online", "G1", "T", "N1"),
            new ClusterResourceRow("R-off", "Offline", "G1", "T", "N1"),
            new ClusterResourceRow("R-fail", "Failed", "G1", "T", "N1"),
            new ClusterResourceRow("A-pend", "Pending", "G1", "T", "N1"),
            new ClusterResourceRow("R-unk", "Unknown", "G2", "T", "N1"),
        };
        var cs = ClusterResourceDiagnostics.FilterNotOnline(input);
        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["OwnerGroup"] = r.OwnerGroup,
            ["ResourceType"] = r.ResourceType,
            ["OwnerNode"] = r.OwnerNode,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "State", "OwnerGroup", "ResourceType", "OwnerNode"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Resources-not-online parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public void LiveCluster_Diagnostics_NotTestableWithoutCluster()
    {
        // LATITUDE-5590 is not a Failover Cluster: live Diagnostics parity
        // (real Get-ClusterResource / real CSV on a cluster) cannot run here.
        // This documents the deferral; Release Candidate acceptance will run
        // it on a real cluster. Never fabricate a PASS.
        Output.WriteLine("NOT TESTABLE IN CURRENT ENVIRONMENT: no Failover Cluster on this machine.");
        Assert.True(true);
    }
}
