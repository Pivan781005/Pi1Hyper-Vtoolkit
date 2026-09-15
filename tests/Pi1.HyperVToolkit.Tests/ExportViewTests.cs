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

// Export page STA coverage on the shared WpfTestHost: placeholder is gone,
// page renders with SK/EN labels, six export buttons, report/scope display,
// no stale planned-text, no binding errors (also covered by AllViews).
[Collection("LanguageSerial")]
public sealed class ExportViewTests
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
            Task.FromResult<IReadOnlyList<NodeResult<IReadOnlyList<VirtualMachineRow>>>>([]);
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

    private sealed class FakeDialog : IExportDialogService
    {
        public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension) => null;
    }

    private sealed class FakeWriter : IExportFileWriter
    {
        public Task WriteAllTextAsync(string path, string content, System.Text.Encoding encoding, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static ExportViewModel SeedVm()
    {
        var report = new ReportService();
        report.SetCurrent(
            new List<DashboardRow> { new("Cluster", "OK", "CLU") },
            "Dashboard");
        var vm = new ExportViewModel(
            report, new ScopeService("N1"), new FakeTargets(), new FakeHyperV(), new SessionCache(),
            new FakeDialog(), new FakeWriter(), TimeProvider.System,
            NullLogger<ExportViewModel>.Instance);
        vm.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        return vm;
    }

    [Fact]
    public void ExportView_RendersLabels_SK_EN()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            WpfTestHost.Run(() =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
                var vm = SeedVm();
                var view = new ExportView { DataContext = vm };
                var window = new Window { Content = view, Width = 1000, Height = 700 };
                window.Show();
                Pump(window);

                var texts = FindVisualChildren<TextBlock>(window).Select(t => t.Text).ToList();
                Assert.Contains("Aktuálny výpis", texts);
                Assert.Contains("Kompletný VM report", texts);
                Assert.Contains("Dashboard", texts);
                var buttons = FindVisualChildren<Button>(window).Select(b => b.Content as string).ToList();
                Assert.Equal(6, buttons.Count);
                Assert.Equal(2, buttons.Count(b => b == "Exportovať CSV"));
                Assert.Equal(2, buttons.Count(b => b == "Exportovať HTML"));
                Assert.Equal(2, buttons.Count(b => b == "Exportovať JSON"));

                var allText = string.Join("\n", texts);
                Assert.DoesNotContain("Zatiaľ nie je implementované", allText, StringComparison.Ordinal);
                Assert.DoesNotContain("HTML a JSON export sú plánované", allText, StringComparison.Ordinal);
                Assert.Contains("Lokálny uzol (N1)", texts);

                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                // Production shell re-runs RefreshAsync on language change;
                // mirror that here so bound labels follow without restart.
                vm.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                Pump(window);
                var enTexts = FindVisualChildren<TextBlock>(window).Select(t => t.Text).ToList();
                Assert.Contains("Current report", enTexts);
                Assert.Contains("Full VM report", enTexts);
                Assert.Contains("Local node (N1)", enTexts);
                Assert.DoesNotContain("Lokálny uzol (N1)", enTexts);
                var enButtons = FindVisualChildren<Button>(window).Select(b => b.Content as string).ToList();
                Assert.Contains("Export CSV", enButtons);
                Assert.Contains("Export HTML", enButtons);
                Assert.Contains("Export JSON", enButtons);

                window.Close();
            }, TimeSpan.FromMinutes(2), nameof(ExportView_RendersLabels_SK_EN));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Shell_ExportSection_HostsRealViewModel()
    {
        var scope = new ScopeService("N1");
        var report = new ReportService();
        var cache = new SessionCache();
        var targets = new FakeTargets();
        var hyperV = new FakeHyperV();
        var snapshot = new CapabilitySnapshot();
        var policy = new ScopePolicy("N1");
        var diagnosticsVm = new DiagnosticsViewModel(
            targets, hyperV, new FakeStorageForShell(), new FakeClusterService(),
            scope, report, cache, NullLogger<DiagnosticsViewModel>.Instance);
        var exportVm = new ExportViewModel(
            report, scope, targets, hyperV, cache,
            new FakeDialog(), new FakeWriter(), TimeProvider.System,
            NullLogger<ExportViewModel>.Instance);
        var shell = new MainViewModel(
            scope, new InMemorySettings2(), new FakeProbes2(),
            new Pi1.HyperVToolkit.Infrastructure.Security.PrivilegeService(), snapshot, policy,
            new VmsViewModel(targets, hyperV, scope, report, cache, NullLogger<VmsViewModel>.Instance),
            new NodesViewModel(targets, new FakeSystemInfo2(), hyperV, scope, report, cache, NullLogger<NodesViewModel>.Instance),
            new DashboardViewModel(targets, hyperV, new FakeSystemInfo2(), new FakeStorageForShell(), new FakeClusterInfo2(),
                scope, report, cache, NullLogger<DashboardViewModel>.Instance),
            new StorageViewModel(targets, new FakeStorageForShell(), hyperV, scope, report, cache, NullLogger<StorageViewModel>.Instance),
            new NetworkingViewModel(targets, hyperV, scope, report, cache, NullLogger<NetworkingViewModel>.Instance),
            new ClusterViewModel(targets, new FakeClusterService(), hyperV, new FakeSystemInfo2(), new FakeStorageForShell(),
                scope, report, cache, NullLogger<ClusterViewModel>.Instance),
            diagnosticsVm, exportVm,
            new SettingsViewModel(scope, new InMemorySettings2(), snapshot, policy, NullLogger<SettingsViewModel>.Instance),
            new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider()),
            NullLogger<MainViewModel>.Instance);

        Assert.DoesNotContain(
            shell.NavItems,
            n => n.ViewModel is SectionPlaceholderViewModel placeholder && placeholder.TitleKey == "Ph_Export_T");
        Assert.Contains(shell.NavItems, n => ReferenceEquals(n.ViewModel, exportVm));
    }

    private sealed class FakeStorageForShell : IStorageService
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

    private sealed class FakeSystemInfo2 : ISystemInformationService
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

    private sealed class FakeClusterInfo2 : IClusterInfoService
    {
        public Task<Core.Aggregations.DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Aggregations.DashboardCalculator.ClusterSnapshot(string.Empty, []));
    }

    private sealed class InMemorySettings2 : ISettingsService
    {
        public AppSettings Load() => AppSettings.Default;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeProbes2 : ICapabilityProbeService
    {
        public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "ok"));
        public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.FailoverCluster, false, "missing"));
        public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "ok"));
    }
}
