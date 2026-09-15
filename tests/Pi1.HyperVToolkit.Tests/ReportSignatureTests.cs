using Pi1.HyperVToolkit.Core.Export;

namespace Pi1.HyperVToolkit.Tests;

// One ResultName MUST represent one visible column signature: every
// exportable tab declares its expected ordered canonical column IDs (read
// from the XAML grids, not from the catalog), and the gate proves each
// schema matches exactly. Two tabs may share a ResultName ONLY with an
// identical signature. This catches union-schema defects such as the former
// ClusterPlacement (Ownership + Preferred Owners) and VirtualMachines
// (Overview + Memory) collisions.
public sealed class ReportSignatureTests
{
    private sealed record VisibleTable(string Page, string Tab, string ResultName, string[] Columns);

    // Authoritative per-tab definitions. Source: the current XAML DataGrid
    // column Bindings in visible order (header keys intentionally omitted —
    // EveryHeaderKey test covers those separately).
    private static readonly VisibleTable[] Tables =
    [
        new("Dashboard", "Overview", "Dashboard",
            ["Area", "Status", "Value"]),
        new("Dashboard", "Nodes", "NodeSummary",
            ["Node", "RunningVM", "OffVM", "VCpu", "LogicalCPU", "RAMGB", "FreeRAMGB", "RAMUsedPct", "AssignedGB", "DemandGB", "WasteGB"]),
        new("Dashboard", "VMSummary", "VmSummary",
            ["Metric", "Value"]),
        new("Dashboard", "StorageSummary", "StorageSummary",
            ["Area", "Count", "Status"]),
        new("Dashboard", "Jobs", "StorageJobs",
            ["Name", "JobState", "JobType", "PercentComplete", "BytesProcessed", "BytesTotal", "ElapsedTime"]),
        new("Dashboard", "CSV", "CSV",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]),

        new("VM", "Overview", "VirtualMachines",
            ["HostNode", "VM", "State", "CPU", "AssignedGB", "DemandGB", "WasteGB", "IPv4", "MAC", "Switch", "Uptime"]),
        new("VM", "Network", "VMNetwork",
            ["HostNode", "VMName", "IPv4", "MacAddress", "SwitchName", "AllIPs"]),
        new("VM", "Memory", "VMMemory",
            ["HostNode", "VM", "State", "Dynamic", "StartupGB", "MinimumGB", "MaximumGB", "AssignedGB", "DemandGB", "WasteGB"]),
        new("VM", "Checkpoints", "VMCheckpoints",
            ["HostNode", "VM", "Name", "Created", "Type"]),
        new("VM", "WithoutIP", "VMWithoutIP",
            ["HostNode", "VMName", "MacAddress", "SwitchName"]),
        new("Export", "FullVMReport", ReportSchemas.FullVMReportName,
            ["HostNode", "VM", "State", "CPU", "AssignedGB", "DemandGB", "WasteGB", "IPv4", "MAC", "Switch", "Uptime", "Dynamic", "StartupGB", "MinimumGB", "MaximumGB"]),

        new("Nodes", "HW", "NodeHardware",
            ["Node", "Manufacturer", "Model", "OS", "Version", "Uptime", "CPUName", "Sockets", "Cores", "LogicalCPU", "RAMGB", "UsedRAMGB", "FreeRAMGB", "RAMUsedPct"]),
        new("Nodes", "Capacity", "NodeCapacity",
            ["Node", "RunningVM", "OffVM", "VCpu", "LogicalCPU", "RAMGB", "FreeRAMGB", "RAMUsedPct", "AssignedGB", "DemandGB", "WasteGB", "AssignedPct", "DemandPct"]),
        new("Nodes", "Volumes", "NodeVolumes",
            ["Node", "Drive", "Label", "FileSystem", "SizeGB", "FreeGB", "FreePercent"]),
        new("Nodes", "Network", "NodeAdapters",
            ["Node", "Description", "IPv4", "MAC", "Gateway", "DNS", "DHCP"]),
        new("Nodes", "Host", "HostSettings",
            ["Node", "LogicalProcessorCount", "MemoryCapacityGB", "FreeRAMGB", "RAMUsedPct", "NumaSpanningEnabled", "MigrationEnabled", "MaxMigrations", "EnhancedSessionMode", "VirtualMachinePath", "VirtualHardDiskPath"]),

