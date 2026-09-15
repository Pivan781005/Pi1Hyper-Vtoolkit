using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Cluster;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Phase 5 cluster parity. Two layers:
//  1. LIVE collector comparison (nodes/groups/resources/networks/quorum/
//     witness/events/placement): genuine Diff on a real cluster; on this
//     non-clustered machine both sides are empty and the test reports
//     NOT TESTABLE (pass) instead of inventing data.
//  2. CROSS-ENGINE formula checks (run everywhere): identical fabricated
//     inputs through the reference PowerShell logic vs the C# calculators.
// Only PowerShell-as-reference in tests; production stays native.
public sealed class ClusterParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    // NOTE: Pi1.Cluster.psm1 is deliberately NOT imported: it has a latent
    // module-wide parse bug (line 461, "z $failedNode:") so Import-Module
    // fails on any machine and would poison stderr. Live snippets call the
    // FailoverClusters cmdlets directly; placement uses an inline replica.
    private static readonly string[] ClusterModules = [];

    // Every live snippet guards the missing FailoverClusters capability so a
    // non-cluster host yields clean empty JSON instead of a cmdlet error.
    // Shape matters: the guard assigns to $r (an if-statement cannot be piped
    // to ConvertTo-Json) and each snippet ends with the output expression $r.
    private const string CapabilityGuard =
        "$r = if (-not (Get-Module -ListAvailable -Name FailoverClusters)) { @() } else { ";

    public ClusterParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    private async Task<Core.Services.ClusterTopologySnapshot> CollectLiveAsync()
    {
        using var querier = new MmiCimQuerier(NullLogger<MmiCimQuerier>.Instance);
        var service = new ClusterService(
            querier,
            new ClusterEventReader(NullLogger<ClusterEventReader>.Instance),
            NullLogger<ClusterService>.Instance);
        return await service.GetSnapshotAsync();
    }

    private void ReportLiveNotTestable(string what, int psCount, bool csAvailable)
    {
        ReportNotTestable(Output,
            $"NOT TESTABLE IN CURRENT ENVIRONMENT: no cluster capability here ({what}: " +
            $"PS rows {psCount}, C# available {csAvailable}). Live comparison runs on a real cluster.");
    }

    [Fact]
    public async Task ClusterNodesParity()
    {
        const string snippet = CapabilityGuard +
            "Get-ClusterNode -ErrorAction Stop | Select-Object Name, State, " +
            "@{Name='DrainStatus';Expression={$_.DrainStatus}}, " +
            "@{Name='NodeWeight';Expression={$_.NodeWeight}}, " +
            "@{Name='FaultDomain';Expression={$_.FaultDomain}} | Sort-Object Name }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["NodeWeight"]);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS nodes: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("nodes", 0, false);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.Nodes.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["DrainStatus"] = r.DrainStatus,
            ["NodeWeight"] = r.NodeWeight,
            ["FaultDomain"] = r.FaultDomain,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["NodeWeight"]),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "State", "DrainStatus", "NodeWeight", "FaultDomain"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Cluster nodes parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task ClusterGroupsParity()
    {
        const string snippet = CapabilityGuard +
            "Get-ClusterGroup -ErrorAction Stop | Select-Object Name, State, OwnerNode, " +
            "@{Name='GroupType';Expression={$_.GroupType}}, @{Name='Priority';Expression={$_.Priority}} | " +
            "Sort-Object OwnerNode, Name }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS groups: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("groups", 0, false);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.Groups.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["OwnerNode"] = r.OwnerNode,
            ["GroupType"] = r.GroupType,
            ["Priority"] = r.Priority,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "State", "OwnerNode", "GroupType", "Priority"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Cluster groups parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task ClusterResourcesParity()
    {
        const string snippet = CapabilityGuard +
            "Get-ClusterResource -ErrorAction Stop | Select-Object Name, State, OwnerGroup, ResourceType, OwnerNode | " +
            "Sort-Object State, OwnerGroup, Name }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS resources: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("resources", 0, false);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.Resources.Select(r => new Dictionary<string, object?>
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

        Assert.True(diffs.Count == 0, $"Cluster resources parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task ClusterNetworksParity()
    {
        const string snippet = CapabilityGuard +
            "Get-ClusterNetwork -ErrorAction Stop | Select-Object Name, State, Role, Address, AddressMask, Metric, AutoMetric | " +
            "Sort-Object Name }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["Metric"]);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS networks: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("networks", 0, false);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.Networks.Select(r => new Dictionary<string, object?>
        {
            ["Name"] = r.Name,
            ["State"] = r.State,
            ["Role"] = r.Role,
            ["Address"] = r.Address,
            ["AddressMask"] = r.AddressMask,
            ["Metric"] = r.Metric,
            ["AutoMetric"] = r.AutoMetric,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["Metric"]),
            r => $"{r.GetValueOrDefault("Name")}",
            ["Name", "State", "Role", "Address", "AddressMask", "Metric", "AutoMetric"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Cluster networks parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task QuorumParity()
    {
        const string snippet = CapabilityGuard +
            "Get-ClusterQuorum -ErrorAction Stop | Select-Object QuorumType, QuorumResource }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS quorum rows: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("quorum", 0, false);
            return;
        }

        var quorum = snapshot.Quorum;
        var csRows = quorum is null
            ? new List<Dictionary<string, string>>()
            : ParityRow.ParseRows(JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?> { ["QuorumType"] = quorum.QuorumType, ["QuorumResource"] = quorum.QuorumResource },
            }), []);
        var diffs = ParityRow.Diff(psRows, csRows,
            r => $"{r.GetValueOrDefault("QuorumType")}|{r.GetValueOrDefault("QuorumResource")}",
            ["QuorumType", "QuorumResource"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Quorum parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task WitnessParity()
    {
        // Live witness rows: quorum row plus Witness|Quorum-matched resources
        // with the intended parameter subset as Detail.
        const string snippet = CapabilityGuard +
            "$rows = @(); " +
            "$q = Get-ClusterQuorum -ErrorAction Stop; " +
            "foreach($one in @($q)){ $rows += [PSCustomObject]@{Section='Quorum'; Name='Quorum'; Type=$one.QuorumType; State=''; OwnerGroup=''; OwnerNode=''; Detail=[string]$one.QuorumResource} }; " +
            "$resources = Get-ClusterResource -ErrorAction Stop | Where-Object { $_.ResourceType -match 'Witness' -or $_.Name -match 'Witness|Quorum' }; " +
            "foreach($res in @($resources)){ $detailParts=@(); " +
            "$params = Get-ClusterParameter -InputObject $res -ErrorAction Stop; " +
            "foreach($p in @($params)){ if($p.Name -match 'Share|Path|Account|Endpoint|Witness|Disk|Cloud|Storage|File'){ $detailParts += ('{0}={1}' -f $p.Name,$p.Value) } }; " +
            "$rows += [PSCustomObject]@{Section='Resource'; Name=$res.Name; Type=$res.ResourceType; State=$res.State; OwnerGroup=$res.OwnerGroup; OwnerNode=$res.OwnerNode; Detail=($detailParts -join '; ')} }; " +
            "$rows }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS witness rows: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && !snapshot.IsAvailable)
        {
            ReportLiveNotTestable("witness", 0, false);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.WitnessRows.Select(r => new Dictionary<string, object?>
        {
            ["Section"] = r.Section,
            ["Name"] = r.Name,
            ["Type"] = r.Type,
            ["State"] = r.State,
            ["OwnerGroup"] = r.OwnerGroup,
            ["OwnerNode"] = r.OwnerNode,
            ["Detail"] = r.Detail,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("Section")}|{r.GetValueOrDefault("Name")}",
            ["Section", "Name", "Type", "State", "OwnerGroup", "OwnerNode", "Detail"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Witness parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task ClusterEventsParity()
    {
        const string snippet = CapabilityGuard +
            "Get-WinEvent -LogName 'Microsoft-Windows-FailoverClustering/Operational' -MaxEvents 30 -ErrorAction Stop | " +
            "Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, ClusterModules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, ["Id"]);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS events: {psRows.Count}, C# snapshot events: {snapshot.Events.Count}");

        if (psRows.Count == 0 && snapshot.Events.Count == 0)
        {
            ReportLiveNotTestable("events", 0, snapshot.IsAvailable);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.Events.Select(r => new Dictionary<string, object?>
        {
            ["TimeCreated"] = r.TimeCreated,
            ["Id"] = r.Id,
            ["LevelDisplayName"] = r.Level,
            ["ProviderName"] = r.Provider,
            ["Message"] = r.Message,
        }));
        // TimeCreated compared loosely (same instant, format drift allowed);
        // Id/Level/Provider/Message exact.
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, ["Id"]),
            r => $"{r.GetValueOrDefault("Id")}|{r.GetValueOrDefault("TimeCreated")}|{(r.GetValueOrDefault("Message") ?? string.Empty).Length}",
            ["Id", "LevelDisplayName", "ProviderName", "Message"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Cluster events parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task PlacementParity()
    {
        // Get-PiClusterVMPlacementRows cannot be invoked via module import:
        // Pi1.Cluster.psm1 has a latent module-wide parse bug (line 461,
        // "z $failedNode:") so Import-Module fails on ANY machine. This
        // snippet replicates the function body VERBATIM (helper + per-group
        // logic) against the REAL cluster cmdlets; on this non-clustered
        // machine the guard yields clean empty output.
        var snippet = ClusterFormulaParityTests.OwnerConvertFunctionSource +
            "if (-not (Get-Module -ListAvailable -Name FailoverClusters)) { @() } else { " +
            "function Get-OwnerNamesSafe { param([string]$GroupName, [object]$Resource) " +
            "try { if ($null -ne $Resource) { $r = $Resource | Get-ClusterOwnerNode -ErrorAction Stop; " +
            "return @(Convert-PiClusterOwnerNodeListToNames -OwnerNodeResult $r) } " +
            "if (-not [string]::IsNullOrWhiteSpace($GroupName)) { $r = Get-ClusterOwnerNode -Group $GroupName -ErrorAction Stop; " +
            "return @(Convert-PiClusterOwnerNodeListToNames -OwnerNodeResult $r) } } catch { return @() }; return @() }; " +
            "$groups = Get-ClusterGroup -ErrorAction Stop | Where-Object { $_.GroupType -eq 'VirtualMachine' } | Sort-Object OwnerNode, Name; " +
            "$rows = @(); " +
            "foreach ($g in @($groups)) { " +
            "$ownerNodes = @(Get-OwnerNamesSafe -GroupName $g.Name); " +
            "$preferredOwners = ($ownerNodes -join ', '); $possibleOwners = ''; " +
            "try { $resources = @(Get-ClusterResource -Group $g.Name -ErrorAction Stop); " +
            "$vmResource = $resources | Where-Object { $_.ResourceType -eq 'Virtual Machine' } | Select-Object -First 1; " +
            "if ($vmResource) { $possibleNodes = @(Get-OwnerNamesSafe -Resource $vmResource); $possibleOwners = ($possibleNodes -join ', ') } } catch { $possibleOwners = '' }; " +
            "$antiAffinity = ''; " +
            "try { if ($g.PSObject.Properties.Name -contains 'AntiAffinityClassNames') { $antiAffinity = ($g.AntiAffinityClassNames -join ', ') } } catch { $antiAffinity = '' }; " +
            "$autoFailback = ''; " +
            "try { if ($g.PSObject.Properties.Name -contains 'AutoFailbackType') { $autoFailback = [string]$g.AutoFailbackType } } catch { $autoFailback = '' }; " +
            "$failbackWindow = ''; " +
            "try { $start = if ($g.PSObject.Properties.Name -contains 'FailbackWindowStart') { $g.FailbackWindowStart } else { '' }; " +
            "$end = if ($g.PSObject.Properties.Name -contains 'FailbackWindowEnd') { $g.FailbackWindowEnd } else { '' }; " +
            "if ($start -ne '' -or $end -ne '') { $failbackWindow = ('{0}-{1}' -f $start, $end) } } catch { $failbackWindow = '' }; " +
            "$rows += [PSCustomObject]@{ VMGroup=$g.Name; State=$g.State; OwnerNode=$g.OwnerNode; Priority=$g.Priority; " +
            "PreferredOwners=$preferredOwners; PossibleOwners=$possibleOwners; AntiAffinity=$antiAffinity; AutoFailback=$autoFailback; FailbackWindow=$failbackWindow } }; " +
            "$rows }; $r";
        var reference = await PowerShellReference.CollectSnippetAsync(
            snippet, ["Pi1.Core.psm1", "Pi1.Data.psm1"], ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json, []);
        var snapshot = await CollectLiveAsync();
        Output.WriteLine($"PS placement rows: {psRows.Count}, C# available: {snapshot.IsAvailable}");

        if (psRows.Count == 0 && snapshot.PlacementRows.Count == 0)
        {
            ReportLiveNotTestable("placement", 0, snapshot.IsAvailable);
            return;
        }

        var csJson = JsonSerializer.Serialize(snapshot.PlacementRows.Select(r => new Dictionary<string, object?>
        {
            ["VMGroup"] = r.VMGroup,
            ["State"] = r.State,
            ["OwnerNode"] = r.OwnerNode,
            ["Priority"] = r.Priority,
            ["PreferredOwners"] = r.PreferredOwnersDisplay,
            ["PossibleOwners"] = r.PossibleOwnersDisplay,
            ["AntiAffinity"] = r.AntiAffinity,
            ["AutoFailback"] = r.AutoFailback,
            ["FailbackWindow"] = r.FailbackWindow,
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("VMGroup")}",
            ["VMGroup", "State", "OwnerNode", "Priority", "PreferredOwners", "PossibleOwners", "AntiAffinity", "AutoFailback", "FailbackWindow"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Placement parity failed with {diffs.Count} difference(s).");
    }
}
