using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Dashboard view model. One refresh snapshots every input ONCE (VM + hardware
// follow scope; jobs/CSVs/pools/vdisks/cluster are local or cluster-wide per
// reference semantics) and derives all tabs with pure aggregations — the SAME
// StorageSummary builder the Storage page uses, never a duplicate.
public partial class DashboardViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IHyperVService _hyperV;
    private readonly ISystemInformationService _systemInfo;
    private readonly IStorageService _storage;
    private readonly IClusterInfoService _cluster;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<DashboardViewModel> _logger;

    public DashboardViewModel(
        ITargetNodeResolver targets,
        IHyperVService hyperV,
        ISystemInformationService systemInfo,
        IStorageService storage,
        IClusterInfoService cluster,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<DashboardViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Dashboard"];
    }

    /// <summary>
    /// Raised when the user activates the Dashboard Advisor shortcut.
    /// The shell navigates to the real Diagnostics Advisor (single Advisor
    /// implementation — no Dashboard-specific copy).
    /// </summary>
    public event EventHandler? AdvisorRequested;

    [RelayCommand]
    private void GoToAdvisor() => AdvisorRequested?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<DashboardRow> DashboardRows { get; } = [];
    public ObservableCollection<NodeSummaryRow> NodeSummaryRows { get; } = [];
    public ObservableCollection<VmSummaryRow> VmSummaryRows { get; } = [];
    public ObservableCollection<StorageSummaryRow> StorageSummaryRows { get; } = [];
    public ObservableCollection<StorageJobRow> JobRows { get; } = [];
    public ObservableCollection<CsvRow> CsvRows { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    // Currently selected report tab: 0 Overview cards, 1 Nodes, 2 VM summary,
    // 3 Storage summary, 4 Jobs, 5 CSV. Drives IReportService publication so
    // Export Current Report always means the visible table.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Dashboard"];
        NodeWarnings.Clear();
        HasWarnings = false;

        try
        {
            var targets = await _targets.ResolveAsync(_scope.Current, cancellationToken).ConfigureAwait(true);
            foreach (var warning in targets.Warnings)
            {
                AddWarning(warning);
            }

            if (!targets.IsSuccess)
            {
                SetEmpty(loc["Empty_NoTargets"]);
                return;
            }

            var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);

            // Single snapshot per refresh: scope-aware collectors fan out,
            // local/cluster inputs load once and are shared by every tab.
            // Session cache serves repeat visits without re-querying.
            var vmTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmBase, key, force,
                ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken);
            var hwTask = CacheLoad.SinglesAsync(
                _cache, SessionCacheKeys.Hardware, key, force,
                ct => _systemInfo.GetNodeHardwareAsync(targets.Nodes, ct), cancellationToken);
            var jobsTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StorageJobs, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetStorageJobsAsync(ct), ex => JobWarning("Storage Jobs", ex), cancellationToken);
            var csvTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.Csv, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetCsvRowsAsync(ct), ex => JobWarning("CSV", ex), cancellationToken);
            var poolsTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StoragePools, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetStoragePoolsAsync(ct), ex => JobWarning("Storage Pools", ex), cancellationToken);
            var vdisksTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.VirtualDisks, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetVirtualDisksAsync(ct), ex => JobWarning("Virtual Disks", ex), cancellationToken);
            var clusterTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.Cluster, SessionCacheKeys.LocalScopeKey, force,
                ct => ClusterAsListAsync(ct), ex => JobWarning("klaster", ex), cancellationToken);
            await Task.WhenAll(vmTask, hwTask, jobsTask, csvTask, poolsTask, vdisksTask, clusterTask)
                .ConfigureAwait(true);

            var (vmRows, vmWarnings) = await vmTask.ConfigureAwait(true);
            var (hwRows, hwWarnings) = await hwTask.ConfigureAwait(true);
            foreach (var warning in vmWarnings.Concat(hwWarnings))
            {
                AddWarning(warning);
            }

            var jobs = UnpackLocal(await jobsTask, JobRows);
            var csvs = UnpackLocal(await csvTask, CsvRows);
            var pools = (await poolsTask.ConfigureAwait(true)).Rows;
            var vdisks = (await vdisksTask.ConfigureAwait(true)).Rows;
            var clusterRows = (await clusterTask.ConfigureAwait(true)).Rows;
            var cluster = clusterRows.Count > 0
                ? clusterRows[0]
                : new DashboardCalculator.ClusterSnapshot(string.Empty, []);

            DashboardRows.Clear();
            var dashboard = DashboardCalculator.BuildDashboard(
                cluster,
                loc.ScopeLabel(_scope.Current, _scope.MachineName),
                vmRows,
                hwRows,
                csvs,
                jobs,
                loc["Word_Online"]);
            foreach (var row in dashboard)
            {
                DashboardRows.Add(row);
            }

            NodeSummaryRows.Clear();
            foreach (var row in DashboardCalculator.BuildNodeSummary(vmRows, hwRows))
            {
                NodeSummaryRows.Add(row);
            }

            VmSummaryRows.Clear();
            foreach (var row in DashboardCalculator.BuildVmSummary(vmRows))
            {
                VmSummaryRows.Add(row);
            }

            StorageSummaryRows.Clear();
            foreach (var row in StorageAggregations.BuildStorageSummary(pools, vdisks, csvs, jobs))
            {
                StorageSummaryRows.Add(row);
            }

            PublishCurrent();

            HasData = dashboard.Count > 0;
            StatusText = HasWarnings
                ? loc["Loaded_WithWarningsSimple"]
                : loc["Loaded_Simple"];
        }
        catch (OperationCanceledException)
        {
            StatusText = loc["Shell_OpCancelled"];
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Načítanie dashboardu zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IReadOnlyList<DashboardCalculator.ClusterSnapshot>> ClusterAsListAsync(
        CancellationToken cancellationToken) =>
        [(await _cluster.GetClusterSnapshotAsync(cancellationToken).ConfigureAwait(false))];

    private string JobWarning(string area, Exception ex)
    {
        _logger.LogWarning(ex, "Načítanie dashboard vstupu {Area} zlyhalo.", area);
        var loc = LocalizationService.Instance;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message);
    }

    private List<T> UnpackLocal<T>((IReadOnlyList<T> Rows, List<string> Warnings) result, ObservableCollection<T> target)
    {
        target.Clear();
        foreach (var row in result.Rows)
        {
            target.Add(row);
        }

        foreach (var warning in result.Warnings)
        {
            AddWarning(warning);
        }

        return result.Rows.ToList();
    }

    private void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning) && !NodeWarnings.Contains(warning))
        {
            NodeWarnings.Add(warning);
            HasWarnings = true;
        }
    }

    private void SetEmpty(string status)
    {
        DashboardRows.Clear();
        NodeSummaryRows.Clear();
        VmSummaryRows.Clear();
        StorageSummaryRows.Clear();
        JobRows.Clear();
        CsvRows.Clear();
        PublishCurrent();
        HasData = false;
        StatusText = status;
    }

    // Publishes the currently selected tab's rows — including empty results,
    // so a stale report can never survive a tab switch. Pure local
    // projection: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1:
                _report.SetCurrent(NodeSummaryRows.ToList(), "NodeSummary");
                break;
            case 2:
                _report.SetCurrent(VmSummaryRows.ToList(), "VmSummary");
                break;
            case 3:
                _report.SetCurrent(StorageSummaryRows.ToList(), "StorageSummary");
                break;
            case 4:
                _report.SetCurrent(JobRows.ToList(), "StorageJobs");
                break;
            case 5:
                _report.SetCurrent(CsvRows.ToList(), "CSV");
                break;
            default:
                _report.SetCurrent(DashboardRows.ToList(), "Dashboard");
                break;
        }
    }
}
