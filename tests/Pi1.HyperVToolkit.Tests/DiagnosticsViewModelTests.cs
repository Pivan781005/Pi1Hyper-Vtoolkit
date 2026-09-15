using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

public sealed class DiagnosticsViewModelTests
{
    private sealed class FixedTargets : ITargetNodeResolver
    {
        public string[] Nodes = ["N1"];
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(Nodes.ToList(), []));
    }

    private sealed class FailingTargets : ITargetNodeResolver
    {
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet([], ["no targets"]));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public List<VirtualMachineRow> VmRows =
        [
            new("N1", "APP1", "Running", 2, 20.0, 5.0, 15.0, "", "", "", null, false, 20.0, 1.0, 20.0),
            new("N1", "SQL01", "Running", 2, 4.0, 5.0, -1.0, "", "", "", null, false, 4.0, 1.0, 8.0),
            new("N1", "OFF1", "Off", 2, 32.0, 4.0, 28.0, "", "", "", null, false, 32.0, 1.0, 32.0),
        ];

        public int Calls;

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(
                [NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok(nodes.Count > 0 ? nodes[0] : "N1", VmRows)]);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
            IReadOnlyList<string> nodes, IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<HostSettingsRow>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>> GetVirtualSwitchesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmSwitchRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>> GetVmAdapterVlansAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmVlanRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>> GetVmStorageAsync(
            IReadOnlyList<string> nodes, IReadOnlyList<CsvRow> csvRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmStorageRow>>>>([]);
    }

    private sealed class FakeStorage : IStorageService
    {
        public List<CsvRow> CsvRows =
        [
            new("CSV-low", "Online", "N1", 100.0, 10.0, 90.0, 10.0, @"C:\ClusterStorage\Low"),
            new("CSV-ok", "Online", "N1", 100.0, 80.0, 20.0, 80.0, @"C:\ClusterStorage\Ok"),
        ];

        public int Calls;

        public Task<IReadOnlyList<StorageJobRow>> GetStorageJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StorageJobRow>>([]);
        public Task<IReadOnlyList<StoragePoolRow>> GetStoragePoolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoragePoolRow>>([]);
        public Task<IReadOnlyList<VirtualDiskRow>> GetVirtualDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VirtualDiskRow>>([]);
        public Task<IReadOnlyList<PhysicalDiskRow>> GetPhysicalDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhysicalDiskRow>>([]);
        public Task<IReadOnlyList<StorageVolumeRow>> GetVolumesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StorageVolumeRow>>([]);
        public Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult<IReadOnlyList<CsvRow>>(CsvRows);
        }
    }

    private static ClusterTopologySnapshot AvailableSnapshot() => new(
        true, [],
        [new ClusterNodeRow("N1", "Up", "", "1", "")],
        [],
        [
            new ClusterResourceRow("Good", "Online", "G1", "Virtual Machine", "N1"),
            new ClusterResourceRow("Bad", "Failed", "G1", "Virtual Machine", "N1"),
            new ClusterResourceRow("Down", "Offline", "G2", "IP Address", "N1"),
        ],
        [], null, [], [], []);

    private static (DiagnosticsViewModel Vm, FakeHyperV HyperV, FakeStorage Storage, FakeClusterService Cluster, ReportService Report)
        Create(
            ClusterTopologySnapshot? snapshot = null,
            ITargetNodeResolver? targets = null,
            ISessionCache? cache = null,
            ScopeService? scope = null)
    {
        var cluster = new FakeClusterService();
        if (snapshot is not null)
        {
            cluster.Snapshot = snapshot;
        }

        var hyperV = new FakeHyperV();
        var storage = new FakeStorage();
        var report = new ReportService();
        var vm = new DiagnosticsViewModel(
            targets ?? new FixedTargets(), hyperV, storage, cluster,
            scope ?? new ScopeService("N1"), report, cache ?? new SessionCache(),
            NullLogger<DiagnosticsViewModel>.Instance);
        return (vm, hyperV, storage, cluster, report);
    }

    [Fact]
    public async Task UnavailableCluster_AdvisorStillWorks_OneBannerState()
    {
        var (vm, _, _, _, _) = Create();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.IsClusterAvailable);
        // Advisor consumes local Hyper-V data: 2 Running findings (APP1 reserve, SQL01 pressure).
        Assert.Equal(2, vm.AdvisorRows.Count);
        // CSV reuses the shared snapshot (2 rows, 1 below 20 %).
        Assert.Single(vm.CsvLowFreeRows);
        Assert.Equal("CSV-low", vm.CsvLowFreeRows[0].Name);
        // Cluster resources cleanly empty with no crash.
        Assert.Empty(vm.ResourceRows);
        // No raw backend English leak.
        Assert.DoesNotContain(vm.NodeWarnings, w => w.Contains("Cluster capability unavailable on this machine."));
        Assert.False(vm.HasWarnings);
    }

    [Fact]
    public async Task AvailableCluster_PopulatesAllThreeTabs_PublishesAdvisorByDefault()
    {
        var (vm, _, _, _, report) = Create(AvailableSnapshot());
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.IsClusterAvailable);
        Assert.Equal(2, vm.AdvisorRows.Count);
        Assert.Single(vm.CsvLowFreeRows);
        Assert.Equal(2, vm.ResourceRows.Count);
        Assert.Equal(["Bad", "Down"], vm.ResourceRows.Select(r => r.Name));
        Assert.Equal("Advisor", report.CurrentName);
        Assert.Equal(2, report.Count);
    }

    [Fact]
    public async Task TabSelection_PublishesMatchingResultName()
    {
        var (vm, _, _, _, report) = Create(AvailableSnapshot());
        await vm.RefreshAsync(CancellationToken.None);
        vm.SelectedTabIndex = 1;
        Assert.Equal("CSVLowFree", report.CurrentName);
        Assert.Equal(1, report.Count);
        vm.SelectedTabIndex = 2;
        Assert.Equal("ResourcesNotOnline", report.CurrentName);
        Assert.Equal(2, report.Count);
        vm.SelectedTabIndex = 0;
        Assert.Equal("Advisor", report.CurrentName);
    }

    [Fact]
    public async Task SecondRefresh_UsesCache_ReloadForcesLive()
    {
        var cache = new SessionCache();
        var (vm, hyperV, storage, cluster, _) = Create(AvailableSnapshot(), cache: cache);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.Calls);
        Assert.Equal(1, storage.Calls);
        Assert.Equal(1, cluster.Calls);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.Calls);
        Assert.Equal(1, storage.Calls);
        Assert.Equal(1, cluster.Calls);
        await vm.ReloadAsync(CancellationToken.None);
        Assert.Equal(2, hyperV.Calls);
        Assert.Equal(2, storage.Calls);
        Assert.Equal(2, cluster.Calls);
    }

    [Fact]
    public async Task ScopeChange_UsesDifferentCacheKey()
    {
        var cache = new SessionCache();
        var scope = new ScopeService("N1");
        var (vm, hyperV, _, _, _) = Create(AvailableSnapshot(), cache: cache, scope: scope);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.Calls);
        scope.SetScope(ScopeMode.Node, "N2");
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, hyperV.Calls);
    }

    [Fact]
    public async Task CancelledRefresh_Propagates()
    {
        var (vm, _, _, _, _) = Create(AvailableSnapshot());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.RefreshAsync(cts.Token));
    }

    [Fact]
    public void SelectAdvisorTab_SetsIndexZero()
    {
        var (vm, _, _, _, _) = Create();
        vm.SelectedTabIndex = 2;
        vm.SelectAdvisorTab();
        Assert.Equal(0, vm.SelectedTabIndex);
    }
}
