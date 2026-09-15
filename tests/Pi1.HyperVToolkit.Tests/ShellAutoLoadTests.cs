using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Diagnostics;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Tests;

// Shell orchestration: startup auto-load, navigation auto-load, language
// switching, settings persistence and help freshness (view-model level;
// rendered-view audits live in ViewBindingTests).
[Collection("LanguageSerial")]
public sealed class ShellAutoLoadTests
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
        public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "Hyper-V: OK"));
        public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.FailoverCluster, false, "Cluster tools missing."));
        public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "CIM/WMI: OK"));
    }

    private sealed class FakeTargets : ITargetNodeResolver
    {
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(["TESTNODE"], []));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public int VmCalls;

        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default)
        {
            VmCalls++;
            IReadOnlyList<VirtualMachineRow> rows =
            [
                new("TESTNODE", "APP1", "Running", 2, 4.0, 3.0, 1.0, "", "", "",
                    null, false, 4.0, 1.0, 8.0),
            ];
            return Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(
                [NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok(nodes[0], rows)]);
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

    private sealed class FakeSystemInfo : ISystemInformationService
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
            Task.FromResult<IReadOnlyList<CsvRow>>([]);
    }

    private sealed class FakeCluster : IClusterInfoService
    {
        public Task<Core.Aggregations.DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Aggregations.DashboardCalculator.ClusterSnapshot(string.Empty, []));
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

    private static (MainViewModel Shell, FakeHyperV HyperV, VmsViewModel Vms) CreateShell()
    {
        var scope = new ScopeService("TESTNODE");
        var snapshot = new CapabilitySnapshot();
        var policy = new ScopePolicy("TESTNODE");
        var report = new ReportService();
        var cache = new SessionCache();
        var targets = new FakeTargets();
        var hyperV = new FakeHyperV();
        var systemInfo = new FakeSystemInfo();
        var vmsVm = new VmsViewModel(targets, hyperV, scope, report, cache, NullLogger<VmsViewModel>.Instance);
        var nodesVm = new NodesViewModel(targets, systemInfo, hyperV, scope, report, cache, NullLogger<NodesViewModel>.Instance);
        var dashboardVm = new DashboardViewModel(
            targets, hyperV, systemInfo, new FakeStorage(), new FakeCluster(),
            scope, report, cache, NullLogger<DashboardViewModel>.Instance);
        var storageVm = new StorageViewModel(
            targets, new FakeStorage(), hyperV, scope, report, cache, NullLogger<StorageViewModel>.Instance);
        var networkingVm = new NetworkingViewModel(
            targets, hyperV, scope, report, cache, NullLogger<NetworkingViewModel>.Instance);
        var clusterVm = new ClusterViewModel(
            targets, new FakeClusterService(), hyperV, systemInfo, new FakeStorage(),
            scope, report, cache, NullLogger<ClusterViewModel>.Instance);
        var diagnosticsVm = new DiagnosticsViewModel(
            targets, hyperV, new FakeStorage(), new FakeClusterService(),
            scope, report, cache, NullLogger<DiagnosticsViewModel>.Instance);
        var exportVm = new ExportViewModel(
            report, scope, targets, hyperV, cache,
            new FakeExportDialog(), new FakeExportWriter(), TimeProvider.System,
            NullLogger<ExportViewModel>.Instance);
        var settingsVm = new SettingsViewModel(
            scope, new FakeSettingsService(), snapshot, policy, NullLogger<SettingsViewModel>.Instance);
        var shell = new MainViewModel(
            scope, new FakeSettingsService(), new FakeProbeService(),
            new Pi1.HyperVToolkit.Infrastructure.Security.PrivilegeService(),
            snapshot, policy, vmsVm, nodesVm, dashboardVm, storageVm, networkingVm,
            clusterVm, diagnosticsVm, exportVm,
            settingsVm, new HelpViewModel(new AppInfoProvider()),
            NullLogger<MainViewModel>.Instance);
        return (shell, hyperV, vmsVm);
    }

    [Fact]
    public async Task Startup_AutoLoadsDashboard_WithoutRefresh()
    {
        var (shell, _, _) = CreateShell();
        var dashboard = (DashboardViewModel)shell.NavItems[0].ViewModel;
        Assert.Empty(dashboard.DashboardRows);
        await shell.InitializeAsync(CancellationToken.None);
        Assert.NotEmpty(dashboard.DashboardRows);
        Assert.Equal(11, dashboard.DashboardRows.Count);
        Assert.Equal(LocalizationService.Instance["Loaded_Simple"], dashboard.StatusText);
    }

    [Fact]
    public async Task Navigation_AutoLoadsSection_UsesCacheOnReturn()
    {
        var (shell, hyperV, vmsVm) = CreateShell();
        await shell.InitializeAsync(CancellationToken.None);
        var dashboardCalls = hyperV.VmCalls;
        Assert.True(dashboardCalls >= 1);

        shell.SelectedNavItem = shell.NavItems[1]; // Virtual Machines
        await WaitForAsync(() => vmsVm.OverviewRows.Count > 0, TimeSpan.FromSeconds(10));
        Assert.Equal("APP1", vmsVm.OverviewRows[0].VM);
        Assert.Equal(dashboardCalls, hyperV.VmCalls); // shared snapshot: no new VM call.
        var afterNav = hyperV.VmCalls;

        shell.SelectedNavItem = shell.NavItems[0]; // back to Dashboard
        await WaitForAsync(() => ReferenceEquals(shell.SelectedNavItem?.ViewModel,
            shell.NavItems[0].ViewModel), TimeSpan.FromSeconds(5));
        await Task.Delay(500); // let any (cached) reload settle
        Assert.Equal(afterNav, hyperV.VmCalls); // cache: no new collector call
    }

    [Fact]
    public async Task RefreshButton_ForcesNewCollectorCall()
    {
        var (shell, hyperV, _) = CreateShell();
        await shell.InitializeAsync(CancellationToken.None);
        var before = hyperV.VmCalls;
        await shell.RefreshCurrentCommand.ExecuteAsync(null);
        Assert.True(hyperV.VmCalls > before);
    }

    [Fact]
    public void ShellLabels_FollowLanguage()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var (shell, _, _) = CreateShell();
            Assert.Equal("Virtuálne počítače", shell.NavItems[1].Title);
            Assert.Equal("Lokálny uzol", shell.ScopeOptions[0].Label);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal("Virtual Machines", shell.NavItems[1].Title);
            Assert.Equal("Local node", shell.ScopeOptions[0].Label);
            Assert.Contains("Administrator:", shell.AdminLabel);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task Settings_Save_PersistsLanguage_AppliesImmediately()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var scope = new ScopeService("N1");
            var settings = new FakeSettingsService();
            var vm = new SettingsViewModel(
                scope, settings, new CapabilitySnapshot(), new ScopePolicy("N1"),
                NullLogger<SettingsViewModel>.Instance);
            vm.Language = "en";
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal("en", settings.Stored.Language);
            Assert.Equal(AppLanguage.English, LocalizationService.Instance.CurrentLanguage);
            Assert.Equal("Settings saved.", vm.SaveMessage);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Help_HasCurrentContent_NoStalePhase2Text()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var help = new HelpViewModel(new AppInfoProvider());
            Assert.DoesNotContain("fáza 2", help.Purpose);
            Assert.DoesNotContain("migrácia jednotlivých reportov bude nasledovať", help.Areas);
            Assert.Contains(new AppInfoProvider().ApplicationVersion, help.Purpose);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.DoesNotContain("fáza 2", help.Purpose);
            Assert.Contains("read-only", help.Purpose);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(50);
        }
    }
}
