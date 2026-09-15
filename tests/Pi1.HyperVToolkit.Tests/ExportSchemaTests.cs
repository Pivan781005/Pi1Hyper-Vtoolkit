using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Localization;

namespace Pi1.HyperVToolkit.Tests;

// Report-inventory gate: EVERY exportable user-visible report grid maps to
// exactly one explicit export schema. A visible report added without a schema
// fails here instead of silently falling back to reflection (which would leak
// internal CLR properties into reports). Unknown names stay unsupported.
public sealed class ExportSchemaTests
{
    // Authoritative inventory of every user-visible report table:
    // Dashboard 6, VMs 3 + FullVMReport, Nodes 5, Storage 11, Networking 3,
    // Cluster 14 tabs (Preferred Owners shares ClusterPlacement rows),
    // Diagnostics 3. Failover MoveVmRows and detail panels are intentionally
    // excluded (secondary/detail views, documented in the final report).
    public static readonly string[] VisibleReportNames =
    [
        "Dashboard", "NodeSummary", "VmSummary", "StorageSummary", "StorageJobs", "CSV",
        "VirtualMachines", "VMMemory", "VMCheckpoints", "VMWithoutIP", ReportSchemas.FullVMReportName,
        "NodeHardware", "NodeCapacity", "NodeVolumes", "NodeAdapters", "HostSettings",
        "VMStorageMap", "VMStorageByCsv", "SelectedVmStorage",
        "StoragePools", "VirtualDisks", "PhysicalDisks", "PhysicalDiskSummary", "StorageVolumes",
        "VMNetwork", "VmSwitches", "VmVlans",
        "ClusterNodes", "ClusterRoles", "ClusterResources", "ClusterNetworks",
        "ClusterQuorum", "ClusterWitness", "ClusterEvents", "ClusterPlacement", "ClusterPreferredOwners",
        "VmDistribution", "PlacementAdvice", "FailoverSimulation", "HealthChecks",
        "Advisor", "CSVLowFree", "ResourcesNotOnline",
    ];

    [Fact]
    public void EveryVisibleReport_HasExactlyOneSchema()
    {
        Assert.Equal(VisibleReportNames.Length, VisibleReportNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(43, VisibleReportNames.Length);
        foreach (var name in VisibleReportNames)
        {
            Assert.True(ReportSchemas.TryGet(name, out var schema), $"No export schema for visible report '{name}'.");
            Assert.Equal(name, schema.Name);
            Assert.NotEmpty(schema.Columns);
            Assert.Equal(
                schema.Columns.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count(),
                schema.Columns.Count);
        }
    }

    [Fact]
    public void Catalog_HasNoOrphanSchemas()
    {
        foreach (var name in ReportSchemas.BuiltInNames)
        {
            Assert.Contains(name, VisibleReportNames);
        }

        Assert.Equal(VisibleReportNames.Length, ReportSchemas.BuiltInNames.Count);
    }

    [Fact]
    public void EveryBuiltInReport_HasFriendlyDisplayName()
    {
        foreach (var name in VisibleReportNames)
        {
            Assert.True(ReportSchemas.HasDisplayName(name), $"No friendly display name for '{name}'.");
        }
    }

    [Fact]
    public void UnknownResultName_IsUnsupported()
    {
        Assert.False(ReportSchemas.TryGet("MysteryReport", out _));
        Assert.False(ReportSchemas.TryGet(string.Empty, out _));
    }

    [Fact]
    public void EveryHeaderKey_ExistsInBothLanguages()
    {
        foreach (var name in ReportSchemas.BuiltInNames)
        {
            Assert.True(ReportSchemas.TryGet(name, out var schema), name);
            foreach (var column in schema.Columns)
            {
                Assert.NotEqual(column.HeaderKey, UiStrings.Get(AppLanguage.Slovak, column.HeaderKey));
                Assert.NotEqual(column.HeaderKey, UiStrings.Get(AppLanguage.English, column.HeaderKey));
            }
        }
    }

    [Fact]
    public void NoSchema_ExposesInternalFields()
    {
        // Internal CLR details that must never become report columns. Note:
        // "Advice" alone is a legitimate VISIBLE Failover text column
        // (H_Advice); the internal parts are the Advice enum / AdviceCount,
        // exposed only via localized Recommendation/Advice text. Likewise
        // PreferredOwners/PossibleOwners columns carry the visible display
        // strings (proven by ClusterPlacement_OwnersExportAsDisplayText).
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rule", "IsSensitive", "AdviceCount",
        };
        foreach (var name in ReportSchemas.BuiltInNames)
        {
            Assert.True(ReportSchemas.TryGet(name, out var schema), name);
            foreach (var column in schema.Columns)
            {
                Assert.DoesNotContain(column.Id, forbidden);
                Assert.False(column.Id.EndsWith("Bytes", StringComparison.Ordinal),
                    $"Schema '{name}' leaks raw byte field '{column.Id}'.");
            }
        }
    }