        new("Storage", "Jobs", "StorageJobs",
            ["Name", "JobState", "JobType", "PercentComplete", "BytesProcessed", "BytesTotal", "ElapsedTime"]),
        new("Storage", "Summary", "StorageSummary",
            ["Area", "Count", "Status"]),
        new("Storage", "Pools", "StoragePools",
            ["FriendlyName", "HealthStatus", "OperationalStatus", "SizeGB", "AllocatedGB", "FreeGB"]),
        new("Storage", "VirtualDisks", "VirtualDisks",
            ["FriendlyName", "HealthStatus", "OperationalStatus", "ResiliencySettingName", "ProvisioningType", "SizeGB", "AllocatedGB"]),
        new("Storage", "PhysicalDisks", "PhysicalDisks",
            ["FriendlyName", "MediaType", "BusType", "SizeGB", "HealthStatus", "OperationalStatus", "CanPool", "Usage", "SerialNumber"]),
        new("Storage", "DiskSummary", "PhysicalDiskSummary",
            ["Group", "Count", "TotalGB", "Healthy", "NotHealthy", "CanPool"]),
        new("Storage", "CSV", "CSV",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]),
        new("Storage", "Volumes", "StorageVolumes",
            ["DriveLetter", "FileSystemLabel", "FileSystem", "HealthStatus", "OperationalStatus", "SizeGB", "FreeGB", "FreePercent"]),
        new("Storage", "VMMap", "VMStorageMap",
            ["HostNode", "VM", "State", "Controller", "VHDFormat", "VHDType", "VHDSizeGB", "VHDFileGB", "CSV", "CSVFreeGB", "CSVFreePercent", "Path"]),
        new("Storage", "ByCSV", "VMStorageByCsv",
            ["CSV", "VMCount", "DiskCount", "VHDSizeGB", "VHDFileGB", "CSVFreeGB", "CSVFreePercent"]),
        new("Storage", "SelectedVM", "SelectedVmStorage",
            ["HostNode", "VM", "State", "Controller", "VHDFormat", "VHDType", "VHDSizeGB", "VHDFileGB", "CSV", "CSVFreeGB", "CSVFreePercent", "Path"]),

        new("Networking", "Switches", "VmSwitches",
            ["HostNode", "Name", "SwitchType", "AllowManagementOS", "NetworkAdapter"]),
        new("Networking", "Adapters", "VMNetwork",
            ["HostNode", "VMName", "IPv4", "MacAddress", "SwitchName", "AllIPs"]),
        new("Networking", "VLAN", "VmVlans",
            ["HostNode", "VMName", "VMNetworkAdapterName", "OperationMode", "AccessVlanId", "NativeVlanId", "AllowedVlanIdList"]),

        new("Cluster", "Nodes", "ClusterNodes",
            ["Name", "State", "DrainStatus", "NodeWeight", "FaultDomain"]),
        new("Cluster", "Roles", "ClusterRoles",
            ["Name", "State", "OwnerNode", "GroupType", "Priority"]),
        new("Cluster", "Resources", "ClusterResources",
            ["Name", "State", "OwnerGroup", "ResourceType", "OwnerNode"]),
        new("Cluster", "Networks", "ClusterNetworks",
            ["Name", "State", "Role", "Address", "AddressMask", "Metric", "AutoMetric"]),
        new("Cluster", "Quorum", "ClusterQuorum",
            ["QuorumType", "QuorumResource"]),
        new("Cluster", "Witness", "ClusterWitness",
            ["Section", "Name", "Type", "State", "OwnerGroup", "OwnerNode", "Detail"]),
        new("Cluster", "Events", "ClusterEvents",
            ["TimeCreated", "Id", "Level", "Provider", "Message"]),
        new("Cluster", "Ownership", "ClusterPlacement",
            ["VMGroup", "State", "OwnerNode", "Priority", "PreferredOwners", "PossibleOwners", "AntiAffinity"]),
        new("Cluster", "PreferredOwners", "ClusterPreferredOwners",
            ["VMGroup", "OwnerNode", "PreferredOwners", "PossibleOwners", "AutoFailback", "FailbackWindow"]),
        new("Cluster", "Distribution", "VmDistribution",
            ["OwnerNode", "VMGroups", "RunningVM", "VCpu", "AssignedGB", "DemandGB", "WasteGB", "HighPriority"]),
        new("Cluster", "PlacementAdvisor", "PlacementAdvice",
            ["Severity", "Node", "RunningVM", "VCpu", "RAMGB", "FreeRAMGB", "DemandGB", "DemandPct", "AssignedGB", "AssignedPct", "Recommendation"]),
        new("Cluster", "Failover", "FailoverSimulation",
            ["FailedNode", "TargetNode", "VMsToMove", "MoveDemandGB", "MoveAssignedGB", "MoveVCpu", "TargetRAMGB", "TargetFreeOSGB", "AfterDemandGB", "AfterDemandPct", "FreeAfterDemandGB", "Status", "Advice"]),
        new("Cluster", "HealthScore", "HealthChecks",
            ["Area", "Status", "Detail"]),
        new("Cluster", "CSV", "CSV",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]),

        new("Diagnostics", "Advisor", "Advisor",
            ["Severity", "HostNode", "VM", "AssignedGB", "DemandGB", "WasteGB", "Recommendation"]),
        new("Diagnostics", "CsvLowFree", "CSVLowFree",
            ["Name", "State", "OwnerNode", "SizeGB", "FreeGB", "UsedGB", "FreePercent", "Path"]),
        new("Diagnostics", "ResourcesNotOnline", "ResourcesNotOnline",
            ["Name", "State", "OwnerGroup", "ResourceType", "OwnerNode"]),
    ];

    [Fact]
    public void EveryVisibleTable_HasMatchingSchema()
    {
        Assert.Equal(48, Tables.Length); // 47 tabs + export-only FullVMReport
        foreach (var table in Tables)
        {
            Assert.True(
                ReportSchemas.TryGet(table.ResultName, out var schema),
                $"{table.Page}/{table.Tab}: no schema for '{table.ResultName}'.");
            Assert.Equal(
                table.Columns,
                schema.Columns.Select(c => c.Id));
        }
    }

    [Fact]
    public void SharedResultName_RequiresIdenticalSignature()
    {
        foreach (var group in Tables.GroupBy(t => t.ResultName, StringComparer.Ordinal))
        {
            var distinct = group
                .Select(t => string.Join("|", t.Columns))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.True(
                distinct.Count == 1,
                $"ResultName '{group.Key}' covers incompatible signatures: " +
                string.Join(" vs ", group.Select(t => $"{t.Page}/{t.Tab}")));
        }
    }

    [Fact]
    public void NoSchema_IsSilentUnionOfTwoTabs()
    {
        // The two fixed defects, pinned: Ownership != Preferred Owners,
        // Overview != Memory.
        Assert.NotEqual(
            TableColumns("Cluster", "Ownership"),
            TableColumns("Cluster", "PreferredOwners"));
        Assert.NotEqual(
            TableColumns("Cluster", "Ownership"),
            UnionColumns("Cluster", "Ownership", "Cluster", "PreferredOwners"));
        Assert.NotEqual(
            TableColumns("VM", "Overview"),
            TableColumns("VM", "Memory"));
    }

    private static string[] TableColumns(string page, string tab) =>
        Tables.Single(t => t.Page == page && t.Tab == tab).Columns;

    private static string[] UnionColumns(string pageA, string tabA, string pageB, string tabB) =>
        TableColumns(pageA, tabA).Union(TableColumns(pageB, tabB)).ToArray();
}
