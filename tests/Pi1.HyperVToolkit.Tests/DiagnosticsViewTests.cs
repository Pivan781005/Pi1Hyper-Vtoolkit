using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;
using Pi1.HyperVToolkit.Views;
using static Pi1.HyperVToolkit.Tests.ViewBindingTests;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Phase 6 STA coverage on the SHARED WpfTestHost (single Application per
/// AppDomain): three tabs render with SK/EN labels, copy menu inherited,
/// recommendation left-aligned, scalars centered, single localized
/// cluster-unavailable state, no raw backend leak, Dashboard shortcut.
/// Non-STA navigation coverage lives here too (no dispatcher needed).
/// </summary>
[Collection("LanguageSerial")]
public sealed class DiagnosticsViewTests
{
    private sealed class FakeTargets : ITargetNodeResolver
    {
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(["N1"], []));
    }

    private sealed class FakeHyperV : IHyperVService
    {
        public Task<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>> GetVirtualMachinesAsync(
            IReadOnlyList<string> nodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>(
                [NodeResult<IReadOnlyList<VirtualMachineRow>>.Ok("N1",
                    [new VirtualMachineRow("N1", "APP1", "Running", 2, 20.0, 5.0, 15.0, "", "", "", null, false, 20.0, 1.0, 20.0)])]);

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
            Task.FromResult<IReadOnlyList<CsvRow>>(
                [new CsvRow("CSV1", "Online", "N1", 100.0, 10.0, 90.0, 10.0, @"C:\ClusterStorage\Volume1")]);
    }

    private static DiagnosticsViewModel SeedVm()
    {
        var vm = new DiagnosticsViewModel(
            new FakeTargets(), new FakeHyperV(), new FakeStorage(), new FakeClusterService(),
            new ScopeService("N1"), new ReportService(), new SessionCache(),
            NullLogger<DiagnosticsViewModel>.Instance);
        SeedDiagnostics(vm);
        vm.IsClusterAvailable = false;
        return vm;
    }

    [Fact]
    public void DiagnosticsView_RendersThreeTabs_SK_EN()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            WpfTestHost.Run(() =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
                var vm = SeedVm();
                var view = new DiagnosticsView { DataContext = vm };
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                Pump(window);

                var tabs = FindVisualChildren<TabControl>(window).FirstOrDefault();
                Assert.NotNull(tabs);
                Assert.Equal(3, tabs.Items.Count);

                var skHeaders = FindVisualChildren<TabItem>(window).Select(t => t.Header as string).ToList();
                Assert.Contains("Advisor", skHeaders);
                Assert.Contains("CSV – málo miesta", skHeaders);
                Assert.Contains("Prostriedky mimo Online", skHeaders);

                foreach (var grid in FindVisualChildren<DataGrid>(window))
                {
                    Assert.NotNull(grid.ContextMenu);
                    Assert.Equal(2, grid.ContextMenu.Items.Count);
                }

                var seenSql = false;
                var seenCsv = false;
                var seenRes = false;
                for (var i = 0; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Pump(window);
                    seenSql |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "SQL01");
                    seenCsv |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "CSV1");
                    seenRes |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "R-bad");
                }

                Assert.True(seenSql, "Seeded Advisor VM 'SQL01' was never rendered.");
                Assert.True(seenCsv, "Seeded CSV 'CSV1' was never rendered.");
                Assert.True(seenRes, "Seeded resource 'R-bad' was never rendered.");
                tabs.SelectedIndex = 0;
                Pump(window);

                var allText = string.Join("\n", FindVisualChildren<TextBlock>(window).Select(t => t.Text));
                Assert.DoesNotContain("Zatiaľ nie je implementované", allText, StringComparison.Ordinal);
                Assert.DoesNotContain("bisect", allText, StringComparison.OrdinalIgnoreCase);

                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                Pump(window);
                var enHeaders = FindVisualChildren<TabItem>(window).Select(t => t.Header as string).ToList();
                Assert.Contains("Advisor", enHeaders);
                Assert.Contains("CSV Low Free", enHeaders);
                Assert.Contains("Resources Not Online", enHeaders);

                window.Close();
            }, TimeSpan.FromMinutes(2), nameof(DiagnosticsView_RendersThreeTabs_SK_EN));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void DiagnosticsView_RecommendationLeftAligned_ScalarsCentered()
    {
        WpfTestHost.Run(() =>
        {
            var vm = SeedVm();
            var view = new DiagnosticsView { DataContext = vm };
            var window = new Window { Content = view, Width = 1400, Height = 800 };
            window.Show();
            Pump(window);

            var tabs = FindVisualChildren<TabControl>(window).First();
            tabs.SelectedIndex = 0;
            Pump(window);

            var grid = FindVisualChildren<DataGrid>(window).First(g => ReferenceEquals(g.ItemsSource, vm.AdvisorRows));
            var recColumn = grid.Columns.OfType<DataGridTextColumn>()
                .First(c => (c.Header as string)?.Contains("odporú") == true || (c.Header as string) == "Recommendation" || (c.Header as string) == "Odporúčanie");
            var left = (Style)grid.TryFindResource("LeftAlignedText");
            Assert.Same(left, recColumn.ElementStyle);

            var vmColumn = grid.Columns.OfType<DataGridTextColumn>()
                .First(c => (c.Binding as System.Windows.Data.Binding)?.Path?.Path == "VM");
            var centered = (Style)grid.TryFindResource("CenteredText");
            Assert.Same(centered, vmColumn.ElementStyle);

            window.Close();
        }, TimeSpan.FromMinutes(2), nameof(DiagnosticsView_RecommendationLeftAligned_ScalarsCentered));
    }

    [Fact]
    public async Task DiagnosticsView_UnavailableState_OneBanner_NoLeak()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var vm = new DiagnosticsViewModel(
                new FakeTargets(), new FakeHyperV(), new FakeStorage(), new FakeClusterService(),
                new ScopeService("N1"), new ReportService(), new SessionCache(),
                NullLogger<DiagnosticsViewModel>.Instance);
            await vm.RefreshAsync(CancellationToken.None);
            Assert.False(vm.IsClusterAvailable);

            WpfTestHost.Run(() =>
            {
                var view = new DiagnosticsView { DataContext = vm };
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                Pump(window);

                var texts = FindVisualChildren<TextBlock>(window).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
                Assert.Equal(1, texts.Count(t =>
                    string.Equals(t, "Failover Cluster nie je na tomto počítači dostupný.", StringComparison.Ordinal)));
                Assert.DoesNotContain(texts, t => t.Contains("Cluster capability unavailable on this machine.", StringComparison.Ordinal));

                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                Pump(window);
                var enTexts = FindVisualChildren<TextBlock>(window).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
                Assert.Equal(1, enTexts.Count(t =>
                    string.Equals(t, "Failover Cluster is not available on this computer.", StringComparison.Ordinal)));

                window.Close();
            }, TimeSpan.FromMinutes(2), nameof(DiagnosticsView_UnavailableState_OneBanner_NoLeak));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Diagnostics_PlaceholderGone_DashboardNavigatesToAdvisor()
    {
        var scope = new ScopeService("N1");
        var report = new ReportService();
        var cache = new SessionCache();
        var targets = new FakeTargets();
        var hyperV = new FakeHyperV();
        var systemInfo = new FakeSystemInfo();
        var storage = new FakeStorage();
        var snapshot = new CapabilitySnapshot();
        var policy = new ScopePolicy("N1");
        var vmsVm = new VmsViewModel(targets, hyperV, scope, report, cache, NullLogger<VmsViewModel>.Instance);
        var nodesVm = new NodesViewModel(targets, systemInfo, hyperV, scope, report, cache, NullLogger<NodesViewModel>.Instance);
        var dashboardVm = new DashboardViewModel(targets, hyperV, systemInfo, storage,
            new FakeDashboardCluster(), scope, report, cache, NullLogger<DashboardViewModel>.Instance);
        var storageVm = new StorageViewModel(targets, storage, hyperV, scope, report, cache, NullLogger<StorageViewModel>.Instance);
        var networkingVm = new NetworkingViewModel(targets, hyperV, scope, report, cache, NullLogger<NetworkingViewModel>.Instance);
        var clusterVm = new ClusterViewModel(targets, new FakeClusterService(), hyperV, systemInfo, storage,
            scope, report, cache, NullLogger<ClusterViewModel>.Instance);
        var diagnosticsVm = new DiagnosticsViewModel(targets, hyperV, storage, new FakeClusterService(),
            scope, report, cache, NullLogger<DiagnosticsViewModel>.Instance);
        var exportVm = new ExportViewModel(report, scope, targets, hyperV, cache,
            new FakeExportDialog2(), new FakeExportWriter2(), TimeProvider.System,
            NullLogger<ExportViewModel>.Instance);
        var shell = new MainViewModel(scope, new InMemorySettings(), new FakeProbes(),
            new Pi1.HyperVToolkit.Infrastructure.Security.PrivilegeService(), snapshot, policy,
            vmsVm, nodesVm, dashboardVm, storageVm, networkingVm, clusterVm, diagnosticsVm, exportVm,
            new SettingsViewModel(scope, new InMemorySettings(), snapshot, policy, NullLogger<SettingsViewModel>.Instance),
            new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider()),
            NullLogger<MainViewModel>.Instance);

        var diagItem = shell.NavItems.First(n => n.ViewModel is DiagnosticsViewModel);
        Assert.NotNull(diagItem);
        Assert.DoesNotContain(shell.NavItems, n => n.ViewModel is SectionPlaceholderViewModel vm && vm.TitleKey == "Ph_Diag_T");

        diagnosticsVm.SelectedTabIndex = 2;
        dashboardVm.GoToAdvisorCommand.Execute(null);
        Assert.Equal(0, diagnosticsVm.SelectedTabIndex);
        Assert.Same(diagnosticsVm, shell.CurrentView);
        diagnosticsVm.SelectedTabIndex = 2;
        shell.GoToDiagnosticsAdvisor();
        Assert.Same(diagnosticsVm, shell.CurrentView);
        Assert.Equal(0, diagnosticsVm.SelectedTabIndex);
    }

    private sealed class FakeDashboardCluster : IClusterInfoService
    {
        public Task<Core.Aggregations.DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Aggregations.DashboardCalculator.ClusterSnapshot(string.Empty, []));
    }

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Load() => AppSettings.Default;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeProbes : ICapabilityProbeService
    {
        public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "ok"));
        public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.FailoverCluster, false, "missing"));
        public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "ok"));
    }

    private sealed class FakeExportDialog2 : IExportDialogService
    {
        public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension) => null;
    }

    private sealed class FakeExportWriter2 : IExportFileWriter
    {
        public Task WriteAllTextAsync(string path, string content, System.Text.Encoding encoding, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
