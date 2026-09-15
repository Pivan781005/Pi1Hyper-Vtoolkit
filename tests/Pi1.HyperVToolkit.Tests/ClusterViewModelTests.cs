using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ClusterViewModelTests
{
    private sealed class FixedTargets : ITargetNodeResolver
    {
        public string[] Nodes = ["N1", "N2"];
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(Nodes.ToList(), []));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public List<VirtualMachineRow> VmRows =
        [
            new("N1", "APP1", "Running", 2, 4.0, 3.0, 1.0, "", "", "", null, false, 4.0, 1.0, 8.0),
        ];

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(
                [NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok("N1", VmRows)]);

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

    private sealed class FakeSystemInfo : ISystemInformationService
    {
        public Task<IReadOnlyList<NodeResult<NodeHardwareRow>>> GetNodeHardwareAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<NodeHardwareRow>>>(
            [
                NodeResult<NodeHardwareRow>.Ok("N1",
                    new NodeHardwareRow("N1", "", "", "", "", null, "", 0, null, null, 64.0, null, 32.0, null)),
                NodeResult<NodeHardwareRow>.Ok("N2",
                    new NodeHardwareRow("N2", "", "", "", "", null, "", 0, null, null, 64.0, null, 48.0, null)),
            ]);

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>> GetNodeVolumesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeVolumeRow>>>>([]);
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>> GetNodeAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<NodeNetworkAdapterRow>>>>([]);
    }

    private sealed class FakeStorage : IStorageService
    {
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
        public Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CsvRow>>(
            [
                new CsvRow("CSV1", "Online", "N1", 100.0, 50.0, 50.0, 50.0, @"C:\ClusterStorage\Volume1"),
            ]);
    }

    private static ClusterTopologySnapshot AvailableSnapshot() => new(
        true, [],
        [new ClusterNodeRow("N1", "Up", "", "1", ""), new ClusterNodeRow("N2", "Up", "", "1", "")],
        [new ClusterGroupRow("VM1", "Online", "N1", "VirtualMachine", "High")],
        [new ClusterResourceRow("Virtual Machine VM1", "Online", "VM1", "Virtual Machine", "N1")],
        [new ClusterNetworkRow("Net1", "Up", "InternalAndClient", "10.0.0.0", "255.255.255.0", 1000, true)],
        new QuorumInfo("Node Majority", ""),
        [new WitnessRow("Quorum", "Quorum", "Node Majority", "", "", "", "")],
        [new ClusterEventRow(new DateTime(2026, 1, 1), 1205, "Error", "P", "M")],
        [new VmPlacementRow("VM1", "Online", "N1", "High", ["N1", "N2"], ["N1"], "", "", "")]);

    private static (ClusterViewModel Vm, FakeClusterService Cluster) Create(
        ClusterTopologySnapshot? snapshot = null, ScopeService? scope = null)
    {
        var cluster = new FakeClusterService();
        if (snapshot is not null)
        {
            cluster.Snapshot = snapshot;
        }

        var vm = new ClusterViewModel(
            new FixedTargets(), cluster, new FakeHyperV(), new FakeSystemInfo(), new FakeStorage(),
            scope ?? new ScopeService("N1"), new ReportService(), new SessionCache(),
            NullLogger<ClusterViewModel>.Instance);
        return (vm, cluster);
    }

    [Fact]
    public async Task UnavailableSnapshot_CleanState_NoCrash()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.IsAvailable);
        Assert.False(vm.HasData);
        Assert.Empty(vm.NodeRows);
        Assert.Empty(vm.PlacementRows);
        // No duplicate: StatusText must NOT repeat the banner text.
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public async Task UnavailableSnapshot_BackendDiagnosticNotExposed()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.HasWarnings);
        Assert.Empty(vm.NodeWarnings);
    }

    [Fact]
    public async Task AvailableSnapshot_PartialWarnings_Shown()
    {
        var baseSnapshot = AvailableSnapshot();
        var snapshotWithWarnings = baseSnapshot with
        {
            Warnings = ["Nepodarilo sa načítať cluster eventy."]
        };
        var (vm, _) = Create(snapshotWithWarnings);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.IsAvailable);
        Assert.True(vm.HasWarnings);
        Assert.Contains("Nepodarilo sa načítať cluster eventy.", vm.NodeWarnings);
    }

    [Fact]
    public async Task AvailableSnapshot_PopulatesAllTabs()
    {
        var (vm, _) = Create(AvailableSnapshot());
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.IsAvailable);
        Assert.True(vm.HasData);
        Assert.Equal(2, vm.NodeRows.Count);
        Assert.Single(vm.GroupRows);
        Assert.Single(vm.ResourceRows);
        Assert.Single(vm.NetworkRows);
        Assert.Single(vm.QuorumRows);
        Assert.Single(vm.WitnessRows);
        Assert.Single(vm.EventRows);
        Assert.Single(vm.PlacementRows);
        Assert.Single(vm.DistributionRows);
        Assert.Equal("N1", vm.DistributionRows[0].OwnerNode);
        Assert.Equal(1, vm.DistributionRows[0].RunningVM);
        Assert.NotEmpty(vm.AdviceRows);
        Assert.Single(vm.CsvRows);
        Assert.Equal(100, vm.HealthScore);
        Assert.Equal(6, vm.HealthRows.Count);
        Assert.Equal(["N1", "N2"], vm.FailoverNodeOptions.ToList());
        Assert.Equal("N1", vm.SelectedFailedNode);
        Assert.Single(vm.FailoverRows);
        Assert.Equal("N2", vm.FailoverRows[0].TargetNode);
        Assert.Single(vm.MoveVmRows);
    }

    [Fact]
    public async Task SecondRefresh_UsesCache_ReloadForcesLive()
    {
        var (vm, cluster) = Create(AvailableSnapshot());
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, cluster.Calls);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, cluster.Calls);
        await vm.ReloadAsync(CancellationToken.None);
        Assert.Equal(2, cluster.Calls);
    }

    [Fact]
    public async Task FailoverSourceChange_RecomputesWithoutReload()
    {
        var (vm, cluster) = Create(AvailableSnapshot());
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, cluster.Calls);
        vm.SelectedFailedNode = "N2";
        Assert.Single(vm.FailoverRows);
        Assert.Equal("N2", vm.FailoverRows[0].FailedNode);
        Assert.Equal("N1", vm.FailoverRows[0].TargetNode);
        Assert.Empty(vm.MoveVmRows); // no running VMs on N2 in fixtures
        Assert.Equal(1, cluster.Calls); // pure recompute, no re-query
    }

    [Fact]
    public async Task CancelledRefresh_Propagates()
    {
        var (vm, _) = Create(AvailableSnapshot());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.RefreshAsync(cts.Token));
    }
}
