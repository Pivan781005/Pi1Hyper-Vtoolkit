using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

// "Current Report" means the currently selected user-visible table: switching
// tabs republishes that tab's rows through IReportService immediately, with
// no provider re-query, and empty tabs replace (never preserve) stale reports.
public sealed class ReportPublicationTests
{
    private sealed class FakeTargets : ITargetNodeResolver
    {
        public string[] Nodes = ["N1"];
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(Nodes.ToList(), []));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public List<VirtualMachineRow> VmRows =
        [
            new("N1", "APP1", "Running", 2, 12.0, 3.0, 9.0, "10.0.0.1", "AA", "SW", null, true, 12.0, 1.0, 12.0),
            new("N1", "APP2", "Off", 1, 2.0, 2.0, 0.0, string.Empty, string.Empty, string.Empty, null, false, 2.0, 1.0, 2.0),
        ];

        public List<VmNetworkRow> NicRows =
        [
            new("N1", "APP1", "10.0.0.1", "AA", "SW", "10.0.0.1"),
            new("N1", "APP2", string.Empty, "BB", "SW", string.Empty),
        ];

        public List<CheckpointRow> CpRows =
        [
            new("N1", "APP1", "CP1", new DateTime(2026, 1, 1, 12, 0, 0), "Standard"),
        ];

        public List<VmSwitchRow> SwitchRows =
        [
            new("N1", "vSwitch", "External", true, "NIC"),
        ];

        public List<VmVlanRow> VlanRows =
        [
            new("N1", "APP1", "NIC", "Access", 20, 0, string.Empty),
        ];

