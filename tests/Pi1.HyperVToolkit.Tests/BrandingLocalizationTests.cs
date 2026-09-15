using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Tests.Parity;
using Pi1.HyperVToolkit.ViewModels;
using static Pi1.HyperVToolkit.Tests.ViewBindingTests;

namespace Pi1.HyperVToolkit.Tests;

// Final polish gates: "π1 Hyper-V Toolkit" branding in user-visible text,
// no Slovak strings in English UI, application icon asset + window wiring.
[Collection("LanguageSerial")]
public sealed class BrandingLocalizationTests
{
    private const string Brand = "π1 Hyper-V Toolkit";

    private sealed class FakeTargets : ITargetNodeResolver
    {
        public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TargetNodeSet(["N1"], []));
    }

    private sealed class FakeQuerier : Infrastructure.Cim.ICimQuerier
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([]);
        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Branding_TitleHelpHtml_UsePiGlyph()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.StartsWith(Brand, UiStrings.Get(AppLanguage.Slovak, "Help_Purpose"), StringComparison.Ordinal);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.StartsWith(Brand, UiStrings.Get(AppLanguage.English, "Help_Purpose"), StringComparison.Ordinal);

            Assert.True(ReportSchemas.TryGet("Dashboard", out var schema));
            var html = ReportHtmlSerializer.Serialize(
                schema, [], "scope", new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), AppLanguage.English);
            Assert.Contains(Brand, html, StringComparison.Ordinal);
            Assert.DoesNotContain("Pi1 Hyper-V Toolkit", html, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void ShellTitle_UsesPiGlyph()
    {
        var scope = new ScopeService("N1");
        var shell = ShellFactory.Create(scope, new ReportService());
        Assert.Equal(Brand, shell.Title);
    }

    [Fact]
    public async Task PendingSection_Localized_NoSlovakInEnglish()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var placeholder = new SectionPlaceholderViewModel("Ph_Diag_T", "Ph_Diag_D");

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            await placeholder.RefreshAsync(CancellationToken.None);
            Assert.Equal("Loading will be available after this section is migrated.", placeholder.StatusText);

            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            await placeholder.RefreshAsync(CancellationToken.None);
            Assert.Equal("Načítanie bude dostupné po migrácii tejto sekcie.", placeholder.StatusText);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void ScopePolicy_Messages_FollowLanguage()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var policy = new ScopePolicy("N1");

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            var noCluster = policy.Evaluate(ScopeMode.Cluster, null, clusterAvailable: false);
            Assert.Contains("Cluster scope is not available", noCluster.Message, StringComparison.Ordinal);
            var noNode = policy.Evaluate(ScopeMode.Node, "  ", clusterAvailable: false);
            Assert.Equal("Enter a node name for Selected node mode.", noNode.Message);

            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var skCluster = policy.Evaluate(ScopeMode.Cluster, null, clusterAvailable: false);
            Assert.Equal(ScopePolicy.ClusterUnavailableMessage, skCluster.Message);
            var skNode = policy.Evaluate(ScopeMode.Node, "  ", clusterAvailable: false);
            Assert.Equal(ScopePolicy.NodeNameRequiredMessage, skNode.Message);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task TargetNodeResolver_Warnings_FollowLanguage()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var snapshot = new CapabilitySnapshot();
            var resolver = new Infrastructure.Scope.TargetNodeResolver(
                new ScopeService("N1"), snapshot, new FakeQuerier(),
                NullLogger<Infrastructure.Scope.TargetNodeResolver>.Instance);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            var empty = await resolver.ResolveAsync(new ScopeState(ScopeMode.Node, ""));
            Assert.Equal(["No node was selected."], empty.Warnings);
            var noCluster = await resolver.ResolveAsync(new ScopeState(ScopeMode.Cluster, "N1"));
            Assert.Single(noCluster.Warnings);
            Assert.StartsWith("Cluster scope is not available", noCluster.Warnings[0], StringComparison.Ordinal);

            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var skEmpty = await resolver.ResolveAsync(new ScopeState(ScopeMode.Node, ""));
            Assert.Equal(["Nebol vybraný žiadny uzol."], skEmpty.Warnings);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    // Compact shell factory for title/window checks (behavioral coverage
    // lives in the phase suites; fakes return empty datasets).
    private static class ShellFactory
    {
        private sealed class Targets : ITargetNodeResolver
        {
            public Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default) =>
                Task.FromResult(new TargetNodeSet(["N1"], []));
        }

        private sealed class HyperV : IHyperVService
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

        private sealed class SystemInfo : ISystemInformationService
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

        private sealed class Storage : IStorageService
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

        private sealed class ClusterInfo : IClusterInfoService
        {
            public Task<Core.Aggregations.DashboardCalculator.ClusterSnapshot> GetClusterSnapshotAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new Core.Aggregations.DashboardCalculator.ClusterSnapshot(string.Empty, []));
        }

        private sealed class Settings : ISettingsService
        {
            public AppSettings Load() => AppSettings.Default;
            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private sealed class Probes : ICapabilityProbeService
        {
            public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "ok"));
            public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new CapabilityReport(CapabilityKind.FailoverCluster, false, "missing"));
            public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "ok"));
        }

        private sealed class Dialog : IExportDialogService
        {
            public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension) => null;
        }

        private sealed class Writer : IExportFileWriter
        {
            public Task WriteAllTextAsync(string path, string content, System.Text.Encoding encoding, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        public static MainViewModel Create(ScopeService scope, ReportService report)
        {
            var cache = new SessionCache();
            var snapshot = new CapabilitySnapshot();
            var policy = new ScopePolicy("N1");
            var targets = new Targets();
            var hyperV = new HyperV();
            var systemInfo = new SystemInfo();
            var storage = new Storage();
            var vmsVm = new VmsViewModel(targets, hyperV, scope, report, cache, NullLogger<VmsViewModel>.Instance);
            var nodesVm = new NodesViewModel(targets, systemInfo, hyperV, scope, report, cache, NullLogger<NodesViewModel>.Instance);
            var dashboardVm = new DashboardViewModel(targets, hyperV, systemInfo, storage,
                new ClusterInfo(), scope, report, cache, NullLogger<DashboardViewModel>.Instance);
            var storageVm = new StorageViewModel(targets, storage, hyperV, scope, report, cache, NullLogger<StorageViewModel>.Instance);
            var networkingVm = new NetworkingViewModel(targets, hyperV, scope, report, cache, NullLogger<NetworkingViewModel>.Instance);
            var clusterVm = new ClusterViewModel(targets, new FakeClusterService(), hyperV, systemInfo, storage,
                scope, report, cache, NullLogger<ClusterViewModel>.Instance);
            var diagnosticsVm = new DiagnosticsViewModel(targets, hyperV, storage, new FakeClusterService(),
                scope, report, cache, NullLogger<DiagnosticsViewModel>.Instance);
            var exportVm = new ExportViewModel(report, scope, targets, hyperV, cache,
                new Dialog(), new Writer(), TimeProvider.System,
                NullLogger<ExportViewModel>.Instance);
            return new MainViewModel(scope, new Settings(), new Probes(),
                new Infrastructure.Security.PrivilegeService(), snapshot, policy,
                vmsVm, nodesVm, dashboardVm, storageVm, networkingVm, clusterVm, diagnosticsVm, exportVm,
                new SettingsViewModel(scope, new Settings(), snapshot, policy, NullLogger<SettingsViewModel>.Instance),
                new HelpViewModel(new Infrastructure.Diagnostics.AppInfoProvider()),
                NullLogger<MainViewModel>.Instance);
        }
    }

    [Fact]
    public void AppIcon_Asset_IsValidMultiSizeIco()
    {
        var modulesDir = PowerShellReference.FindModulesDirectory();
        Assert.NotNull(modulesDir);
        var repoRoot = Directory.GetParent(modulesDir)!.FullName;
        var icoPath = Path.Combine(repoRoot, "src", "Pi1.HyperVToolkit", "Assets", "AppIcon.ico");
        Assert.True(File.Exists(icoPath), $"Missing icon asset: {icoPath}");

        var bytes = File.ReadAllBytes(icoPath);
        Assert.True(bytes.Length > 6, "Icon file is truncated.");
        // ICONDIR: reserved=0, type=1 (icon), count>=1.
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(count >= 4, $"Expected at least 4 icon sizes, found {count}.");
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var offset = 6 + i * 16;
            var width = bytes[offset] == 0 ? 256 : bytes[offset];
            var height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            Assert.Equal(width, height);
            sizes.Add(width);
            var length = BitConverter.ToUInt32(bytes, offset + 8);
            Assert.True(length > 0, $"Icon entry {width}x{height} is empty.");
        }

        foreach (var required in new[] { 16, 32, 48, 256 })
        {
            Assert.Contains(required, sizes);
        }
    }

    [Fact]
    public void MainWindow_TitleAndIcon()
    {
        WpfTestHost.Run(() =>
        {
            var scope = new ScopeService("N1");
            var shell = ShellFactory.Create(scope, new ReportService());
            var window = new Pi1.HyperVToolkit.MainWindow(shell);
            try
            {
                Assert.Equal(Brand, window.Title);
                Assert.NotNull(window.Icon);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2), nameof(MainWindow_TitleAndIcon));
    }
}