    [Fact]
    public void ClusterPlacement_OwnersExportAsDisplayText()
    {
        Assert.True(ReportSchemas.TryGet("ClusterPlacement", out var schema));
        var rows = new List<object>
        {
            new Core.Models.VmPlacementRow("VM1", "Online", "N1", "High", ["N1", "N2"], ["N1"], "No", "Yes", "00:00"),
        };
        var csv = ReportCsvSerializer.Serialize(schema, rows);
        Assert.Contains("\"N1, N2\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void FullVMReport_UsesSemanticVmBaseFields_InOrder()
    {
        Assert.True(ReportSchemas.TryGet(ReportSchemas.FullVMReportName, out var schema));
        Assert.Equal(
        [
            "HostNode", "VM", "State", "CPU", "AssignedGB", "DemandGB", "WasteGB",
            "IPv4", "MAC", "Switch", "Uptime", "Dynamic", "StartupGB", "MinimumGB", "MaximumGB",
        ],
        schema.Columns.Select(c => c.Id));
    }

    [Fact]
    public void VirtualMachines_IsOverviewOnly_NotFullReport()
    {
        // The Overview grid has no Dynamic/Startup/Minimum/Maximum columns;
        // those live on the Memory tab (VMMemory) and the legacy FullVMReport.
        Assert.True(ReportSchemas.TryGet("VirtualMachines", out var overview));
        Assert.True(ReportSchemas.TryGet("VMMemory", out var memory));
        Assert.True(ReportSchemas.TryGet(ReportSchemas.FullVMReportName, out var full));
        Assert.Equal(
        [
            "HostNode", "VM", "State", "CPU", "AssignedGB", "DemandGB", "WasteGB",
            "IPv4", "MAC", "Switch", "Uptime",
        ],
        overview.Columns.Select(c => c.Id));
        Assert.Equal(
        [
            "HostNode", "VM", "State", "Dynamic", "StartupGB", "MinimumGB", "MaximumGB",
            "AssignedGB", "DemandGB", "WasteGB",
        ],
        memory.Columns.Select(c => c.Id));
        Assert.Equal(15, full.Columns.Select(c => c.Id).Count());
        Assert.NotEqual(
            full.Columns.Select(c => c.Id),
            overview.Columns.Select(c => c.Id));
    }

    [Fact]
    public void Advisor_ExportsVisibleRecommendation_NotRuleFlags()
    {
        Assert.True(ReportSchemas.TryGet("Advisor", out var schema));
        Assert.Equal(
            ["Severity", "HostNode", "VM", "AssignedGB", "DemandGB", "WasteGB", "Recommendation"],
            schema.Columns.Select(c => c.Id));
    }

    [Fact]
    public void ClusterPlacement_SplitMatchesBothVisibleTabs()
    {
        Assert.True(ReportSchemas.TryGet("ClusterPlacement", out var ownership));
        Assert.True(ReportSchemas.TryGet("ClusterPreferredOwners", out var preferred));
        Assert.Equal(
            ["VMGroup", "State", "OwnerNode", "Priority", "PreferredOwners", "PossibleOwners", "AntiAffinity"],
            ownership.Columns.Select(c => c.Id));
        Assert.Equal(
            ["VMGroup", "OwnerNode", "PreferredOwners", "PossibleOwners", "AutoFailback", "FailbackWindow"],
            preferred.Columns.Select(c => c.Id));
    }

    [Fact]
    public void PlacementAdvice_ExportsLocalizedRecommendation_NotEnum()
    {
        Assert.True(ReportSchemas.TryGet("PlacementAdvice", out var schema));
        Assert.Contains(schema.Columns, c => c.Id == "Recommendation");
        Assert.DoesNotContain(schema.Columns, c => c.Id == "Advice");
    }

    [Fact]
    public void FailoverSimulation_AdviceIsText_NotEnum()
    {
        Assert.True(ReportSchemas.TryGet("FailoverSimulation", out var schema));
        var advice = Assert.Single(schema.Columns, c => c.Id == "Advice");
        Assert.Equal(ReportColumnKind.Text, advice.Kind);
    }
}