        public int VmCalls;
        public int NicCalls;
        public int CpCalls;
        public int SwitchCalls;
        public int VlanCalls;

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            VmCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(
                [NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok("N1", VmRows)]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            NicCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>>(
                [NodeResult<IReadOnlyList<VmNetworkRow>>.Ok("N1", NicRows)]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            CpCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>>(
                [NodeResult<IReadOnlyList<CheckpointRow>>.Ok("N1", CpRows)]);
        }

        public Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
            IReadOnlyList<string> nodes, IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<HostSettingsRow>>>(
                [NodeResult<HostSettingsRow>.Ok("N1",
                    new HostSettingsRow("N1", 8, 64.0, 32.0, 50.0, true, true, 2, true, "C:\\VMs", "C:\\VHDs"))]);

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>> GetVirtualSwitchesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            SwitchCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>>(
                [NodeResult<IReadOnlyList<VmSwitchRow>>.Ok("N1", SwitchRows)]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>> GetVmAdapterVlansAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            VlanCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>>(
                [NodeResult<IReadOnlyList<VmVlanRow>>.Ok("N1", VlanRows)]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>> GetVmStorageAsync(
            IReadOnlyList<string> nodes, IReadOnlyList<CsvRow> csvRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>>(
                [NodeResult<IReadOnlyList<VmStorageRow>>.Ok("N1",
                    [new VmStorageRow("N1", "APP1", "Running", "SCSI 0:0", "VHDX", "Dynamic",
                        10.0, 8.0, "CSV1", 50.0, 50.0, "C:\\disk.vhdx")])]);
    }

    private sealed class FakeSystemInfo : ISystemInformationService
    {
        public int HwCalls;
        public int VolCalls;
        public int NicCalls;

        public Task<IReadOnlyList<NodeResult<NodeHardwareRow>>> GetNodeHardwareAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            HwCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<NodeHardwareRow>>>(
                [NodeResult<NodeHardwareRow>.Ok("N1",
                    new NodeHardwareRow("N1", "M", "Model", "OS", "1.0", null, "CPU", 1, 4, 8, 64.0, 32.0, 32.0, 50.0))]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>> GetNodeVolumesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            VolCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>>(
                [NodeResult<IReadOnlyList<NodeVolumeRow>>.Ok("N1",
                    [new NodeVolumeRow("N1", "C:", "Sys", "NTFS", 100.0, 50.0, 50.0)])]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>> GetNodeAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            NicCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>>(
                [NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>.Ok("N1",
                    [new NodeNetworkAdapterRow("N1", "NIC", "10.0.0.5", "CC", "10.0.0.1", "8.8.8.8", true)])]);
        }
    }

    private sealed class FakeStorage : IStorageService
    {
        public List<CsvRow> CsvRows =
        [
            new("CSV1", "Online", "N1", 100.0, 50.0, 50.0, 50.0, "C:\\CSV1"),
        ];

        public int CsvCalls;

        public Task<IReadOnlyList<StorageJobRow>> GetStorageJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StorageJobRow>>(
                [new StorageJobRow("J1", "Running", string.Empty, 10, 100, 200, TimeSpan.FromMinutes(1))]);
        public Task<IReadOnlyList<StoragePoolRow>> GetStoragePoolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoragePoolRow>>(
                [new StoragePoolRow("P1", "Healthy", "OK", 100.0, 40.0, 60.0)]);
        public Task<IReadOnlyList<VirtualDiskRow>> GetVirtualDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VirtualDiskRow>>(
                [new VirtualDiskRow("VD1", "Healthy", "OK", "Mirror", "Fixed", 50.0, 20.0)]);
        public Task<IReadOnlyList<PhysicalDiskRow>> GetPhysicalDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhysicalDiskRow>>([]);
        public Task<IReadOnlyList<StorageVolumeRow>> GetVolumesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StorageVolumeRow>>(
                [new StorageVolumeRow("C", "Sys", "NTFS", "Healthy", "OK", 100.0, 50.0, 50.0)]);
        public Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(CancellationToken cancellationToken = default)
        {
            CsvCalls++;
            return Task.FromResult<IReadOnlyList<CsvRow>>(CsvRows);
        }
    }

    private sealed class FakeDashboardCluster : IClusterInfoService
    {
        public Task<DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardCalculator.ClusterSnapshot("CLU", [("N1", "Up")]));
    }

    private static ClusterTopologySnapshot ClusterSnapshot(bool withGroups = true) => new(
        true, [],
        [new ClusterNodeRow("N1", "Up", "NotInitiated", "1", "")],
        withGroups ? [new ClusterGroupRow("VM1", "Online", "N1", "VirtualMachine", "High")] : [],
        [new ClusterResourceRow("R1", "Online", "VM1", "Virtual Machine", "N1")],
        [new ClusterNetworkRow("Net1", "Up", "InternalAndClient", "10.0.0.0", "255.255.255.0", 1000, true)],
        new QuorumInfo("Node Majority", "Disk1"),
        [new WitnessRow("Quorum", "W1", "File Share Witness", "Online", "G", "N1", "Share=X")],
        [new ClusterEventRow(new DateTime(2026, 5, 1, 12, 0, 0), 1205, "Error", "P", "M")],
        [new VmPlacementRow("VM1", "Online", "N1", "High", ["N1"], ["N1"], "No", "Yes", "00:00")]);

    [Fact]
    public async Task Vms_TabSwitch_PublishesVisibleTable_NoRequery()
    {
        var report = new ReportService();
        var hyperV = new FakeHyperV();
        var vm = new VmsViewModel(
            new FakeTargets(), hyperV, new ScopeService("N1"), report, new SessionCache(),
            NullLogger<VmsViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("VirtualMachines", report.CurrentName);
        Assert.Equal(2, report.Count);
        var calls = (hyperV.VmCalls, hyperV.NicCalls, hyperV.CpCalls);

        vm.SelectedTabIndex = 1; // Network
        Assert.Equal("VMNetwork", report.CurrentName);
        Assert.Equal(2, report.Count);

        vm.SelectedTabIndex = 3; // Checkpoints
        Assert.Equal("VMCheckpoints", report.CurrentName);
        Assert.Single(report.GetRows<CheckpointRow>());

        vm.SelectedTabIndex = 4; // Without IP
        Assert.Equal("VMWithoutIP", report.CurrentName);
        Assert.Single(report.GetRows<VmNetworkRow>());

        vm.SelectedTabIndex = 2; // Memory (distinct visible columns)
        Assert.Equal("VMMemory", report.CurrentName);
        Assert.Equal(2, report.Count);

        Assert.Equal(calls, (hyperV.VmCalls, hyperV.NicCalls, hyperV.CpCalls));
    }

    [Fact]
    public async Task Vms_OverviewToMemory_ChangesResultName_NoRequery()
    {
        var report = new ReportService();
        var hyperV = new FakeHyperV();
        var vm = new VmsViewModel(
            new FakeTargets(), hyperV, new ScopeService("N1"), report, new SessionCache(),
            NullLogger<VmsViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("VirtualMachines", report.CurrentName);
        var calls = (hyperV.VmCalls, hyperV.NicCalls, hyperV.CpCalls);

        vm.SelectedTabIndex = 2;
        Assert.Equal("VMMemory", report.CurrentName);
        Assert.Equal(2, report.Count);
        Assert.Equal(calls, (hyperV.VmCalls, hyperV.NicCalls, hyperV.CpCalls));

        vm.SelectedTabIndex = 0;
        Assert.Equal("VirtualMachines", report.CurrentName);
        Assert.Equal(calls, (hyperV.VmCalls, hyperV.NicCalls, hyperV.CpCalls));
    }

    [Fact]
    public async Task Vms_FilterChange_RepublishesFilteredOverview()
    {
        var report = new ReportService();
        var vm = new VmsViewModel(
            new FakeTargets(), new FakeHyperV(), new ScopeService("N1"), report, new SessionCache(),
            NullLogger<VmsViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, report.Count);

        vm.StateFilter = "Running";
        Assert.Equal("VirtualMachines", report.CurrentName);
        Assert.Single(report.GetRows<VirtualMachineRow>());
    }

    [Fact]
    public async Task Nodes_TabSwitch_PublishesVisibleTable_NoRequery()
    {
        var report = new ReportService();
        var systemInfo = new FakeSystemInfo();
        var vm = new NodesViewModel(
            new FakeTargets(), systemInfo, new FakeHyperV(), new ScopeService("N1"), report, new SessionCache(),
            NullLogger<NodesViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("NodeHardware", report.CurrentName);
        Assert.Single(report.GetRows<NodeHardwareRow>());
        var calls = (systemInfo.HwCalls, systemInfo.VolCalls, systemInfo.NicCalls);

        vm.SelectedTabIndex = 1; // Capacity
        Assert.Equal("NodeCapacity", report.CurrentName);
        Assert.Single(report.GetRows<NodeCapacityRow>());

        vm.SelectedTabIndex = 2; // Volumes
        Assert.Equal("NodeVolumes", report.CurrentName);

        vm.SelectedTabIndex = 3; // Adapters
        Assert.Equal("NodeAdapters", report.CurrentName);

        vm.SelectedTabIndex = 4; // Host settings
        Assert.Equal("HostSettings", report.CurrentName);
        Assert.Single(report.GetRows<HostSettingsRow>());

        Assert.Equal(calls, (systemInfo.HwCalls, systemInfo.VolCalls, systemInfo.NicCalls));
    }

    [Fact]
    public async Task Networking_TabSwitch_PublishesVisibleTable_NoRequery()
    {
        var report = new ReportService();
        var hyperV = new FakeHyperV();
        var vm = new NetworkingViewModel(
            new FakeTargets(), hyperV, new ScopeService("N1"), report, new SessionCache(),
            NullLogger<NetworkingViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        // Default tab is Switches (index 0) — published on load.
        Assert.Equal("VmSwitches", report.CurrentName);
        Assert.Single(report.GetRows<VmSwitchRow>());
        var calls = (hyperV.SwitchCalls, hyperV.NicCalls, hyperV.VlanCalls);

        vm.SelectedTabIndex = 1; // Adapters
        Assert.Equal("VMNetwork", report.CurrentName);
        Assert.Equal(2, report.Count);

        vm.SelectedTabIndex = 2; // VLAN
        Assert.Equal("VmVlans", report.CurrentName);
        Assert.Single(report.GetRows<VmVlanRow>());

        Assert.Equal(calls, (hyperV.SwitchCalls, hyperV.NicCalls, hyperV.VlanCalls));
    }

    [Fact]
    public async Task Storage_TabSwitch_PublishesVisibleTable_NoRequery()
    {
        var report = new ReportService();
        var hyperV = new FakeHyperV();
        var storage = new FakeStorage();
        var vm = new StorageViewModel(
            new FakeTargets(), storage, hyperV, new ScopeService("N1"), report, new SessionCache(),
            NullLogger<StorageViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("StorageJobs", report.CurrentName); // default tab 0
        Assert.Single(report.GetRows<StorageJobRow>());
        var vmCalls = hyperV.VmCalls;
        var csvCalls = storage.CsvCalls;

        vm.SelectedTabIndex = 4; // Physical Disks (empty here)
        Assert.Equal("PhysicalDisks", report.CurrentName);
        Assert.Equal(0, report.Count);

        vm.SelectedTabIndex = 6; // CSV
        Assert.Equal("CSV", report.CurrentName);
        Assert.Single(report.GetRows<CsvRow>());

        vm.SelectedTabIndex = 8; // VM map
        Assert.Equal("VMStorageMap", report.CurrentName);
        Assert.Single(report.GetRows<VmStorageRow>());

        vm.SelectedTabIndex = 9; // By CSV
        Assert.Equal("VMStorageByCsv", report.CurrentName);

        vm.SelectedTabIndex = 1; // Summary
        Assert.Equal("StorageSummary", report.CurrentName);

        Assert.Equal(vmCalls, hyperV.VmCalls);
        Assert.Equal(csvCalls, storage.CsvCalls);
    }

    [Fact]
    public async Task Storage_EmptyTab_ReplacesStaleReport()
    {
        var report = new ReportService();
        var vm = new StorageViewModel(
            new FakeTargets(), new FakeStorage(), new FakeHyperV(), new ScopeService("N1"), report, new SessionCache(),
            NullLogger<StorageViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("StorageJobs", report.CurrentName);
        Assert.Equal(1, report.Count);

        vm.SelectedTabIndex = 4; // PhysicalDisks has zero rows in fixtures
        Assert.Equal("PhysicalDisks", report.CurrentName);
        Assert.Equal(0, report.Count);
    }

    [Fact]
    public async Task Cluster_TabSwitch_PublishesVisibleTable_NoRequery()
    {
        var report = new ReportService();
        var cluster = new FakeClusterService { Snapshot = ClusterSnapshot() };
        var vm = new ClusterViewModel(
            new FakeTargets(), cluster, new FakeHyperV(), new FakeSystemInfo(), new FakeStorage(),
            new ScopeService("N1"), report, new SessionCache(),
            NullLogger<ClusterViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("ClusterNodes", report.CurrentName);
        Assert.Single(report.GetRows<ClusterNodeRow>());
        Assert.Equal(1, cluster.Calls);

        vm.SelectedTabIndex = 1; // Roles
        Assert.Equal("ClusterRoles", report.CurrentName);
        Assert.Single(report.GetRows<ClusterGroupRow>());

        vm.SelectedTabIndex = 6; // Events
        Assert.Equal("ClusterEvents", report.CurrentName);
        Assert.Single(report.GetRows<ClusterEventRow>());

        vm.SelectedTabIndex = 8; // Preferred owners: same rows, own columns
        Assert.Equal("ClusterPreferredOwners", report.CurrentName);
        Assert.Single(report.GetRows<VmPlacementRow>());

        vm.SelectedTabIndex = 12; // Health
        Assert.Equal("HealthChecks", report.CurrentName);

        vm.SelectedTabIndex = 13; // CSV
        Assert.Equal("CSV", report.CurrentName);

        Assert.Equal(1, cluster.Calls);
    }

    [Fact]
    public async Task Cluster_OwnershipToPreferredOwners_ChangesResultName_NoRequery()
    {
        var report = new ReportService();
        var cluster = new FakeClusterService { Snapshot = ClusterSnapshot() };
        var vm = new ClusterViewModel(
            new FakeTargets(), cluster, new FakeHyperV(), new FakeSystemInfo(), new FakeStorage(),
            new ScopeService("N1"), report, new SessionCache(),
            NullLogger<ClusterViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        vm.SelectedTabIndex = 7; // Ownership
        Assert.Equal("ClusterPlacement", report.CurrentName);
        Assert.Single(report.GetRows<VmPlacementRow>());

        vm.SelectedTabIndex = 8; // Preferred Owners
        Assert.Equal("ClusterPreferredOwners", report.CurrentName);
        Assert.Single(report.GetRows<VmPlacementRow>());

        Assert.Equal(1, cluster.Calls);
    }

    [Fact]
    public async Task Cluster_EmptyTab_ReplacesStaleReport()
    {
        var report = new ReportService();
        var cluster = new FakeClusterService { Snapshot = ClusterSnapshot(withGroups: false) };
        var vm = new ClusterViewModel(
            new FakeTargets(), cluster, new FakeHyperV(), new FakeSystemInfo(), new FakeStorage(),
            new ScopeService("N1"), report, new SessionCache(),
            NullLogger<ClusterViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("ClusterNodes", report.CurrentName);
        Assert.Equal(1, report.Count);

        vm.SelectedTabIndex = 1; // Roles: zero rows here
        Assert.Equal("ClusterRoles", report.CurrentName);
        Assert.Equal(0, report.Count);
    }

    [Fact]
    public async Task Diagnostics_EmptyTab_ReplacesStaleReport()
    {
        var report = new ReportService();
        var storage = new FakeStorage { CsvRows = [] };
        var vm = new DiagnosticsViewModel(
            new FakeTargets(), new FakeHyperV(), storage, new FakeClusterService(),
            new ScopeService("N1"), report, new SessionCache(),
            NullLogger<DiagnosticsViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("Advisor", report.CurrentName);
        Assert.True(report.Count > 0);

        vm.SelectedTabIndex = 1; // CSV Low Free: zero rows here
        Assert.Equal("CSVLowFree", report.CurrentName);
        Assert.Equal(0, report.Count);
    }

    [Fact]
    public async Task Dashboard_TabSwitch_PublishesVisibleTable()
    {
        var report = new ReportService();
        var vm = new DashboardViewModel(
            new FakeTargets(), new FakeHyperV(), new FakeSystemInfo(), new FakeStorage(), new FakeDashboardCluster(),
            new ScopeService("N1"), report, new SessionCache(),
            NullLogger<DashboardViewModel>.Instance);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("Dashboard", report.CurrentName);

        vm.SelectedTabIndex = 2; // VM summary
        Assert.Equal("VmSummary", report.CurrentName);
        Assert.Equal(6, report.Count);

        vm.SelectedTabIndex = 5; // CSV
        Assert.Equal("CSV", report.CurrentName);
        Assert.Single(report.GetRows<CsvRow>());

        vm.SelectedTabIndex = 4; // Jobs
        Assert.Equal("StorageJobs", report.CurrentName);
    }
}
