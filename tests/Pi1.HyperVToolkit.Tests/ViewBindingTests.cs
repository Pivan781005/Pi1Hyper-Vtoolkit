using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Behaviors;
using Pi1.HyperVToolkit.Clipboard;
using Pi1.HyperVToolkit.Converters;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;
using Pi1.HyperVToolkit.Views;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Renders every real view on an STA thread with sample data and fails on any
/// WPF data-binding error. Catches Binding path typos that never crash at runtime.
/// Serialized with other language-sensitive classes (rendered headers test).
/// </summary>
[Collection("LanguageSerial")]
public sealed class ViewBindingTests
{
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _errors;
        public BindingErrorListener(List<string> errors) => _errors = errors;
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                lock (_errors)
                {
                    _errors.Add(message);
                }
            }
        }
    }

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

    /// <summary>
    /// The test host has no Application object, but StaticResource in the views
    /// resolves against Application resources at load time. Exactly one host
    /// application may exist per process and Application is thread-affine, so all
    /// UI tests share a single STA UI thread with its own dispatcher loop.
    /// </summary>
    internal static class WpfTestHost
    {
        private static readonly object Sync = new();
        private static readonly ManualResetEventSlim Ready = new(false);
        private static Thread? _uiThread;
        private static Dispatcher? _dispatcher;

        public static void Run(Action body, TimeSpan timeout, string testName)
        {
            EnsureStarted();
            Exception? error = null;
            using var done = new ManualResetEventSlim(false);
            _dispatcher!.BeginInvoke(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            if (!done.Wait(timeout))
            {
                throw new TimeoutException($"UI test '{testName}' timed out on the shared STA thread.");
            }

            if (error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        private static void EnsureStarted()
        {
            lock (Sync)
            {
                if (_uiThread is not null)
                {
                    return;
                }

                _uiThread = new Thread(() =>
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    Ready.Set();
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "WpfTestHost",
                };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();
                if (!Ready.Wait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Shared STA UI thread did not start.");
                }

                Run(() =>
                {
                    var host = new Application
                    {
                        // The shared host outlives individual test windows.
                        ShutdownMode = ShutdownMode.OnExplicitShutdown,
                    };
                    host.Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
                    host.Resources.Add("InverseBoolToVisibilityConverter", new InverseBoolToVisibilityConverter());
                    host.Resources.Add("SeverityToBrushConverter", new SeverityToBrushConverter());
                    host.Resources.Add("EnumMatchConverter", new EnumMatchConverter());
                    host.Resources.Add("StatusValueConverter", new StatusValueConverter());
                    host.Resources.Add("BoolYesNoConverter", new BoolYesNoConverter());
                    host.Resources.Add("DataSizeConverter", new DataSizeConverter());
                    host.Resources.Add("LocalizedDateConverter", new LocalizedDateConverter());
                    host.Resources.Add("VmSummaryValueConverter", new VmSummaryValueConverter());
                    host.Resources.Add("PlacementAdviceConverter", new PlacementAdviceConverter());
                    host.Resources.Add("FailoverAdviceConverter", new FailoverAdviceConverter());
                    host.Resources.Add("HealthScoreToBrushConverter", new HealthScoreToBrushConverter());
                    host.Resources.Add("AdvisorRecommendationConverter", new AdvisorRecommendationConverter());
                    host.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Pi1.HyperVToolkit;component/Styles/DataGridStyles.xaml"),
                    });
                }, TimeSpan.FromSeconds(30), "host-init");
            }
        }
    }


    [Fact]
    public void AllViews_RenderWithoutBindingErrors()
    {
        var errors = new List<string>();

        WpfTestHost.Run(() =>
        {
            var step = "init";
            var listener = new BindingErrorListener(errors);
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            try
            {
                // Harness smoke test: a bare window must render without WPF errors.
                // If THIS fails, the test host (not the views) cannot host WPF.
                step = "smoke";
                var smoke = new Window { Content = new TextBlock { Text = "smoke" }, Width = 200, Height = 100 };
                smoke.Show();
                smoke.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                smoke.Close();

                var scope = new ScopeService("N1");
                var report = new ReportService();
                var vmsVm = new VmsViewModel(new FakeTargets(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
                SeedVms(vmsVm);
                var nodesVm = new NodesViewModel(new FakeTargets(), new FakeSystemInfo(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<NodesViewModel>.Instance);
                SeedNodes(nodesVm);
                var dashboardVm = new DashboardViewModel(
                    new FakeTargets(), new FakeHyperV(), new FakeSystemInfo(),
                    new FakeStorage(), new FakeCluster(),
                    scope, report, new SessionCache(), NullLogger<DashboardViewModel>.Instance);
                SeedDashboard(dashboardVm);
                var storageVm = new StorageViewModel(
                    new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
                SeedStorage(storageVm);
                var networkingVm = new NetworkingViewModel(
                    new FakeTargets(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<NetworkingViewModel>.Instance);
                SeedNetworking(networkingVm);
                var settingsVm = new SettingsViewModel(scope,
                    new InMemorySettingsService(), new CapabilitySnapshot(), new ScopePolicy("N1"),
                    NullLogger<SettingsViewModel>.Instance);
                var clusterVm = new ClusterViewModel(
                    new FakeTargets(), new FakeClusterService(), new FakeHyperV(), new FakeSystemInfo(),
                    new FakeStorage(), scope, report, new SessionCache(), NullLogger<ClusterViewModel>.Instance);
                SeedCluster(clusterVm);
                var diagnosticsVm = new DiagnosticsViewModel(
                    new FakeTargets(), new FakeHyperV(), new FakeStorage(), new FakeClusterService(),
                    scope, report, new SessionCache(), NullLogger<DiagnosticsViewModel>.Instance);
                SeedDiagnostics(diagnosticsVm);
                var exportVm = new ExportViewModel(report, scope, new FakeTargets(), new FakeHyperV(), new SessionCache(),
                    new FakeExportDialog(), new FakeExportWriter(), TimeProvider.System,
                    NullLogger<ExportViewModel>.Instance);
                exportVm.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

                foreach (var entry in new (string Name, FrameworkElement View, ViewModelBase Context)[]
                         {
                               ("placeholder", new SectionPlaceholderView(), new SectionPlaceholderViewModel("Test", "Poznámka.")),
                               ("vms", new VmsView(), vmsVm),
                               ("nodes", new NodesView(), nodesVm),
                               ("dashboard", new DashboardView(), dashboardVm),
                               ("storage", new StorageView(), storageVm),
                               ("networking", new NetworkingView(), networkingVm),
                               ("cluster", new ClusterView(), clusterVm),
                               ("diagnostics", new DiagnosticsView(), diagnosticsVm),
                               ("export", new ExportView(), exportVm),
                              ("settings", new SettingsView(), settingsVm),
                              ("help", new HelpView(), new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider())),
                         })
                {
                    step = $"datacontext-{entry.Name}";
                    entry.View.DataContext = entry.Context;

                    step = $"show-{entry.Name}";
                    var window = new Window { Content = entry.View, Width = 1200, Height = 800 };
                    window.Show();
                    // Pump the dispatcher so bindings evaluate and grids generate rows.
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"View rendering failed at step '{step}'.", ex);
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            }
        }, TimeSpan.FromMinutes(2), nameof(AllViews_RenderWithoutBindingErrors));

        Assert.True(errors.Count == 0,
            $"WPF binding errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>
    /// Regression test for the Phase 3 incident where the shipped binary rendered
    /// a stale debug placeholder ("bisect") instead of the VM grids: the view must
    /// expose real tabs, a visible DataGrid with the seeded row, the VM name
    /// rendered through bindings, and no debug text anywhere in the visual tree.
    /// Fails loudly on stale binaries as well as on broken XAML.
    /// </summary>
    [Fact]
    public void VmsView_RendersTabsGridRowsAndNoDebugText()
    {
        WpfTestHost.Run(() =>
        {
            var step = "init";
            try
            {
                step = "seed";
                var scope = new ScopeService("N1");
                var report = new ReportService();
                var vmsVm = new VmsViewModel(new FakeTargets(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
                SeedVms(vmsVm);

                step = "show";
                var view = new VmsView { DataContext = vmsVm };
                var window = new Window { Content = view, Width = 1200, Height = 800 };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                step = "tabs";
                var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                Assert.NotNull(tabs);
                Assert.Equal(5, tabs.Items.Count);

                var seenVmName = false;
                var seenDosVm = false;
                var seenOverviewGrid = false;
                for (var i = 0; i < tabs.Items.Count; i++)
                {
                    step = $"tab-{i}";
                    tabs.SelectedIndex = i;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

                    AssertNoDebugText(window, step);

                    foreach (var grid in FindVisualChildren<System.Windows.Controls.DataGrid>(window))
                    {
                        if (ReferenceEquals(grid.ItemsSource, vmsVm.OverviewRows))
                        {
                            seenOverviewGrid = true;
                            Assert.True(grid.IsVisible, "Overview DataGrid is not visible.");
                            Assert.Equal(2, grid.Items.Count);
                        }
                    }

                    seenVmName |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "APP1");
                    seenDosVm |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "DOSVM");
                }

                Assert.True(seenOverviewGrid, "Overview DataGrid bound to OverviewRows was never rendered.");
                Assert.True(seenVmName, "Seeded VM name 'APP1' was never rendered through bindings.");
                Assert.True(seenDosVm, "VM without IP ('DOSVM') was never rendered in the grid.");

                step = "close";
                window.Close();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"VmsView content check failed at step '{step}'.", ex);
            }
        }, TimeSpan.FromMinutes(2), nameof(VmsView_RendersTabsGridRowsAndNoDebugText));
    }

    /// <summary>NodesView counterpart: grids render seeded rows, no debug text.</summary>
    [Fact]
    public void NodesView_RendersGridsAndNoDebugText()
    {
        WpfTestHost.Run(() =>
        {
            var step = "init";
            try
            {
                step = "seed";
                var scope = new ScopeService("N1");
                var report = new ReportService();
                var nodesVm = new NodesViewModel(
                    new FakeTargets(), new FakeSystemInfo(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<NodesViewModel>.Instance);
                SeedNodes(nodesVm);

                step = "show";
                var view = new NodesView { DataContext = nodesVm };
                var window = new Window { Content = view, Width = 1200, Height = 800 };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                step = "tabs";
                var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                Assert.NotNull(tabs);
                Assert.Equal(5, tabs.Items.Count); // Prehľad HW, Kapacita, Zväzky, Sieť, Hostiteľ Hyper-V

                var seenHardwareGrid = false;
                var seenNodeName = false;
                for (var i = 0; i < tabs.Items.Count; i++)
                {
                    step = $"tab-{i}";
                    tabs.SelectedIndex = i;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                    AssertNoDebugText(window, step);

                    foreach (var grid in FindVisualChildren<System.Windows.Controls.DataGrid>(window))
                    {
                        if (ReferenceEquals(grid.ItemsSource, nodesVm.HardwareRows) && grid.Items.Count >= 1)
                        {
                            seenHardwareGrid = true;
                            Assert.True(grid.IsVisible, "Hardware DataGrid is not visible.");
                        }
                    }

                    seenNodeName |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == "N1");
                }

                Assert.True(seenHardwareGrid, "Hardware DataGrid with the seeded row was never rendered.");
                Assert.True(seenNodeName, "Seeded node name 'N1' was never rendered through bindings.");

                step = "close";
                window.Close();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"NodesView content check failed at step '{step}'.", ex);
            }
        }, TimeSpan.FromMinutes(2), nameof(NodesView_RendersGridsAndNoDebugText));
    }

    /// <summary>
    /// Phase 4 structural test: Dashboard / Storage / Networking views expose
    /// the specified tabs and render seeded rows through bindings.
    /// </summary>
    [Fact]
    public void Phase4Views_RenderTabsAndGrids()
    {
        WpfTestHost.Run(() =>
        {
            var step = "init";
            try
            {
                step = "seed";
                var scope = new ScopeService("N1");
                var report = new ReportService();
                var dashboardVm = new DashboardViewModel(
                    new FakeTargets(), new FakeHyperV(), new FakeSystemInfo(),
                    new FakeStorage(), new FakeCluster(),
                    scope, report, new SessionCache(), NullLogger<DashboardViewModel>.Instance);
                SeedDashboard(dashboardVm);
                var storageVm = new StorageViewModel(
                    new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
                SeedStorage(storageVm);
                var networkingVm = new NetworkingViewModel(
                    new FakeTargets(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<NetworkingViewModel>.Instance);
                SeedNetworking(networkingVm);

                foreach (var entry in new (string Name, FrameworkElement View, ViewModelBase Context, int Tabs, string Marker)[]
                         {
                             ("dashboard", new DashboardView(), dashboardVm, 6, "CLU"),
                             ("storage", new StorageView(), storageVm, 11, "CSV1"),
                             ("networking", new NetworkingView(), networkingVm, 3, "vSwitch"),
                         })
                {
                    step = $"show-{entry.Name}";
                    entry.View.DataContext = entry.Context;
                    var window = new Window { Content = entry.View, Width = 1200, Height = 800 };
                    window.Show();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                    step = $"tabs-{entry.Name}";
                    var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                    Assert.NotNull(tabs);
                    Assert.Equal(entry.Tabs, tabs.Items.Count);

                    var seenMarker = false;
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        AssertNoDebugText(window, $"{entry.Name}-{i}");
                        seenMarker |= FindVisualChildren<TextBlock>(window).Any(t => t.Text == entry.Marker);
                    }

                    Assert.True(seenMarker, $"Seeded marker '{entry.Marker}' was never rendered in {entry.Name} view.");

                    step = $"close-{entry.Name}";
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Phase4 view check failed at step '{step}'.", ex);
            }
        }, TimeSpan.FromMinutes(2), nameof(Phase4Views_RenderTabsAndGrids));
    }

    /// <summary>
    /// Cluster page STA test: 14 tabs render seeded rows without binding
    /// errors, Events shows the ProviderName column, Message stays
    /// left-aligned, every grid inherits the shared copy menu, SK/EN labels
    /// switch, and no stale "migration in progress" text remains.
    /// </summary>
    [Fact]
    public void ClusterView_RendersTabsGridsAndCopyMenu()
    {
        var previous = Core.State.LocalizationService.Instance.CurrentLanguage;
        try
        {
            WpfTestHost.Run(() =>
            {
                var step = "init";
                try
                {
                    step = "seed";
                    Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.Slovak);
                    var scope = new ScopeService("N1");
                    var report = new ReportService();
                    var clusterVm = new ClusterViewModel(
                        new FakeTargets(), new FakeClusterService(), new FakeHyperV(), new FakeSystemInfo(),
                        new FakeStorage(), scope, report, new SessionCache(), NullLogger<ClusterViewModel>.Instance);
                    SeedCluster(clusterVm);

                    step = "show";
                    var view = new ClusterView { DataContext = clusterVm };
                    var window = new Window { Content = view, Width = 1500, Height = 850 };
                    window.Show();
                    Pump(window);

                    step = "tabs";
                    var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                    Assert.NotNull(tabs);
                    Assert.Equal(14, tabs.Items.Count);

                    var seenNode = false;
                    var seenProvider = false;
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        step = $"tab-{i}";
                        tabs.SelectedIndex = i;
                        Pump(window);
                        AssertNoDebugText(window, step);
                        foreach (var grid in FindVisualChildren<System.Windows.Controls.DataGrid>(window))
                        {
                            // Shared DataGrid behavior: copy menu on every grid.
                            Assert.NotNull(grid.ContextMenu);
                            Assert.Equal(2, grid.ContextMenu.Items.Count);
                            foreach (var text in FindVisualChildren<TextBlock>(grid)
                                         .Select(t => t.Text)
                                         .Where(t => !string.IsNullOrEmpty(t)))
                            {
                                seenNode |= text == "N1";
                            }
                        }

                        seenProvider |= FindVisualChildren<System.Windows.Controls.DataGrid>(window)
                            .SelectMany(g => g.Columns.OfType<System.Windows.Controls.DataGridTextColumn>())
                            .Any(c => (c.Header as string) == "Poskytovateľ");
                    }

                    Assert.True(seenNode, "Seeded cluster node was never rendered.");
                    Assert.True(seenProvider, "Events grid has no ProviderName (Poskytovateľ) column.");

                    step = "message-left";
                    tabs.SelectedIndex = 6; // Events tab back into view.
                    Pump(window);
                    var messageCell = FindCellByText(window, "Cluster network connectivity lost.");
                    Assert.NotNull(messageCell);
                    var messageText = FindVisualChildren<TextBlock>(messageCell).First();
                    Assert.Equal(HorizontalAlignment.Left, messageText.HorizontalAlignment);

                    step = "english";
                    Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.English);
                    var enHeaders = new List<string>();
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        Pump(window);
                        enHeaders.AddRange(FindVisualChildren<System.Windows.Controls.DataGrid>(window)
                            .SelectMany(g => g.Columns)
                            .Select(c => c.Header as string)
                            .Where(h => !string.IsNullOrEmpty(h))!);
                    }

                    Assert.Contains("Owner group", enHeaders);
                    Assert.Contains("Provider", enHeaders);

                    step = "no-stale-placeholder";
                    var allText = string.Join("\n", FindVisualChildren<TextBlock>(window).Select(t => t.Text));
                    Assert.DoesNotContain("migration in progress", allText, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Migrácia tejto sekcie prebieha", allText, StringComparison.Ordinal);

                    step = "close";
                    window.Close();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Cluster view check failed at step '{step}'.", ex);
                }
            }, TimeSpan.FromMinutes(2), nameof(ClusterView_RendersTabsGridsAndCopyMenu));
        }
        finally
        {
            Core.State.LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>
    /// Unavailable cluster renders EXACTLY ONE localized notice and no raw
    /// backend diagnostic. Drives the real RefreshAsync first (refresh-then-
    /// show: async work happens off-STA, rendering on STA), so this covers
    /// the actual warning lifecycle, not hand-seeded state.
    /// </summary>
    [Fact]
    public async Task ClusterView_UnavailableState()
    {
        var previous = Core.State.LocalizationService.Instance.CurrentLanguage;
        try
        {
            Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.Slovak);

            var scope = new ScopeService("N1");
            var report = new ReportService();
            var clusterVm = new ClusterViewModel(
                new FakeTargets(), new FakeClusterService(), new FakeHyperV(), new FakeSystemInfo(),
                new FakeStorage(), scope, report, new SessionCache(), NullLogger<ClusterViewModel>.Instance);
            // Real path: unavailable snapshot through LoadAsync.
            await clusterVm.RefreshAsync(CancellationToken.None);
            Assert.False(clusterVm.IsAvailable);

            WpfTestHost.Run(() =>
            {
                var view = new ClusterView { DataContext = clusterVm };
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                Pump(window);

                var texts = FindVisualChildren<TextBlock>(window)
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                // 1+2. Localized notice visible EXACTLY ONCE.
                Assert.Equal(1, texts.Count(t =>
                    string.Equals(t, "Failover Cluster nie je na tomto počítači dostupný.", StringComparison.Ordinal)));

                // 3. Raw Infrastructure English diagnostic never leaks into UI.
                Assert.DoesNotContain(
                    texts,
                    t => t.Contains("Cluster capability unavailable on this machine.", StringComparison.Ordinal));

                // 4+5. Tabs and empty grids remain visible.
                var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                Assert.NotNull(tabs);
                Assert.Equal(14, tabs.Items.Count);
                Assert.NotEmpty(FindVisualChildren<System.Windows.Controls.DataGrid>(window));

                // 7. English shows exactly one localized notice, no Slovak copy.
                Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.English);
                Pump(window);
                var enTexts = FindVisualChildren<TextBlock>(window)
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                Assert.Equal(1, enTexts.Count(t =>
                    string.Equals(t, "Failover Cluster is not available on this computer.", StringComparison.Ordinal)));
                Assert.DoesNotContain(
                    enTexts,
                    t => t.Contains("nie je na tomto počítači dostupný", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    enTexts,
                    t => t.Contains("Cluster capability unavailable on this machine.", StringComparison.Ordinal));

                window.Close();
            }, TimeSpan.FromMinutes(2), nameof(ClusterView_UnavailableState));
        }
        finally
        {
            Core.State.LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static void AssertNoDebugText(DependencyObject root, string step)
    {
        var hits = FindVisualChildren<TextBlock>(root)
            .Select(t => t.Text)
            .Where(text => !string.IsNullOrEmpty(text) &&
                text.Contains("bisect", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(hits.Count == 0, $"Debug text found at step '{step}': {string.Join(", ", hits)}");
    }

    internal static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
            {
                yield return match;
            }

            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Load() => AppSettings.Default;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private static void SeedVms(VmsViewModel vm)
    {
        var row = new VirtualMachineRow("N1", "APP1", "Running", 4, 8.0, 7.0, 1.0,
            "192.168.1.10", "00155D010203", "LAN", TimeSpan.FromHours(2), true, 4.0, 1.0, 8.0);
        vm.OverviewRows.Add(row);
        vm.MemoryRows.Add(row);
        vm.SelectedVm = row;
        vm.NetworkRows.Add(new VmNetworkRow("N1", "APP1", "192.168.1.10", "00155D010203", "LAN", "192.168.1.10"));
        vm.WithoutIpRows.Add(new VmNetworkRow("N1", "APP2", string.Empty, "00155D040506", "LAN", string.Empty));
        vm.CheckpointRows.Add(new CheckpointRow("N1", "APP1", "Before patch", new DateTime(2026, 1, 1, 12, 0, 0), "Standard"));
        // DOS edge case: VM with no guest IP must still appear in the grid.
        vm.OverviewRows.Add(new VirtualMachineRow("N1", "DOSVM", "Running", 1, 0.5, 0.5, 0.0,
            string.Empty, string.Empty, string.Empty, null, false, 0.5, 0.5, 0.5));
        vm.StatusText = "Test";
    }

    private static void SeedNodes(NodesViewModel vm)
    {
        var hw = new NodeHardwareRow("N1", "Dell", "R740", "Microsoft Windows Server 2022",
            "10.0.20348", TimeSpan.FromDays(4), "Intel Xeon", 2, 16, 32, 64.0, 32.0, 32.0, 50.0);
        vm.HardwareRows.Add(hw);
        vm.SelectedNode = hw;
        vm.CapacityRows.Add(new NodeCapacityRow("N1", 2, 1, 6, 32, 64.0, 32.0, 50.0, 24.0, 20.0, 4.0, 37.5, 31.3));
        vm.VolumeRows.Add(new NodeVolumeRow("N1", "C:", "System", "NTFS", 100.0, 25.0, 25.0));
        vm.AdapterRows.Add(new NodeNetworkAdapterRow("N1", "Intel NIC", "192.168.1.5", "AA:BB:CC:DD:EE:FF", "192.168.1.1", "8.8.8.8", true));
        var hs = new HostSettingsRow("N1", 32, 64.0, 32.0, 50.0, true, true, 2, true, @"C:\VMs", @"C:\VHDs");
        vm.HostSettingsRows.Add(hs);
        vm.SelectedHostSettings = hs;
        vm.StatusText = "Test";
    }

    private static void SeedDashboard(DashboardViewModel vm)
    {
        vm.DashboardRows.Add(new DashboardRow("Cluster", "OK", "CLU"));
        vm.DashboardRows.Add(new DashboardRow("Warnings", "OK", "0"));
        vm.NodeSummaryRows.Add(new NodeSummaryRow("N1", 1, 0, 2, 8, 32.0, 16.0, 50.0, 4.0, 3.0, 1.0));
        vm.VmSummaryRows.Add(new VmSummaryRow("Total VM", 1));
        vm.StorageSummaryRows.Add(new StorageSummaryRow("CSV", 1, "OK"));
        vm.JobRows.Add(new StorageJobRow("Repair", "Running", string.Empty, 10, 1, 2, null));
        vm.CsvRows.Add(new CsvRow("CSV1", "Online", "N1", 100.0, 50.0, 50.0, 50.0, @"C:\ClusterStorage\Volume1"));
        vm.StatusText = "Test";
    }

    private static void SeedStorage(StorageViewModel vm)
    {
        vm.JobRows.Add(new StorageJobRow("Repair", "Running", string.Empty, 10, 1, 2, TimeSpan.FromMinutes(5)));
        vm.SummaryRows.Add(new StorageSummaryRow("CSV", 1, "OK"));
        vm.PoolRows.Add(new StoragePoolRow("Pool1", "Healthy", "OK", 100.0, 40.0, 60.0));
        vm.VirtualDiskRows.Add(new VirtualDiskRow("VD1", "Healthy", "OK", "Parity", "Thin", 50.0, 20.0));
        vm.PhysicalDiskRows.Add(new PhysicalDiskRow("Disk1", "SSD", "RAID", 223.6, "Healthy", "OK", false, "Auto-Select", "SN1"));
        vm.PhysicalDiskSummaryRows.Add(new PhysicalDiskSummaryRow("SSD, RAID", 1, 223.6, 1, 0, 0));
        vm.CsvRows.Add(new CsvRow("CSV1", "Online", "N1", 100.0, 50.0, 50.0, 50.0, @"C:\ClusterStorage\Volume1"));
        vm.VolumeRows.Add(new StorageVolumeRow("C", "System", "NTFS", "Healthy", "OK", 100.0, 25.0, 25.0));
        var storage = new VmStorageRow("N1", "APP1", "Running", "SCSI 0:0", "VHDX", "Dynamic",
            10.0, 8.0, "CSV1", 50.0, 50.0, @"C:\ClusterStorage\Volume1\disk.vhdx");
        vm.StorageMapRows.Add(storage);
        vm.SelectedVmStorageRows.Add(storage);
        vm.ByCsvRows.Add(new VmStorageByCsvRow("CSV1", 1, 1, 10.0, 8.0, 50.0, 50.0));
        var option = new VirtualMachineRow("N1", "APP1", "Running", 2, 4.0, 3.0, 1.0,
            string.Empty, string.Empty, string.Empty, null, false, 4.0, 1.0, 8.0);
        vm.VmOptions.Add(option);
        vm.SelectedVm = option;
        vm.StatusText = "Test";
    }

    private static void SeedNetworking(NetworkingViewModel vm)
    {
        vm.SwitchRows.Add(new VmSwitchRow("N1", "vSwitch", "External", true, "Intel NIC"));
        vm.AdapterRows.Add(new VmNetworkRow("N1", "APP1", "192.168.1.10", "00155D010203", "vSwitch", "192.168.1.10"));
        vm.VlanRows.Add(new VmVlanRow("N1", "APP1", "Network Adapter", "Access", 20, 0, string.Empty));
        vm.StatusText = "Test";
    }

    private static void SeedCluster(ClusterViewModel vm)
    {
        vm.NodeRows.Add(new ClusterNodeRow("N1", "Up", "NotInitiated", "1", ""));
        vm.NodeRows.Add(new ClusterNodeRow("N2", "Up", "NotInitiated", "1", ""));
        vm.GroupRows.Add(new ClusterGroupRow("VM1", "Online", "N1", "VirtualMachine", "High"));
        vm.ResourceRows.Add(new ClusterResourceRow("Virtual Machine VM1", "Online", "VM1", "Virtual Machine", "N1"));
        vm.NetworkRows.Add(new ClusterNetworkRow("Cluster Network 1", "Up", "InternalAndClient", "10.0.0.0", "255.255.255.0", 1000, true));
        vm.QuorumRows.Add(new QuorumInfo("Node Majority", ""));
        vm.WitnessRows.Add(new WitnessRow("Quorum", "Quorum", "Node Majority", "", "", "", ""));
        vm.EventRows.Add(new ClusterEventRow(new DateTime(2026, 5, 1, 12, 0, 0), 1205, "Error", "Microsoft-Windows-FailoverClustering", "Cluster network connectivity lost."));
        vm.PlacementRows.Add(new VmPlacementRow("VM1", "Online", "N1", "High", ["N1", "N2"], ["N1", "N2"], "", "", ""));
        vm.DistributionRows.Add(new VmDistributionRow("N1", 1, 1, 2, 4.0, 3.0, 1.0, 1));
        vm.AdviceRows.Add(new PlacementAdviceRow("OK", "N1", 1, 2, 64.0, 32.0, 20.0, 31.3, 24.0, 37.5, PlacementAdviceKind.Balanced, 0));
        vm.FailoverNodeOptions.Add("N1");
        vm.FailoverNodeOptions.Add("N2");
        // NOTE: assigning SelectedFailedNode rebuilds (clears) failover rows,
        // so seed them afterwards.
        vm.SelectedFailedNode = "N1";
        vm.FailoverRows.Add(new FailoverRow("N1", "N2", 1, 8.0, 10.0, 4, 64.0, 30.0, 28.0, 43.8, 36.0, "OK", FailoverAdviceKind.Viable));
        vm.MoveVmRows.Add(new VirtualMachineRow("N1", "APP1", "Running", 2, 4.0, 3.0, 1.0, "", "", "", null, false, 4.0, 1.0, 8.0));
        vm.HealthRows.Add(new HealthCheckRow("Nodes", "OK", "2/2 Up"));
        vm.HealthScore = 100;
        vm.CsvRows.Add(new CsvRow("CSV1", "Online", "N1", 100.0, 50.0, 50.0, 50.0, @"C:\ClusterStorage\Volume1"));
        vm.FailoverNodeOptions.Add("N1");
        vm.FailoverNodeOptions.Add("N2");
        vm.SelectedFailedNode = "N1";
        vm.IsAvailable = true;
        vm.HasData = true;
        vm.StatusText = "Test";
    }

    internal static void SeedDiagnostics(DiagnosticsViewModel vm)
    {
        vm.AdvisorRows.Add(new AdvisorRow("Warning", "N1", "SQL01", 4.0, 5.0, -1.0, AdvisorRule.Pressure, true));
        vm.AdvisorRows.Add(new AdvisorRow("Info", "N1", "APP1", 20.0, 5.0, 15.0, AdvisorRule.Reserve, false));
        vm.CsvLowFreeRows.Add(new CsvRow("CSV1", "Online", "N1", 100.0, 10.0, 90.0, 10.0, @"C:\ClusterStorage\Volume1"));
        vm.ResourceRows.Add(new ClusterResourceRow("R-bad", "Failed", "G1", "Virtual Machine", "N1"));
        vm.IsClusterAvailable = false;
        vm.StatusText = "Test";
    }

    private static readonly string[] RawHeaderBlacklist =
    [
        "AssignedGB", "DemandGB", "StartupGB", "MinimumGB", "MaximumGB",
        "FreeRAMGB", "RAMUsedPct", "UsedRAMGB", "OwnerNode", "SwitchType",
        "AllowManagementOS", "SizeGB", "FreeGB", "UsedGB", "HostNode", "VMName",
        "MacAddress", "SwitchName", "AllIPs", "HealthStatus", "OperationalStatus",
        "DriveLetter", "FileSystemLabel",         "SerialNumber", "MediaType", "BusType",
        "VHDSizeGB", "VHDFileGB", "VHDType", "VHDFormat",
        "CSVFreeGB", "CSVFreePercent", "VMCount", "DiskCount", "RunningVM", "OffVM",
        "LogicalCPU", "AssignedPct", "DemandPct", "MaxMig", "VMPath", "VHDPath",
        "PercentComplete", "BytesProcessed", "BytesTotal", "ElapsedTime",
        "JobState", "JobType", "FreePercent", "Dynamic", "RunningVMs",
        "OwnerGroup", "ResourceType", "GroupType", "DrainStatus", "NodeWeight",
        "FaultDomain", "AddressMask", "AutoMetric", "QuorumType", "QuorumResource",
        "VMGroup", "PreferredOwners", "PossibleOwners", "AntiAffinity", "AutoFailback",
        "FailbackWindow",         "VMGroups", "HighPriority",
        "FailedNode", "TargetNode", "VMsToMove", "MoveDemand", "MoveAssigned",
        "MoveVCpu", "TargetRAM", "TargetFree", "AfterDemand", "AfterDemandPct",
        "FreeAfter",
        // NOTE: "Uptime", "Controller", "Role", "Priority", "Message",
        // "Detail", "Section", "Provider", "Score", "Level", "Time", "Id",
        // "Address" and "Metric" are deliberately NOT here — each is the
        // correct English header. The Slovak pass asserts absence separately.
    ];

    /// <summary>
    /// Localization audit on ACTUAL rendered grids: no raw C# property names
    /// in any column header, in Slovak AND English, with spot checks.
    /// </summary>
    [Fact]
    public void Headers_AreLocalized_NoRawPropertyNames()
    {
        var previous = Core.State.LocalizationService.Instance.CurrentLanguage;
        try
        {
            foreach (var language in new[] { Core.Localization.AppLanguage.Slovak, Core.Localization.AppLanguage.English })
            {
                var headers = CollectAllHeaders(language);
                foreach (var header in headers)
                {
                    Assert.DoesNotContain(header, RawHeaderBlacklist);
                    Assert.False(header.EndsWith("GB", StringComparison.Ordinal),
                        $"Header '{header}' still carries a unit suffix ({language}).");
                }

                if (language == Core.Localization.AppLanguage.Slovak)
                {
                    Assert.Contains("Pridelená pamäť", headers);
                    Assert.Contains("Hostiteľský uzol", headers);
                    Assert.DoesNotContain("Uptime", headers);
                    Assert.DoesNotContain("Controller", headers);
                    Assert.DoesNotContain("OwnerGroup", headers);
                    Assert.DoesNotContain("PreferredOwners", headers);
                    Assert.DoesNotContain("FailbackWindow", headers);
                    Assert.DoesNotContain("Severity", headers);
                    Assert.DoesNotContain("Recommendation", headers);
                    Assert.DoesNotContain("Advice", headers);
                }
                else
                {
                    Assert.Contains("Assigned memory", headers);
                    Assert.Contains("Host node", headers);
                    Assert.Contains("Uptime", headers);
                    Assert.Contains("Controller", headers);
                    Assert.Contains("Owner group", headers);
                    Assert.Contains("Preferred owners", headers);
                    Assert.Contains("Failback window", headers);
                }
            }
        }
        finally
        {
            Core.State.LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static List<string> CollectAllHeaders(Core.Localization.AppLanguage language)
    {
        var collected = new List<string>();
        WpfTestHost.Run(() =>
        {
            Core.State.LocalizationService.Instance.SetLanguage(language);
            var scope = new ScopeService("N1");
            var report = new ReportService();
            var vmsVm = new VmsViewModel(new FakeTargets(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
            SeedVms(vmsVm);
            var nodesVm = new NodesViewModel(new FakeTargets(), new FakeSystemInfo(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<NodesViewModel>.Instance);
            SeedNodes(nodesVm);
            var dashboardVm = new DashboardViewModel(
                new FakeTargets(), new FakeHyperV(), new FakeSystemInfo(),
                new FakeStorage(), new FakeCluster(),
                scope, report, new SessionCache(), NullLogger<DashboardViewModel>.Instance);
            SeedDashboard(dashboardVm);
            var storageVm = new StorageViewModel(
                new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
            SeedStorage(storageVm);
                var networkingVm = new NetworkingViewModel(
                    new FakeTargets(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<NetworkingViewModel>.Instance);
                SeedNetworking(networkingVm);
                var clusterVm = new ClusterViewModel(
                    new FakeTargets(), new FakeClusterService(), new FakeHyperV(), new FakeSystemInfo(),
                    new FakeStorage(), scope, report, new SessionCache(), NullLogger<ClusterViewModel>.Instance);
                SeedCluster(clusterVm);
                var diagnosticsVm = new DiagnosticsViewModel(
                    new FakeTargets(), new FakeHyperV(), new FakeStorage(), new FakeClusterService(),
                    scope, report, new SessionCache(), NullLogger<DiagnosticsViewModel>.Instance);
                SeedDiagnostics(diagnosticsVm);

            foreach (FrameworkElement view in new FrameworkElement[]
                     {
                          new VmsView(), new NodesView(), new DashboardView(),
                          new StorageView(), new NetworkingView(), new ClusterView(),
                          new DiagnosticsView(),
                     })
            {
                view.DataContext = view is VmsView ? vmsVm
                    : view is NodesView ? nodesVm
                    : view is DashboardView ? dashboardVm
                    : view is StorageView ? (ViewModelBase)storageVm
                    : view is ClusterView ? (ViewModelBase)clusterVm
                    : view is DiagnosticsView ? (ViewModelBase)diagnosticsVm
                    : networkingVm;
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).FirstOrDefault();
                var pages = tabs is not null ? tabs.Items.Count : 1;
                for (var i = 0; i < pages; i++)
                {
                    if (tabs is not null)
                    {
                        tabs.SelectedIndex = i;
                    }

                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    foreach (var grid in FindVisualChildren<System.Windows.Controls.DataGrid>(window))
                    {
                        foreach (var column in grid.Columns)
                        {
                            if (column.Header is string text && !string.IsNullOrWhiteSpace(text))
                            {
                                collected.Add(text);
                            }
                        }
                    }
                }

                window.Close();
            }
        }, TimeSpan.FromMinutes(3), $"{nameof(Headers_AreLocalized_NoRawPropertyNames)}-{language}");

        return collected.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Global DataGrid policy where practical: shared header/cell styles
    /// center content with padding; normal columns are content-sized (Auto),
    /// long-text columns take remaining width (Star) with left-aligned style.
    /// </summary>
    [Fact]
    public void DataGrid_GlobalStyle_IsCenteredPaddedAndAuto()
    {
        WpfTestHost.Run(() =>
        {
            var resources = Application.Current.Resources;
            var merged = resources.MergedDictionaries
                .SelectMany(d => d.Values.OfType<Style>())
                .ToList();
            var headerStyle = merged.FirstOrDefault(s => s.TargetType == typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            var cellStyle = merged.FirstOrDefault(s => s.TargetType == typeof(System.Windows.Controls.DataGridCell));
            Assert.NotNull(headerStyle);
            Assert.NotNull(cellStyle);
            Assert.Contains(headerStyle.Setters.OfType<Setter>(),
                s => s.Property == System.Windows.Controls.Control.HorizontalContentAlignmentProperty &&
                    Equals(s.Value, System.Windows.HorizontalAlignment.Center));
            Assert.Contains(cellStyle.Setters.OfType<Setter>(),
                s => s.Property == System.Windows.Controls.Control.HorizontalContentAlignmentProperty &&
                    Equals(s.Value, System.Windows.HorizontalAlignment.Center));
            Assert.Contains(cellStyle.Setters.OfType<Setter>(),
                s => s.Property == System.Windows.Controls.Control.PaddingProperty &&
                    Equals(s.Value, new Thickness(10, 4, 10, 4)));

            var scope = new ScopeService("N1");
            var report = new ReportService();
            var storageVm = new StorageViewModel(
                new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
            SeedStorage(storageVm);
            var view = new StorageView { DataContext = storageVm };
            var window = new Window { Content = view, Width = 1400, Height = 800 };
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).First();
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                foreach (var grid in FindVisualChildren<System.Windows.Controls.DataGrid>(window))
                {
                    foreach (var column in grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>())
                    {
                        var unit = column.Width.UnitType;
                        Assert.True(
                            unit == DataGridLengthUnitType.Auto || unit == DataGridLengthUnitType.Star,
                            $"Column '{column.Header}' is neither Auto nor Star.");
                        if (unit == DataGridLengthUnitType.Star)
                        {
                            Assert.NotNull(column.ElementStyle);
                        }
                    }
                }
            }

            window.Close();
        }, TimeSpan.FromMinutes(2), nameof(DataGrid_GlobalStyle_IsCenteredPaddedAndAuto));
    }

    /// <summary>Help shows current content only: no stale Phase 2 language.</summary>
    [Fact]
    public void HelpView_HasNoStaleText()
    {
        WpfTestHost.Run(() =>
        {
            var helpVm = new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider());
            var view = new HelpView { DataContext = helpVm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var text = string.Join("\n", FindVisualChildren<TextBlock>(window).Select(t => t.Text));
            window.Close();
            Assert.DoesNotContain("fáza 2", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bude nasledovať", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Zoznam zmien", text, StringComparison.Ordinal);
            Assert.Contains(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider().ApplicationVersion, text);
        }, TimeSpan.FromMinutes(2), nameof(HelpView_HasNoStaleText));
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text;
        public bool ShouldThrow;

        public void SetText(string text)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("clipboard locked by test");
            }

            Text = text;
        }
    }

    private sealed class FakeProbes : ICapabilityProbeService
    {
        public Task<CapabilityReport> CheckHyperVAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.HyperV, true, "Hyper-V: OK"));
        public Task<CapabilityReport> CheckClusterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.FailoverCluster, false, "Cluster: N/A"));
        public Task<CapabilityReport> CheckCimAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilityReport(CapabilityKind.Cim, true, "CIM/WMI: OK"));
    }

    internal static System.Windows.Controls.DataGridCell? FindCellByText(Window window, string cellText)
    {
        foreach (var cell in FindVisualChildren<System.Windows.Controls.DataGridCell>(window))
        {
            var text = FindVisualChildren<TextBlock>(cell).FirstOrDefault()?.Text;
            if (string.Equals(text, cellText, StringComparison.Ordinal))
            {
                return cell;
            }
        }

        return null;
    }

    private static System.Windows.Controls.DataGrid FindGridByItemSource(Window window, object itemsSource)
    {
        return FindVisualChildren<System.Windows.Controls.DataGrid>(window)
            .First(g => ReferenceEquals(g.ItemsSource, itemsSource));
    }

    /// <summary>
    /// Rendered cell content is ACTUALLY centered (TextAlignment on the
    /// generated TextBlock, not just cell alignment), vertically centered,
    /// with breathing room — and exact values carry no literal padding.
    /// </summary>
    [Fact]
    public void Cells_AreCentered_WithPadding_ValuesExact()
    {
        WpfTestHost.Run(() =>
        {
            var scope = new ScopeService("N1");
            var report = new ReportService();
            var storageVm = new StorageViewModel(
                new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
            SeedStorage(storageVm);
            var view = new StorageView { DataContext = storageVm };
            var window = new Window { Content = view, Width = 1500, Height = 800 };
            window.Show();
            Pump(window);

            var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).First();
            tabs.SelectedIndex = 8; // Mapa VM
            Pump(window);

            var grid = FindGridByItemSource(window, storageVm.StorageMapRows);
            var vmColumn = grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>()
                .First(c => (c.Binding as System.Windows.Data.Binding)?.Path?.Path == "VM");
            var centeredStyle = (Style)grid.TryFindResource("CenteredText");
            Assert.Same(centeredStyle, vmColumn.ElementStyle);

            var macCell = FindCellByText(window, "APP1");
            Assert.NotNull(macCell);
            var macText = FindVisualChildren<TextBlock>(macCell).First();
            Assert.Equal(TextAlignment.Center, macText.TextAlignment);
            Assert.Equal(VerticalAlignment.Center, macText.VerticalAlignment);

            // Breathing room comes from presentation padding (never literal
            // spaces): the TextBlock carries >=10px left padding while the
            // backing value stays exact.
            Assert.True(macText.Padding.Left >= 10,
                $"Expected TextBlock left padding, measured {macText.Padding.Left:F1}px.");
            Assert.Equal("APP1", macText.Text);
            Assert.False(macText.Text.StartsWith(' ') || macText.Text.EndsWith(' '));

            // Path exception stays left-aligned.
            var pathCell = FindCellByText(window, @"C:\ClusterStorage\Volume1\disk.vhdx");
            Assert.NotNull(pathCell);
            var pathText = FindVisualChildren<TextBlock>(pathCell).First();
            Assert.Equal(HorizontalAlignment.Left, pathText.HorizontalAlignment);

            window.Close();
        }, TimeSpan.FromMinutes(2), nameof(Cells_AreCentered_WithPadding_ValuesExact));
    }

    /// <summary>Copy value/row through the shared behavior with a fake clipboard.</summary>
    [Fact]
    public void CopyBehavior_ValueAndRow()
    {
        var previous = Core.State.LocalizationService.Instance.CurrentLanguage;
        var previousClipboard = DataGridBehavior.Clipboard;
        try
        {
            WpfTestHost.Run(() =>
            {
                Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.Slovak);
                var fake = new FakeClipboard();
                DataGridBehavior.Clipboard = fake;

                var scope = new ScopeService("N1");
                var report = new ReportService();
                var vmsVm = new VmsViewModel(new FakeTargets(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
                SeedVms(vmsVm);
                var view = new VmsView { DataContext = vmsVm };
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                Pump(window);

                var grid = FindGridByItemSource(window, vmsVm.OverviewRows);

                // 1-2. Exact MAC and IP, no padding.
                var macCell = FindCellByText(window, "00155D010203");
                Assert.NotNull(macCell);
                DataGridBehavior.HandleCellRightClick(grid, macCell);
                Assert.Equal(vmsVm.OverviewRows[0], grid.SelectedItem);
                DataGridBehavior.CopyValue(grid);
                Assert.Equal("00155D010203", fake.Text);

                var ipCell = FindCellByText(window, "192.168.1.10");
                Assert.NotNull(ipCell);
                DataGridBehavior.HandleCellRightClick(grid, ipCell);
                DataGridBehavior.CopyValue(grid);
                Assert.Equal("192.168.1.10", fake.Text);

                // 5-6. Tab-separated row in visible DisplayIndex order.
                DataGridBehavior.CopyRow(grid);
                var parts = (fake.Text ?? string.Empty).Split('\t');
                Assert.True(parts.Length >= 11, $"Expected full row, got: {fake.Text}");
                Assert.Equal("N1", parts[0]);
                Assert.Contains("APP1", parts);
                Assert.Contains("00155D010203", parts);

                // 7. Hidden columns are excluded.
                var hidden = grid.Columns[1];
                var savedVisibility = hidden.Visibility;
                hidden.Visibility = Visibility.Collapsed;
                try
                {
                    DataGridBehavior.CopyRow(grid);
                    Assert.DoesNotContain("APP1", (fake.Text ?? string.Empty).Split('\t'));
                }
                finally
                {
                    hidden.Visibility = savedVisibility;
                }

                // 10. Clipboard failure: no crash, localized status message.
                fake.ShouldThrow = true;
                DataGridBehavior.CopyValue(grid);
                Assert.Equal(Core.State.LocalizationService.Instance["Ctx_ClipboardFailed"], vmsVm.StatusText);

                window.Close();
            }, TimeSpan.FromMinutes(2), "copy-value-row");
        }
        finally
        {
            DataGridBehavior.Clipboard = previousClipboard;
            Core.State.LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>Copy value preserves meaningful path characters; null is safe.</summary>
    [Fact]
    public void CopyBehavior_PathAndNull()
    {
        var previousClipboard = DataGridBehavior.Clipboard;
        try
        {
            WpfTestHost.Run(() =>
            {
                var fake = new FakeClipboard();
                DataGridBehavior.Clipboard = fake;

                var scope = new ScopeService("N1");
                var report = new ReportService();
                var storageVm = new StorageViewModel(
                    new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
                SeedStorage(storageVm);
                var view = new StorageView { DataContext = storageVm };
                var window = new Window { Content = view, Width = 1500, Height = 800 };
                window.Show();
                Pump(window);

                var tabs = FindVisualChildren<System.Windows.Controls.TabControl>(window).First();
                tabs.SelectedIndex = 8; // Mapa VM
                Pump(window);

                var grid = FindGridByItemSource(window, storageVm.StorageMapRows);
                var pathCell = FindCellByText(window, @"C:\ClusterStorage\Volume1\disk.vhdx");
                Assert.NotNull(pathCell);
                DataGridBehavior.HandleCellRightClick(grid, pathCell);
                DataGridBehavior.CopyValue(grid);
                Assert.Equal(@"C:\ClusterStorage\Volume1\disk.vhdx", fake.Text);

                // 9. Null content copies as empty without crashing.
                Assert.Equal(string.Empty, DataGridBehavior.ContentText(null));

                window.Close();
            }, TimeSpan.FromMinutes(2), "copy-path-null");
        }
        finally
        {
            DataGridBehavior.Clipboard = previousClipboard;
        }
    }

    /// <summary>Right-click activates the clicked row even under Cell selection.</summary>
    [Fact]
    public void RightClick_SelectsClickedCell()
    {
        WpfTestHost.Run(() =>
        {
            var scope = new ScopeService("N1");
            var report = new ReportService();
            var vmsVm = new VmsViewModel(new FakeTargets(), new FakeHyperV(), scope, report, new SessionCache(), NullLogger<VmsViewModel>.Instance);
            SeedVms(vmsVm);
            var view = new VmsView { DataContext = vmsVm };
            var window = new Window { Content = view, Width = 1400, Height = 800 };
            window.Show();
            Pump(window);

            var grid = FindGridByItemSource(window, vmsVm.OverviewRows);
            grid.SelectionUnit = DataGridSelectionUnit.Cell;
            var target = FindCellByText(window, "DOSVM");
            Assert.NotNull(target);
            var dosRow = vmsVm.OverviewRows.First(r => r.VM == "DOSVM");

            // Deterministic path: the shared handler itself.
            DataGridBehavior.HandleCellRightClick(grid, target);
            Assert.True(grid.SelectedCells.Count > 0,
                $"SelectedCells empty after direct handler call; SelectedItem={grid.SelectedItem ?? "<null>"}.");
            Assert.Contains(grid.SelectedCells, c => Equals(c.Item, dosRow));

            // Routed path: a real right-click tunnel reaches the same handler
            // (raised on the grid with the cell as source, like real input).
            grid.SelectedCells.Clear();
            var routedHits = 0;
            void OnRouted(object s, System.Windows.Input.MouseButtonEventArgs e) => routedHits++;
            var routedHandler = new System.Windows.Input.MouseButtonEventHandler(OnRouted);
            grid.AddHandler(System.Windows.UIElement.PreviewMouseRightButtonDownEvent, routedHandler);
            try
            {
                var args = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                {
                    RoutedEvent = System.Windows.UIElement.PreviewMouseRightButtonDownEvent,
                    Source = target,
                };
                grid.RaiseEvent(args);
            }
            finally
            {
                grid.RemoveHandler(System.Windows.UIElement.PreviewMouseRightButtonDownEvent, routedHandler);
            }

            Assert.True(routedHits > 0, "Synthetic PreviewMouseRightButtonDown never reached the grid.");
            Assert.Contains(grid.SelectedCells, c => Equals(c.Item, dosRow));

            window.Close();
        }, TimeSpan.FromMinutes(2), nameof(RightClick_SelectsClickedCell));
    }

    /// <summary>Main window opens with the approved large default size.</summary>
    [Fact]
    public void MainWindow_DefaultSize()
    {
        WpfTestHost.Run(() =>
        {
            var scope = new ScopeService("N1");
            var report = new ReportService();
            var snapshot = new CapabilitySnapshot();
            var policy = new ScopePolicy("N1");
            var settings = new InMemorySettingsService();
            var targets = new FakeTargets();
            var hyperV = new FakeHyperV();
            var systemInfo = new FakeSystemInfo();
            var cache = new SessionCache();
            var vmsVm = new VmsViewModel(targets, hyperV, scope, report, cache, NullLogger<VmsViewModel>.Instance);
            var nodesVm = new NodesViewModel(targets, systemInfo, hyperV, scope, report, cache, NullLogger<NodesViewModel>.Instance);
            var dashboardVm = new DashboardViewModel(targets, hyperV, systemInfo,
                new FakeStorage(), new FakeCluster(), scope, report, cache, NullLogger<DashboardViewModel>.Instance);
            var storageVm = new StorageViewModel(targets, new FakeStorage(), hyperV, scope, report, cache, NullLogger<StorageViewModel>.Instance);
            var networkingVm = new NetworkingViewModel(targets, hyperV, scope, report, cache, NullLogger<NetworkingViewModel>.Instance);
            var clusterVm = new ClusterViewModel(targets, new FakeClusterService(), hyperV, systemInfo,
                new FakeStorage(), scope, report, cache, NullLogger<ClusterViewModel>.Instance);
            var diagnosticsVm = new DiagnosticsViewModel(targets, hyperV, new FakeStorage(), new FakeClusterService(),
                scope, report, cache, NullLogger<DiagnosticsViewModel>.Instance);
            var exportVm = new ExportViewModel(report, scope, targets, hyperV, cache,
                new FakeExportDialog(), new FakeExportWriter(), TimeProvider.System,
                NullLogger<ExportViewModel>.Instance);
            var settingsVm = new SettingsViewModel(scope, settings, snapshot, policy, NullLogger<SettingsViewModel>.Instance);
            var shell = new MainViewModel(scope, settings, new FakeProbes(),
                new Pi1.HyperVToolkit.Infrastructure.Security.PrivilegeService(),
                snapshot, policy, vmsVm, nodesVm, dashboardVm, storageVm, networkingVm,
                clusterVm, diagnosticsVm, exportVm,
                settingsVm, new HelpViewModel(new Pi1.HyperVToolkit.Infrastructure.Diagnostics.AppInfoProvider()),
                NullLogger<MainViewModel>.Instance);

            var window = new Pi1.HyperVToolkit.MainWindow(shell);
            Assert.Equal(1700, window.Width);
            Assert.Equal(900, window.Height);
            Assert.Equal(1100, window.MinWidth);
            Assert.Equal(650, window.MinHeight);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            window.Close();
        }, TimeSpan.FromMinutes(2), nameof(MainWindow_DefaultSize));
    }

    /// <summary>Storage tab carries the descriptive localized name in both languages.</summary>
    [Fact]
    public void StorageTab_Renamed()
    {
        var previous = Core.State.LocalizationService.Instance.CurrentLanguage;
        try
        {
            WpfTestHost.Run(() =>
            {
                Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.Slovak);
                var scope = new ScopeService("N1");
                var report = new ReportService();
                var storageVm = new StorageViewModel(
                    new FakeTargets(), new FakeStorage(), new FakeHyperV(),
                    scope, report, new SessionCache(), NullLogger<StorageViewModel>.Instance);
                SeedStorage(storageVm);
                var view = new StorageView { DataContext = storageVm };
                var window = new Window { Content = view, Width = 1400, Height = 800 };
                window.Show();
                Pump(window);

                Assert.Contains(
                    FindVisualChildren<System.Windows.Controls.TabItem>(window).Select(t => t.Header as string),
                    h => h == "Úložisko vybranej VM");

                Core.State.LocalizationService.Instance.SetLanguage(Core.Localization.AppLanguage.English);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Contains(
                    FindVisualChildren<System.Windows.Controls.TabItem>(window).Select(t => t.Header as string),
                    h => h == "Selected VM Storage");

                window.Close();
            }, TimeSpan.FromMinutes(2), nameof(StorageTab_Renamed));
        }
        finally
        {
            Core.State.LocalizationService.Instance.SetLanguage(previous);
        }
    }

    internal static void Pump(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Startup regression test: loads the REAL App.xaml application resource
    /// graph (converters, merged styles, view DataTemplates) through the WPF
    /// XAML parser. A duplicate x:Key (the Phase-5
    /// InverseBoolToVisibilityConverter startup crash) fails here with the
    /// same "Item has already been added" XamlParseException instead of
    /// killing the production process before any window appears.
    /// A second Application instance cannot exist per AppDomain (and .NET
    /// Core has no multi-AppDomain), so this parses the real file content
    /// rather than calling App.InitializeComponent — same parser, same key
    /// enforcement, no global-state hazard. The only test-time adaptation is
    /// assembly-qualifying the xmlns declarations (exactly what the BAML
    /// compiler resolves at build) via a temp copy; the repository files are
    /// never modified.
    /// </summary>
    [Fact]
    public void AppResources_LoadWithoutDuplicateKeys()
    {
        WpfTestHost.Run(() =>
        {
            var appXaml = FindAppXaml();
            Assert.True(File.Exists(appXaml), $"App.xaml not found: {appXaml}");

            var stylesPath = Path.Combine(Path.GetDirectoryName(appXaml)!, "Styles", "DataGridStyles.xaml");
            Assert.True(File.Exists(stylesPath), $"Styles file not found: {stylesPath}");
            var tempStyles = Path.Combine(Path.GetTempPath(), $"Pi1GridStyles_{Guid.NewGuid():N}.xaml");
            try
            {
                File.WriteAllText(tempStyles, File.ReadAllText(stylesPath).Replace(
                    "xmlns:b=\"clr-namespace:Pi1.HyperVToolkit.Behaviors\"",
                    "xmlns:b=\"clr-namespace:Pi1.HyperVToolkit.Behaviors;assembly=Pi1.HyperVToolkit\""));

                // Text-level extraction (NOT XmlDocument.OuterXml, which pushes
                // in-scope xmlns declarations down onto the first using
                // element instead of the fragment root, breaking prefix
                // resolution elsewhere). The inner dictionary is re-rooted
                // with the exact App.xaml namespace URIs, assembly-qualified
                // exactly as the BAML compiler resolves them at build.
                var source = File.ReadAllText(appXaml);
                var xaml = ExtractInnerResourceDictionary(source);
                Assert.NotNull(xaml);
                xaml = xaml!
                    .Replace(
                        "Source=\"Styles/DataGridStyles.xaml\"",
                        "Source=\"file:///" + tempStyles.Replace('\\', '/') + "\"");

                ResourceDictionary resources = null!;
                var parsed = false;
                string? failure = null;
                try
                {
                    resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml)!;
                    parsed = true;
                }
                catch (Exception ex)
                {
                    failure = ex.ToString();
                }

                Assert.True(parsed, $"App.xaml resources failed to load: {failure}");
                Assert.True(resources.Contains("InverseBoolToVisibilityConverter"));
            }
            finally
            {
                try
                {
                    File.Delete(tempStyles);
                }
                catch (Exception)
                {
                    // Best-effort temp cleanup.
                }
            }
        }, TimeSpan.FromMinutes(2), nameof(AppResources_LoadWithoutDuplicateKeys));
    }

    private static string FindAppXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 8 && directory is not null; level++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Pi1.HyperVToolkit", "App.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "App.xaml");
    }

    // Extracts the inner application <ResourceDictionary> span from the raw
    // App.xaml text (the one directly under Application.Resources) and
    // re-roots it with fully-qualified namespace declarations. Returns null
    // when the structure is not recognized.
    private static string? ExtractInnerResourceDictionary(string text)
    {
        const string openTag = "<ResourceDictionary>";
        var start = text.IndexOf(openTag, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var position = start;
        while (position < text.Length)
        {
            var nextOpen = text.IndexOf("<ResourceDictionary", position, StringComparison.Ordinal);
            var nextClose = text.IndexOf("</ResourceDictionary>", position, StringComparison.Ordinal);
            if (nextClose < 0)
            {
                return null;
            }

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                if (IsDottedMember(nextOpen, text))
                {
                    // Property-element tags ("<ResourceDictionary.MergedDictionaries>"
                    // and its closer never match the exact open/close needles):
                    // depth-neutral, skip the open tag.
                    var dottedEnd = text.IndexOf('>', nextOpen);
                    if (dottedEnd < 0)
                    {
                        return null;
                    }

                    position = dottedEnd + 1;
                    continue;
                }

                var tagEnd = text.IndexOf('>', nextOpen);
                if (tagEnd < 0 || tagEnd >= nextClose)
                {
                    return null;
                }

                if (text[tagEnd - 1] != '/')
                {
                    depth++;
                }

                position = tagEnd + 1;
            }
            else
            {
                depth--;
                position = nextClose + "</ResourceDictionary>".Length;
                if (depth == 0)
                {
                    var inner = text[(start + openTag.Length)..(position - "</ResourceDictionary>".Length)];
                    return "<ResourceDictionary " +
                        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "xmlns:converters=\"clr-namespace:Pi1.HyperVToolkit.Converters;assembly=Pi1.HyperVToolkit\" " +
                        "xmlns:vm=\"clr-namespace:Pi1.HyperVToolkit.ViewModels;assembly=Pi1.HyperVToolkit\" " +
                        "xmlns:views=\"clr-namespace:Pi1.HyperVToolkit.Views;assembly=Pi1.HyperVToolkit\">" +
                        inner + "</ResourceDictionary>";
                }
            }
        }

        return null;
    }

    private static bool IsDottedMember(int index, string text)
    {
        // Matches "<ResourceDictionary.MergedDictionaries>" style member
        // elements, which do not affect dictionary depth.
        var after = index + "<ResourceDictionary".Length;
        return after < text.Length && text[after] == '.';
    }
}
