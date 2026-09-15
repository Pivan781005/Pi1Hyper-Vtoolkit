using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Shell-level regression tests for the reported scope inconsistency:
/// Cluster scope must never become active without Failover Cluster capability.
/// Serialized with other language-sensitive classes (shell titles/labels).
/// </summary>
[Collection("LanguageSerial")]
public sealed class MainViewModelScopeTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Stored { get; private set; } = AppSettings.Default;
        public AppSettings ToLoad { get; set; } = AppSettings.Default;

        public AppSettings Load() => ToLoad;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Stored = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbeService : ICapabilityProbeService
    {
        public bool ClusterAvailable { get; set; }

        public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "Hyper-V: OK"));

        public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(
                CapabilityKind.FailoverCluster,
                ClusterAvailable,
                ClusterAvailable ? "Cluster: OK" : "Cluster tools missing."));

        public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "CIM/WMI: OK"));
    }

    private sealed class FakeTargetNodeResolver : ITargetNodeResolver
    {
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(["TESTNODE"], []));
    }

    private sealed class FakeHyperVService : IHyperVService
    {
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>([]);

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>> GetVmNetworkAdaptersAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VmNetworkRow>>>>([]);

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>> GetCheckpointsAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<CheckpointRow>>>>([]);

        public Task<IReadOnlyList<NodeResult<HostSettingsRow>>> GetHostSettingsAsync(
            IReadOnlyList<string> nodes,
            IReadOnlyDictionary<string, NodeHardwareRow?> hardwareByNode,
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

    private sealed class FakeSystemInformationService : ISystemInformationService
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

    private static MainViewModel Create(
        FakeSettingsService settings,
        FakeProbeService probes,
        out ScopeService scope)
    {
        scope = new ScopeService("TESTNODE");
        var snapshot = new CapabilitySnapshot();
        var policy = new ScopePolicy("TESTNODE");
        var report = new ReportService();
        var settingsVm = new SettingsViewModel(
            scope, settings, snapshot, policy, NullLogger<SettingsViewModel>.Instance);
        var targets = new FakeTargetNodeResolver();
        var hyperV = new FakeHyperVService();
        var systemInfo = new FakeSystemInformationService();
        var vmsVm = new VmsViewModel(targets, hyperV, scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
        var nodesVm = new NodesViewModel(targets, systemInfo, hyperV, scope, report, new SessionCache(), NullLogger<NodesViewModel>.Instance);
        var dashboardVm = new DashboardViewModel(
            targets, hyperV, systemInfo,
            new FakeStorageService(), new FakeClusterInfoService(),
            scope, report, new SessionCache(), NullLogger<DashboardViewModel>.Instance);
        var storageVm = new StorageViewModel(
            targets, new FakeStorageService(), hyperV,
            scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
        var networkingVm = new NetworkingViewModel(
            targets, hyperV, scope, report, new SessionCache(), NullLogger<NetworkingViewModel>.Instance);
        var clusterVm = new ClusterViewModel(
            targets, new FakeClusterService(), hyperV, systemInfo, new FakeStorageService(),
            scope, report, new SessionCache(), NullLogger<ClusterViewModel>.Instance);
        var diagnosticsVm = new DiagnosticsViewModel(
            targets, hyperV, new FakeStorageService(), new FakeClusterService(),
            scope, report, new SessionCache(), NullLogger<DiagnosticsViewModel>.Instance);
        var exportVm = new ExportViewModel(
            report, scope, targets, hyperV, new SessionCache(),
            new FakeExportDialog(), new FakeExportWriter(), TimeProvider.System,
            NullLogger<ExportViewModel>.Instance);
        return new MainViewModel(
            scope,
            settings,
            probes,
            new Pi1.HyperVToolkit.Infrastructure.Security.PrivilegeService(),
            snapshot,
            policy,
            vmsVm,
            nodesVm,
            dashboardVm,
            storageVm,
            networkingVm,
            clusterVm,
            diagnosticsVm,
            exportVm,
            settingsVm,
            new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider()),
            NullLogger<MainViewModel>.Instance);
    }

    private sealed class FakeStorageService : IStorageService
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
            Task.FromResult<IReadOnlyList<CsvRow>>([]);
    }

    private sealed class FakeClusterInfoService : IClusterInfoService
    {
        public Task<DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardCalculator.ClusterSnapshot(string.Empty, []));
    }

    private sealed class FakeExportDialog : IExportDialogService
    {
        public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension) => null;
    }

    private sealed class FakeExportWriter : IExportFileWriter
    {
        public Task WriteAllTextAsync(string path, string content, System.Text.Encoding encoding, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task Initialize_SavedClusterWithoutCapability_RecoversToLocal()
    {
        var settings = new FakeSettingsService
        {
            ToLoad = new AppSettings(ScopeMode.Cluster, "TESTNODE", true),
        };
        var vm = Create(settings, new FakeProbeService { ClusterAvailable = false }, out var scope);

        await vm.InitializeAsync(CancellationToken.None);

        Assert.Equal(ScopeMode.Local, scope.Current.Mode);
        Assert.Equal("TESTNODE", scope.Current.SelectedNode);
        Assert.Equal(ScopeMode.Local, vm.SelectedScopeMode);
        Assert.False(string.IsNullOrWhiteSpace(vm.ScopeApplyMessage));
        Assert.Equal("Lokálny uzol (TESTNODE)", vm.ScopeLabel);
    }

    [Fact]
    public async Task Initialize_SavedClusterWithCapability_KeepsCluster()
    {
        var settings = new FakeSettingsService
        {
            ToLoad = new AppSettings(ScopeMode.Cluster, "TESTNODE", true),
        };
        var vm = Create(settings, new FakeProbeService { ClusterAvailable = true }, out var scope);

        await vm.InitializeAsync(CancellationToken.None);

        Assert.Equal(ScopeMode.Cluster, scope.Current.Mode);
        Assert.Equal("Všetky uzly klastra", vm.ScopeLabel);
    }

    [Fact]
    public async Task ApplyScope_ClusterWithoutCapability_StaysLocalAndPersistsLocal()
    {
        var settings = new FakeSettingsService();
        var vm = Create(settings, new FakeProbeService { ClusterAvailable = false }, out var scope);
        await vm.InitializeAsync(CancellationToken.None);

        vm.SelectedScopeMode = ScopeMode.Cluster;
        await vm.ApplyScopeCommand.ExecuteAsync(null);

        Assert.Equal(ScopeMode.Local, scope.Current.Mode);
        Assert.Equal("TESTNODE", scope.Current.SelectedNode);
        Assert.Equal(ScopeMode.Local, settings.Stored.ScopeMode);
        Assert.False(string.IsNullOrWhiteSpace(vm.ScopeApplyMessage));
    }

    [Fact]
    public void SelectingNonNodeScope_DisablesNodeInput()
    {
        var settings = new FakeSettingsService();
        var vm = Create(settings, new FakeProbeService(), out _);

        vm.SelectedScopeMode = ScopeMode.Local;
        Assert.False(vm.IsNodeScope);

        vm.SelectedScopeMode = ScopeMode.Cluster;
        Assert.False(vm.IsNodeScope);

        vm.SelectedScopeMode = ScopeMode.Node;
        Assert.True(vm.IsNodeScope);
    }
}
