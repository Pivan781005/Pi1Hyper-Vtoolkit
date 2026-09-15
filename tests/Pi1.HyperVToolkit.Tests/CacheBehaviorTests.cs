using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

// Proves session-cache / auto-load / refresh / failure semantics with
// deterministic fake collectors (no CIM).
public sealed class CacheBehaviorTests
{
    private sealed class CountingHyperV : IHyperVService
    {
        public int VmCalls;
        public int NicCalls;
        public bool FailOneNode;

        private static VirtualMachineRow Vm(string host) =>
            new(host, "APP1", "Running", 2, 4.0, 3.0, 1.0, "", "00155D010203", "LAN",
                null, false, 4.0, 1.0, 8.0);

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VmCalls++;
            var results = nodes.Select(n =>
                FailOneNode && string.Equals(n, "BAD", StringComparison.OrdinalIgnoreCase)
                    ? NodeResult<IReadOnlyList<VirtualMachineRow>>.Fail(n, new NodeError(NodeErrorKind.HostUnreachable, "Host unreachable", "test"))
                    : NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok(n, (IReadOnlyList<VirtualMachineRow>)[Vm(n)]))
                .ToList();
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(results);
        }

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            NicCalls++;
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>>([]);
        }

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

    private sealed class CountingStorage : IStorageService
    {
        public int JobsCalls;
        public bool FailJobs;
        public int FailJobsTimes;
        public List<StorageJobRow> JobsToReturn = [];

        public Task<IReadOnlyList<StorageJobRow>> GetStorageJobsAsync(CancellationToken cancellationToken = default)
        {
            JobsCalls++;
            if (FailJobs || FailJobsTimes > 0)
            {
                FailJobsTimes--;
                throw new InvalidOperationException("Nepodarilo sa načítať Storage Jobs.");
            }

            return Task.FromResult<IReadOnlyList<StorageJobRow>>(JobsToReturn);
        }

        public Task<IReadOnlyList<StoragePoolRow>> GetStoragePoolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoragePoolRow>>([]);
        public Task<IReadOnlyList<VirtualDiskRow>> GetVirtualDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VirtualDiskRow>>([]);
        public Task<IReadOnlyList<PhysicalDiskRow>> GetPhysicalDisksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhysicalDiskRow>>([]);
        public Task<IReadOnlyList<StorageVolumeRow>> GetVolumesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StorageVolumeRow>>([]);
        public Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CsvRow>>([]);
    }

    private sealed class FixedTargets : ITargetNodeResolver
    {
        public string[] Nodes = ["N1"];
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(Nodes.ToList(), []));
    }

    private sealed class FixedCluster : IClusterInfoService
    {
        public Task<Core.Aggregations.DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Aggregations.DashboardCalculator.ClusterSnapshot(string.Empty, []));
    }

    private sealed class FixedSystemInfo : ISystemInformationService
    {
        public Task<IReadOnlyList<NodeResult<NodeHardwareRow>>> GetNodeHardwareAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<NodeHardwareRow>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>> GetNodeVolumesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>> GetNodeAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>>([]);
    }

    private static (VmsViewModel Vm, CountingHyperV HyperV, ScopeService Scope, FixedTargets Targets) CreateVms()
    {
        var hyperV = new CountingHyperV();
        var scope = new ScopeService("N1");
        var targets = new FixedTargets();
        var vm = new VmsViewModel(
            targets, hyperV, scope, new ReportService(), new SessionCache(),
            NullLogger<VmsViewModel>.Instance);
        return (vm, hyperV, scope, targets);
    }

    [Fact]
    public async Task SecondRefresh_UsesCache_NoSecondCollectorCall()
    {
        var (vm, hyperV, _, _) = CreateVms();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.VmCalls);
        Assert.Single(vm.OverviewRows);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.VmCalls);
        Assert.Single(vm.OverviewRows);
    }

    [Fact]
    public async Task Reload_ForcesNewCollectorCall_UpdatesCache()
    {
        var (vm, hyperV, _, _) = CreateVms();
        await vm.RefreshAsync(CancellationToken.None);
        await vm.ReloadAsync(CancellationToken.None);
        Assert.Equal(2, hyperV.VmCalls);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, hyperV.VmCalls);
    }

    [Fact]
    public async Task ScopeChange_DoesNotReuseOldScopeData()
    {
        var (vm, hyperV, scope, targets) = CreateVms();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.VmCalls);
        scope.SetScope(ScopeMode.Node, "OTHER");
        targets.Nodes = ["OTHER"];
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, hyperV.VmCalls);
        Assert.Equal("OTHER", Assert.Single(vm.OverviewRows).HostNode);
    }

    [Fact]
    public async Task CancelledRefresh_Propagates()
    {
        var (vm, _, _, _) = CreateVms();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.RefreshAsync(cts.Token));
    }

    [Fact]
    public async Task PartialNodeFailure_KeepsGoodRows_AndCachesThem()
    {
        var (vm, hyperV, _, targets) = CreateVms();
        hyperV.FailOneNode = true;
        targets.Nodes = ["N1", "BAD"];
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.HasWarnings);
        Assert.Equal("N1", Assert.Single(vm.OverviewRows).HostNode);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, hyperV.VmCalls);
    }

    private static (DashboardViewModel Vm, CountingStorage Storage) CreateDashboard()
    {
        var storage = new CountingStorage();
        var scope = new ScopeService("N1");
        var vm = new DashboardViewModel(
            new FixedTargets(), new CountingHyperV(), new FixedSystemInfo(),
            storage, new FixedCluster(), scope, new ReportService(), new SessionCache(),
            NullLogger<DashboardViewModel>.Instance);
        return (vm, storage);
    }

    [Fact]
    public async Task EmptyJobs_NoFailureBanner_ZeroNone()
    {
        var (vm, storage) = CreateDashboard();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, storage.JobsCalls);
        Assert.False(vm.HasWarnings);
        Assert.Empty(vm.JobRows);
        var summary = Assert.Single(vm.StorageSummaryRows, r => r.Area == "Storage Jobs");
        Assert.Equal(0, summary.Count);
        Assert.Equal("None", summary.Status);
        var dash = Assert.Single(vm.DashboardRows, r => r.Area == "Storage Jobs");
        Assert.Equal("None", dash.Status);
        Assert.Equal("0", dash.Value);
    }

    [Fact]
    public async Task FailingJobs_ShowsWarning_DoesNotPoisonCache()
    {
        var (vm, storage) = CreateDashboard();
        storage.FailJobs = true;
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.HasWarnings);
        Assert.Contains(vm.NodeWarnings, w => w.Contains("Storage Jobs"));

        // Recovery: a later success replaces the failure, warnings clear.
        storage.FailJobs = false;
        await vm.ReloadAsync(CancellationToken.None);
        Assert.False(vm.HasWarnings);
        Assert.Empty(vm.JobRows);
    }

    [Fact]
    public async Task TransientJobsFailure_ThenSuccess_NoBanner_ZeroNone()
    {
        // The real startup scenario: attempt 1 fails transiently, attempt 2
        // succeeds empty. Final state must carry NO load-failure banner.
        var (vm, storage) = CreateDashboard();
        storage.FailJobsTimes = 1;
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.HasWarnings); // first attempt genuinely failed
        await vm.ReloadAsync(CancellationToken.None);
        Assert.Equal(2, storage.JobsCalls);
        Assert.False(vm.HasWarnings);
        Assert.Empty(vm.JobRows);
        var summary = Assert.Single(vm.StorageSummaryRows, r => r.Area == "Storage Jobs");
        Assert.Equal("None", summary.Status);
    }

    [Fact]
    public async Task PersistentJobsFailure_WarningRemains()
    {
        var (vm, storage) = CreateDashboard();
        storage.FailJobs = true;
        await vm.RefreshAsync(CancellationToken.None);
        await vm.ReloadAsync(CancellationToken.None);
        Assert.True(vm.HasWarnings);
        Assert.Contains(vm.NodeWarnings, w => w.Contains("Storage Jobs"));
    }

    [Fact]
    public async Task ActiveJobs_NoLoadWarning_SemanticWarning()
    {
        // An ACTIVE job is not a collector failure: no load banner, but the
        // semantic status must be Warning with a positive count.
        var (vm, storage) = CreateDashboard();
        storage.JobsToReturn.Add(new StorageJobRow("Rebalance", "Running", "", 42, 10, 100, null));
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.HasWarnings);
        Assert.Single(vm.JobRows);
        var summary = Assert.Single(vm.StorageSummaryRows, r => r.Area == "Storage Jobs");
        Assert.Equal(1, summary.Count);
        Assert.Equal("Warning", summary.Status);
    }
}
