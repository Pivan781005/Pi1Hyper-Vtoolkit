using System.Text.Json;
using Pi1.HyperVToolkit.Core.Cluster;
using Pi1.HyperVToolkit.Core.Models;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Cross-engine formula parity: IDENTICAL fabricated inputs through the
// reference PowerShell logic vs the C# calculators. Runs on ANY machine
// (no cluster needed) and genuinely verifies thresholds, rounding,
// precedence and text decisions. Live-collector comparison lives in
// ClusterParityTests (NOT TESTABLE without a cluster).
public sealed class ClusterFormulaParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Modules =
        ["Pi1.Core.psm1", "Pi1.Data.psm1"];

    // VERBATIM copy of Convert-PiClusterOwnerNodeListToNames
    // (Modules\Pi1.Cluster.psm1, function body). It cannot be imported:
    // Pi1.Cluster.psm1 has a latent module-wide parse bug at line 461
    // ("... z $failedNode:" — `$failedNode:` parses as a drive-qualified
    // variable), so Import-Module fails on ANY machine. The reference file
    // itself is immutable, therefore this copy plus the sync-guard test
    // below (which fails loudly if the reference ever changes).
    internal const string OwnerConvertFunctionSource = """
        function Convert-PiClusterOwnerNodeListToNames {
            param([object]$OwnerNodeResult)
            $names = @()
            foreach ($item in @($OwnerNodeResult)) {
                if ($null -eq $item) { continue }
                if ($item -is [string]) {
                    if (-not [string]::IsNullOrWhiteSpace($item)) { $names += $item }
                    continue
                }
                $propNames = @($item.PSObject.Properties.Name)
                foreach ($candidate in @("Name","NodeName","OwnerNode","ClusterNode","Node")) {
                    if ($propNames -contains $candidate) {
                        $value = $item.$candidate
                        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
                            $names += [string]$value
                        }
                    }
                }
                if ($propNames -contains "ClusterObject") {
                    $co = $item.ClusterObject
                    if ($null -ne $co) {
                        $coProps = @($co.PSObject.Properties.Name)
                        if ($coProps -contains "Name") { $names += [string]$co.Name }
                    }
                }
                if ($names.Count -eq 0) {
                    $text = [string]$item
                    if (-not [string]::IsNullOrWhiteSpace($text) -and $text -notmatch "ClusterOwnerNodeList") {
                        $names += $text
                    }
                }
            }
            return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        }
        """;

    public ClusterFormulaParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    private static VirtualMachineRow Vm(string host, string state, int cpu, double assigned, double demand) =>
        new(host, "VM-" + host + "-" + cpu, state, cpu, assigned, demand,
            Math.Round(assigned - demand, 1), "", "", "", null, false, assigned, 0, assigned);

    private static NodeHardwareRow Hw(string node, double? ram, double? free = null) =>
        new(node, "", "", "", "", null, "", 0, null, null, ram, null, free, null);

    private static VmPlacementRow Placement(string group, string owner, string priority = "Medium",
        params string[] preferred) =>
        new(group, "Online", owner, priority, preferred, [], "", "", "");

    private static string AdvisorText(PlacementAdviceKind kind, int count) => kind switch
    {
        PlacementAdviceKind.Balanced => "Vyzerá vyvážene.",
        PlacementAdviceKind.HighDemand => "Demand RAM je vysoký. Skontroluj failover kapacitu druhého node.",
        PlacementAdviceKind.HighAssigned => "Assigned RAM je vysoký, ale Demand môže byť v poriadku. Sledovať.",
        PlacementAdviceKind.NoPreferredOwners => $"{count} VM role nemá zistených PreferredOwners.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string FailoverText(FailoverAdviceKind kind) => kind switch
    {
        FailoverAdviceKind.Viable => "Failover podľa Demand RAM vyzerá priechodne.",
        FailoverAdviceKind.Tight => "Po failoveri bude RAM veľmi tesná. Odporúčané optimalizovať RAM alebo rozloženie VM.",
        FailoverAdviceKind.Critical => "Po failoveri by Demand RAM prekročil bezpečnú hranicu. Nutné znížiť RAM, presunúť VM alebo navýšiť kapacitu.",
        FailoverAdviceKind.AssignedHigh => "Assigned RAM bude vysoká, ale Demand môže byť OK. Skontroluj dynamickú RAM a reálnu záťaž.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Fact]
    public void OwnerConvertCopy_MatchesReferenceFile()
    {
        // Sync guard: the inlined Convert-PiClusterOwnerNodeListToNames copy
        // above must stay token-identical to Modules\Pi1.Cluster.psm1.
        var modulesDir = PowerShellReference.FindModulesDirectory();
        Assert.NotNull(modulesDir);
        var text = File.ReadAllText(Path.Combine(modulesDir, "Pi1.Cluster.psm1"));
        var start = text.IndexOf("function Convert-PiClusterOwnerNodeListToNames", StringComparison.Ordinal);
        Assert.True(start >= 0, "Reference function not found.");
        var extracted = ExtractBracedBlock(text, start);
        Assert.NotNull(extracted);
        Assert.Equal(NormalizeTokens(extracted), NormalizeTokens(OwnerConvertFunctionSource));
    }

    private static string? ExtractBracedBlock(string text, int start)
    {
        var open = text.IndexOf('{', start);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var stringChar = '\0';
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (ch == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inString = true;
                stringChar = ch;
            }
            else if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string NormalizeTokens(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    [Fact]
    public async Task OwnerNormalization_ReferenceFunction()
    {
        // Calls the REAL Convert-PiClusterOwnerNodeListToNames logic (inlined
        // verbatim — the module cannot be imported, see above) with fabricated
        // shapes (no cluster needed) and compares against OwnerNames.
        // NOTE (verified live): the reference keeps a plain "ClusterOwnerNodeList"
        // string and dedupes case-SENSITIVELY — both documented Phase-1 debt
        // that C# intentionally fixes per spec (case-insensitive uniqueness,
        // type-name exclusion). Those debt shapes are covered by OwnerNamesTests;
        // this cross-check uses the AGREED shapes only.
        var snippet = OwnerConvertFunctionSource + "$items = @(" +
            "'N1', " +
            "[PSCustomObject]@{ Name = 'N2' }, " +
            "[PSCustomObject]@{ OwnerNode = 'N3' }, " +
            "[PSCustomObject]@{ ClusterObject = [PSCustomObject]@{ Name = 'N4' } }, " +
            "$null, '  ', " +
            "[PSCustomObject]@{ NodeName = 'N2' }); " +
            "Convert-PiClusterOwnerNodeListToNames -OwnerNodeResult $items";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        using var document = JsonDocument.Parse(reference.Json);
        var psNames = document.RootElement.EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty).ToList();

        var csNames = OwnerNames.Normalize([
            "N1",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "N2" },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["OwnerNode"] = "N3" },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ClusterObject"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "N4" },
            },
            null, "  ",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["NodeName"] = "N2" },
        ]);

        Output.WriteLine("PS  : " + string.Join(", ", psNames));
        Output.WriteLine("C#  : " + string.Join(", ", csNames));
        Assert.Equal(psNames, csNames.ToList());
    }

    [Fact]
    public async Task Distribution_ReferenceFormulas()
    {
        const string snippet = "$placement = @(" +
            "[PSCustomObject]@{ OwnerNode='N1'; Priority='High' }, " +
            "[PSCustomObject]@{ OwnerNode='N1'; Priority='Low' }, " +
            "[PSCustomObject]@{ OwnerNode='N2'; Priority='3000' }, " +
            "[PSCustomObject]@{ OwnerNode='N9'; Priority='Medium' }); " +
            "$vms = @(" +
            "[PSCustomObject]@{ HostNode='N1'; State='Running'; CPU=2; AssignedGB=4; DemandGB=3; WasteGB=1 }, " +
            "[PSCustomObject]@{ HostNode='N1'; State='Running'; CPU=4; AssignedGB=8; DemandGB=6; WasteGB=2 }, " +
            "[PSCustomObject]@{ HostNode='N1'; State='Off'; CPU=16; AssignedGB=64; DemandGB=64; WasteGB=0 }, " +
            "[PSCustomObject]@{ HostNode='N2'; State='Running'; CPU=1; AssignedGB=2; DemandGB=1; WasteGB=1 }); " +
            "$rows = @(); " +
            "foreach ($group in ($placement | Group-Object OwnerNode)) { " +
            "$node = [string]$group.Name; " +
            "$ownedGroups = @($group.Group); " +
            "$nodeVmRows = @($vms | Where-Object { $_.HostNode -eq $node -and $_.State -eq 'Running' }); " +
            "$rows += [PSCustomObject]@{ OwnerNode=$node; VMGroups=$ownedGroups.Count; RunningVM=$nodeVmRows.Count; " +
            "vCPU=($nodeVmRows | Measure-Object CPU -Sum).Sum; " +
            "AssignedGB=[math]::Round(($nodeVmRows | Measure-Object AssignedGB -Sum).Sum, 1); " +
            "DemandGB=[math]::Round(($nodeVmRows | Measure-Object DemandGB -Sum).Sum, 1); " +
            "WasteGB=[math]::Round(($nodeVmRows | Measure-Object WasteGB -Sum).Sum, 1); " +
            "HighPriority=@($ownedGroups | Where-Object { $_.Priority -match 'High|3000' }).Count } }; " +
            "$rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var psRows = ParityRow.ParseRows(reference.Json,
            ["VMGroups", "RunningVM", "vCPU", "AssignedGB", "DemandGB", "WasteGB", "HighPriority"]);

        var placement = new[]
        {
            Placement("G1", "N1", "High"), Placement("G2", "N1", "Low"),
            Placement("G3", "N2", "3000"), Placement("G9", "N9", "Medium"),
        };
        var vms = new[]
        {
            Vm("N1", "Running", 2, 4, 3), Vm("N1", "Running", 4, 8, 6),
            Vm("N1", "Off", 16, 64, 64), Vm("N2", "Running", 1, 2, 1),
        };
        var cs = VmDistributionCalculator.Build(placement, vms);
        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["OwnerNode"] = r.OwnerNode,
            ["VMGroups"] = r.VMGroups,
            ["RunningVM"] = r.RunningVM,
            ["vCPU"] = r.VCpu,
            ["AssignedGB"] = r.AssignedGB,
            ["DemandGB"] = r.DemandGB,
            ["WasteGB"] = r.WasteGB,
            ["HighPriority"] = r.HighPriority,
        }));
        var diffs = ParityRow.Diff(psRows,
            ParityRow.ParseRows(csJson, ["VMGroups", "RunningVM", "vCPU", "AssignedGB", "DemandGB", "WasteGB", "HighPriority"]),
            r => $"{r.GetValueOrDefault("OwnerNode")}",
            ["OwnerNode", "VMGroups", "RunningVM", "vCPU", "AssignedGB", "DemandGB", "WasteGB", "HighPriority"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Distribution formula parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task Advisor_ReferenceFormulas()
    {
        const string snippet = "$vms = @(" +
            "[PSCustomObject]@{ HostNode='N1'; State='Running'; CPU=2; AssignedGB=40; DemandGB=30 }, " +
            "[PSCustomObject]@{ HostNode='N2'; State='Running'; CPU=2; AssignedGB=10; DemandGB=85.1 }, " +
            "[PSCustomObject]@{ HostNode='N3'; State='Running'; CPU=2; AssignedGB=90.1; DemandGB=10 }, " +
            "[PSCustomObject]@{ HostNode='N9'; State='Running'; CPU=1; AssignedGB=4; DemandGB=3 }); " +
            "$hw = @(" +
            "[PSCustomObject]@{ Node='N1'; RAMGB=100; FreeRAMGB=50 }, " +
            "[PSCustomObject]@{ Node='N2'; RAMGB=100; FreeRAMGB=10 }, " +
            "[PSCustomObject]@{ Node='N3'; RAMGB=100; FreeRAMGB=5 }); " +
            "$placement = @([PSCustomObject]@{ PreferredOwners='N1, N2' }, [PSCustomObject]@{ PreferredOwners='' }); " +
            "$rows = @(); " +
            "foreach ($group in ($vms | Group-Object HostNode)) { " +
            "$node = [string]$group.Name; $items = @($group.Group); " +
            "$nodeHw = $hw | Where-Object { $_.Node -eq $node } | Select-Object -First 1; " +
            "$ramTotal = if ($nodeHw -and $nodeHw.RAMGB -ne '') { [double]$nodeHw.RAMGB } else { 0 }; " +
            "$demand = [math]::Round(($items | Measure-Object DemandGB -Sum).Sum, 1); " +
            "$assigned = [math]::Round(($items | Measure-Object AssignedGB -Sum).Sum, 1); " +
            "$demandPct = if ($ramTotal -gt 0) { [math]::Round(($demand / $ramTotal) * 100, 1) } else { '' }; " +
            "$assignedPct = if ($ramTotal -gt 0) { [math]::Round(($assigned / $ramTotal) * 100, 1) } else { '' }; " +
            "$severity = 'OK'; $recommendation = 'Vyzerá vyvážene.'; " +
            "if ($demandPct -ne '' -and $demandPct -gt 85) { $severity = 'Warning'; $recommendation = 'Demand RAM je vysoký. Skontroluj failover kapacitu druhého node.' } " +
            "elseif ($assignedPct -ne '' -and $assignedPct -gt 90) { $severity = 'Info'; $recommendation = 'Assigned RAM je vysoký, ale Demand môže byť v poriadku. Sledovať.' }; " +
            "$rows += [PSCustomObject]@{ Severity=$severity; Node=$node; RunningVM=$items.Count; " +
            "vCPU=($items | Measure-Object CPU -Sum).Sum; " +
            "RAMGB=if ($nodeHw) { $nodeHw.RAMGB } else { '' }; " +
            "FreeRAMGB=if ($nodeHw) { $nodeHw.FreeRAMGB } else { '' }; " +
            "DemandGB=$demand; DemandPct=$demandPct; AssignedGB=$assigned; AssignedPct=$assignedPct; Recommendation=$recommendation } }; " +
            "$missing = @($placement | Where-Object { [string]::IsNullOrWhiteSpace($_.PreferredOwners) }); " +
            "if ($missing.Count -gt 0) { $rows += [PSCustomObject]@{ Severity='Info'; Node='Cluster'; RunningVM=''; vCPU=''; " +
            "RAMGB=''; FreeRAMGB=''; DemandGB=''; DemandPct=''; AssignedGB=''; AssignedPct=''; " +
            "Recommendation=('{0} VM role nemá zistených PreferredOwners.' -f $missing.Count) } }; " +
            "$rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var numerics = new[] { "RunningVM", "vCPU", "RAMGB", "FreeRAMGB", "DemandGB", "DemandPct", "AssignedGB", "AssignedPct" };
        var psRows = ParityRow.ParseRows(reference.Json, numerics);

        var vms = new[]
        {
            Vm("N1", "Running", 2, 40, 30), Vm("N2", "Running", 2, 10, 85.1),
            Vm("N3", "Running", 2, 90.1, 10), Vm("N9", "Running", 1, 4, 3),
        };
        var hw = new[] { Hw("N1", 100, 50), Hw("N2", 100, 10), Hw("N3", 100, 5) };
        var placement = new[]
        {
            Placement("G1", "N1", preferred: "N1, N2"), Placement("G2", "N1"),
        };
        var cs = PlacementAdvisorCalculator.Build(vms, hw, placement);
        var csJson = JsonSerializer.Serialize(cs.Select(r => new Dictionary<string, object?>
        {
            ["Severity"] = r.Severity,
            ["Node"] = r.Node,
            ["RunningVM"] = r.RunningVM,
            ["vCPU"] = r.VCpu,
            ["RAMGB"] = r.RAMGB,
            ["FreeRAMGB"] = r.FreeRAMGB,
            ["DemandGB"] = r.DemandGB,
            ["DemandPct"] = r.DemandPct,
            ["AssignedGB"] = r.AssignedGB,
            ["AssignedPct"] = r.AssignedPct,
            ["Recommendation"] = AdvisorText(r.Advice, r.AdviceCount),
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, numerics),
            r => $"{r.GetValueOrDefault("Node")}",
            ["Severity", "Node", "RunningVM", "vCPU", "RAMGB", "FreeRAMGB", "DemandGB", "DemandPct", "AssignedGB", "AssignedPct", "Recommendation"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Advisor formula parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task Failover_ReferenceFormulas()
    {
        const string snippet = "$failed = 'N1'; " +
            "$vms = @(" +
            "[PSCustomObject]@{ HostNode='N1'; State='Running'; CPU=4; AssignedGB=20; DemandGB=16 }, " +
            "[PSCustomObject]@{ HostNode='N2'; State='Running'; CPU=2; AssignedGB=30; DemandGB=20 }, " +
            "[PSCustomObject]@{ HostNode='N1'; State='Off'; CPU=8; AssignedGB=32; DemandGB=32 }); " +
            "$hw = @([PSCustomObject]@{ Node='N1'; RAMGB=64; FreeRAMGB=20 }, [PSCustomObject]@{ Node='N2'; RAMGB=64; FreeRAMGB=30 }); " +
            "$failedRunning = @($vms | Where-Object { $_.HostNode -eq $failed -and $_.State -eq 'Running' }); " +
            "$needDemand = [math]::Round(($failedRunning | Measure-Object DemandGB -Sum).Sum, 1); " +
            "$needAssigned = [math]::Round(($failedRunning | Measure-Object AssignedGB -Sum).Sum, 1); " +
            "$needVcpu = ($failedRunning | Measure-Object CPU -Sum).Sum; " +
            "$rows = @(); " +
            "foreach ($target in @('N2')) { " +
            "$targetHw = $hw | Where-Object { $_.Node -eq $target } | Select-Object -First 1; " +
            "$targetRunning = @($vms | Where-Object { $_.HostNode -eq $target -and $_.State -eq 'Running' }); " +
            "$targetDemand = [math]::Round(($targetRunning | Measure-Object DemandGB -Sum).Sum, 1); " +
            "$targetAssigned = [math]::Round(($targetRunning | Measure-Object AssignedGB -Sum).Sum, 1); " +
            "$targetRam = if ($targetHw -and $targetHw.RAMGB -ne '') { [double]$targetHw.RAMGB } else { 0 }; " +
            "$targetFree = if ($targetHw -and $targetHw.FreeRAMGB -ne '') { [double]$targetHw.FreeRAMGB } else { 0 }; " +
            "$afterDemand = [math]::Round($targetDemand + $needDemand, 1); " +
            "$afterAssigned = [math]::Round($targetAssigned + $needAssigned, 1); " +
            "$afterDemandPct = if ($targetRam -gt 0) { [math]::Round(($afterDemand / $targetRam) * 100, 1) } else { '' }; " +
            "$afterAssignedPct = if ($targetRam -gt 0) { [math]::Round(($afterAssigned / $targetRam) * 100, 1) } else { '' }; " +
            "$freeAfter = if ($targetRam -gt 0) { [math]::Round($targetRam - $afterDemand, 1) } else { '' }; " +
            "$status = 'OK'; $advice = 'Failover podľa Demand RAM vyzerá priechodne.'; " +
            "if ($targetRam -gt 0 -and $afterDemandPct -gt 95) { $status = 'Critical'; $advice = 'Po failoveri by Demand RAM prekročil bezpečnú hranicu. Nutné znížiť RAM, presunúť VM alebo navýšiť kapacitu.' } " +
            "elseif ($targetRam -gt 0 -and $afterDemandPct -gt 85) { $status = 'Warning'; $advice = 'Po failoveri bude RAM veľmi tesná. Odporúčané optimalizovať RAM alebo rozloženie VM.' } " +
            "elseif ($targetRam -gt 0 -and $afterAssignedPct -gt 95) { $status = 'Info'; $advice = 'Assigned RAM bude vysoká, ale Demand môže byť OK. Skontroluj dynamickú RAM a reálnu záťaž.' }; " +
            "$rows += [PSCustomObject]@{ FailedNode=$failed; TargetNode=$target; VMsToMove=$failedRunning.Count; " +
            "MoveDemandGB=$needDemand; MoveAssignedGB=$needAssigned; MovevCPU=$needVcpu; TargetRAMGB=$targetRam; TargetFreeOSGB=$targetFree; " +
            "AfterDemandGB=$afterDemand; AfterDemandPct=$afterDemandPct; FreeAfterDemandGB=$freeAfter; Status=$status; Advice=$advice } }; " +
            "$rows";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        var numerics = new[] { "VMsToMove", "MoveDemandGB", "MoveAssignedGB", "MovevCPU", "TargetRAMGB", "TargetFreeOSGB", "AfterDemandGB", "AfterDemandPct", "FreeAfterDemandGB" };
        var psRows = ParityRow.ParseRows(reference.Json, numerics);

        var vms = new[]
        {
            Vm("N1", "Running", 4, 20, 16), Vm("N2", "Running", 2, 30, 20), Vm("N1", "Off", 8, 32, 32),
        };
        var hw = new[]
        {
            new NodeHardwareRow("N1", "", "", "", "", null, "", 0, null, null, 64.0, null, 20.0, null),
            new NodeHardwareRow("N2", "", "", "", "", null, "", 0, null, null, 64.0, null, 30.0, null),
        };
        var cs = FailoverSimulator.Simulate("N1", ["N2"], vms, hw);
        var csJson = JsonSerializer.Serialize(cs.TargetRows.Select(r => new Dictionary<string, object?>
        {
            ["FailedNode"] = r.FailedNode,
            ["TargetNode"] = r.TargetNode,
            ["VMsToMove"] = r.VMsToMove,
            ["MoveDemandGB"] = r.MoveDemandGB,
            ["MoveAssignedGB"] = r.MoveAssignedGB,
            ["MovevCPU"] = r.MoveVCpu,
            ["TargetRAMGB"] = r.TargetRAMGB,
            ["TargetFreeOSGB"] = r.TargetFreeOSGB,
            ["AfterDemandGB"] = r.AfterDemandGB,
            ["AfterDemandPct"] = r.AfterDemandPct,
            ["FreeAfterDemandGB"] = r.FreeAfterDemandGB,
            ["Status"] = r.Status,
            ["Advice"] = FailoverText(r.Advice),
        }));
        var diffs = ParityRow.Diff(psRows, ParityRow.ParseRows(csJson, numerics),
            r => $"{r.GetValueOrDefault("FailedNode")}|{r.GetValueOrDefault("TargetNode")}",
            ["FailedNode", "TargetNode", "VMsToMove", "MoveDemandGB", "MoveAssignedGB", "MovevCPU", "TargetRAMGB", "TargetFreeOSGB", "AfterDemandGB", "AfterDemandPct", "FreeAfterDemandGB", "Status", "Advice"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Failover formula parity failed with {diffs.Count} difference(s).");
    }

    [Fact]
    public async Task HealthScore_ReferenceFormulas()
    {
        const string snippet = "$nodes = @([PSCustomObject]@{ State='Up' }, [PSCustomObject]@{ State='Down' }); " +
            "$resources = @([PSCustomObject]@{ State='Online' }, [PSCustomObject]@{ State='Failed' }); " +
            "$csvs = @([PSCustomObject]@{ FreePercent=10 }); " +
            "$jobs = @([PSCustomObject]@{ Name='J1' }); " +
            "$quorum = @([PSCustomObject]@{ QuorumType='Node Majority' }); " +
            "$vms = @([PSCustomObject]@{ State='Running'; WasteGB=-1 }); " +
            "$score = 100; $checks = @(); " +
            "$down = @($nodes | Where-Object { $_.State -ne 'Up' }); " +
            "if ($down.Count -gt 0) { $score -= 30 }; " +
            "$checks += [PSCustomObject]@{ Area='Nodes'; Status=if($down.Count -eq 0){'OK'}else{'Critical'}; Detail=('{0}/{1} Up' -f (@($nodes|Where-Object State -eq 'Up').Count), $nodes.Count) }; " +
            "$bad = @($resources | Where-Object { $_.State -notin @('Online','Offline') }); " +
            "if ($bad.Count -gt 0) { $score -= 20 }; " +
            "$checks += [PSCustomObject]@{ Area='Resources'; Status=if($bad.Count -eq 0){'OK'}else{'Warning'}; Detail=('{0} not OK/pending/failed' -f $bad.Count) }; " +
            "$low = @($csvs | Where-Object { $_.FreePercent -lt 15 }); " +
            "if ($low.Count -gt 0) { $score -= 15 }; " +
            "$checks += [PSCustomObject]@{ Area='CSV Capacity'; Status=if($low.Count -eq 0){'OK'}else{'Warning'}; Detail=('{0} CSV below 15%' -f $low.Count) }; " +
            "if (@($jobs).Count -gt 0) { $score -= 10 }; " +
            "$checks += [PSCustomObject]@{ Area='Storage Jobs'; Status=if(@($jobs).Count -eq 0){'OK'}else{'Warning'}; Detail=('{0} active/listed' -f @($jobs).Count) }; " +
            "$checks += [PSCustomObject]@{ Area='Quorum/Witness'; Status=if($quorum.Count -gt 0){'OK'}else{'Warning'}; Detail=if($quorum.Count -gt 0){[string]$quorum[0].QuorumType}else{'Unknown'} }; " +
            "if ($quorum.Count -eq 0) { $score -= 10 }; " +
            "$neg = @($vms | Where-Object { $_.WasteGB -lt 0 }); " +
            "if ($neg.Count -gt 0) { $score -= 5 }; " +
            "$checks += [PSCustomObject]@{ Area='VM Memory Pressure'; Status=if($neg.Count -eq 0){'OK'}else{'Info'}; Detail=('{0} VM with Demand > Assigned' -f $neg.Count) }; " +
            "if ($score -lt 0) { $score = 0 }; " +
            "$result = @($checks) + @([PSCustomObject]@{ Score = $score }); $result";
        var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
        if (!reference.IsAvailable)
        {
            ReportNotTestable(Output, reference.Reason);
            return;
        }

        using var document = JsonDocument.Parse(reference.Json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var elements = document.RootElement.EnumerateArray().ToList();
        Assert.True(elements.Count == 7, $"Expected 6 checks + score row, got {elements.Count}.");
        var score = elements[^1].GetProperty("Score").GetInt32();
        var psChecks = elements[..^1].Select(e => ParityRow.Normalize(e, [])).ToList();
        Assert.Equal(20, score); // 100 - 30 - 20 - 15 - 10 - 0 - 5.

        var cs = HealthScoreCalculator.Evaluate(
            [new ClusterNodeRow("N1", "Up", "", "", ""), new ClusterNodeRow("N2", "Down", "", "", "")],
            [new ClusterResourceRow("R1", "Online", "G", "T", "N1"), new ClusterResourceRow("R2", "Failed", "G", "T", "N1")],
            [new CsvRow("C1", "Online", "N1", 100, 10, 90, 10, "C:\\C1")],
            [new StorageJobRow("J1", "Running", "", 1, 1, 1, null)],
            [new QuorumInfo("Node Majority", "")],
            [Vm("N1", "Running", 2, 4, 5)]);
        Assert.Equal(score, cs.Score);
        var csJson = JsonSerializer.Serialize(cs.Checks.Select(r => new Dictionary<string, object?>
        {
            ["Area"] = r.Area, ["Status"] = r.Status, ["Detail"] = r.Detail,
        }));
        var diffs = ParityRow.Diff(psChecks, ParityRow.ParseRows(csJson, []),
            r => $"{r.GetValueOrDefault("Area")}",
            ["Area", "Status", "Detail"]);
        foreach (var diff in diffs)
        {
            Output.WriteLine(diff);
        }

        Assert.True(diffs.Count == 0, $"Health formula parity failed with {diffs.Count} difference(s).");
    }
}
