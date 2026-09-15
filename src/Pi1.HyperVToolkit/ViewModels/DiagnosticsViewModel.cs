using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Diagnostics;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Diagnostics view model. CONSUMER only: analyzes snapshots already collected
// by earlier phases (VM base rows, CSV rows, cluster snapshot) with pure
// calculators — never a duplicate collector, never a live MSCluster query.
// Strictly read-only; recommendations are text, never executed.
// Scope: Advisor is scope-aware (VM base follows scope); CSV Low Free is
// local/cluster-gated; Resources Not Online is cluster-wide.
public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IHyperVService _hyperV;
    private readonly IStorageService _storage;
    private readonly IClusterService _cluster;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<DiagnosticsViewModel> _logger;

    public DiagnosticsViewModel(
        ITargetNodeResolver targets,
        IHyperVService hyperV,
        IStorageService storage,
        IClusterService cluster,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<DiagnosticsViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Diagnostics"];
    }

    public ObservableCollection<AdvisorRow> AdvisorRows { get; } = [];
    public ObservableCollection<CsvRow> CsvLowFreeRows { get; } = [];
    public ObservableCollection<ClusterResourceRow> ResourceRows { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private bool isClusterAvailable;

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    /// <summary>
    /// Dashboard Advisor shortcut target: selects the Advisor tab.
    /// Pure UI state, no reload.
    /// </summary>
    public void SelectAdvisorTab() => SelectedTabIndex = 0;

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Diagnostics"];
        NodeWarnings.Clear();
        HasWarnings = false;

        try
        {
            var targets = await _targets.ResolveAsync(_scope.Current, cancellationToken).ConfigureAwait(true);
            foreach (var warning in targets.Warnings)
            {
                AddWarning(warning);
            }

            // Reuse compatible cached datasets: scope-aware VM base for the
            // Advisor, local CSV snapshot, cluster-wide topology snapshot.
            List<VirtualMachineRow> vmRows = [];
            if (targets.IsSuccess)
            {
                var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);
                var (loaded, warnings) = await CacheLoad.NodeListAsync(
                    _cache, SessionCacheKeys.VmBase, key, force,
                    ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
                foreach (var warning in warnings)
                {
                    AddWarning(warning);
                }

                vmRows = loaded;
            }
            else
            {
                AddWarning(loc["Empty_NoTargets"]);
            }

            var csvTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.Csv, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetCsvRowsAsync(ct),
                ex => string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message),
                cancellationToken);
            var snapshotTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.ClusterSnapshot, SessionCacheKeys.ClusterScopeKey, force,
                async ct => (IReadOnlyList<ClusterTopologySnapshot>)[await _cluster.GetSnapshotAsync(ct).ConfigureAwait(false)],
                ex => string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message),
                cancellationToken);
            await Task.WhenAll(csvTask, snapshotTask).ConfigureAwait(true);

            var (csvs, csvWarnings) = await csvTask.ConfigureAwait(true);
            var (snapshots, snapshotWarnings) = await snapshotTask.ConfigureAwait(true);
            foreach (var warning in csvWarnings.Concat(snapshotWarnings))
            {
                AddWarning(warning);
            }

            var snapshot = snapshots.FirstOrDefault();
            IsClusterAvailable = snapshot is not null && snapshot.IsAvailable;
            if (IsClusterAvailable && snapshot is not null)
            {
                // Partial cluster warnings stay visible. When unavailable, the
                // localized Cluster_Unavailable banner is the ONE user-facing
                // message — raw backend diagnostics stay internal (Phase 5 rule).
                foreach (var warning in snapshot.Warnings)
                {
                    AddWarning(warning);
                }
            }

            var advisor = AdvisorCalculator.Build(vmRows);
            var lowFree = CsvLowFreeCalculator.Filter(csvs);
            var badResources = IsClusterAvailable && snapshot is not null
                ? ClusterResourceDiagnostics.FilterNotOnline(snapshot.Resources)
                : new List<ClusterResourceRow>();

            AdvisorRows.Clear();
            foreach (var row in advisor)
            {
                AdvisorRows.Add(row);
            }

            CsvLowFreeRows.Clear();
            foreach (var row in lowFree)
            {
                CsvLowFreeRows.Add(row);
            }

            ResourceRows.Clear();
            foreach (var row in badResources)
            {
                ResourceRows.Add(row);
            }

            PublishCurrent();

            HasData = AdvisorRows.Count > 0 || CsvLowFreeRows.Count > 0 || ResourceRows.Count > 0;
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
            _logger.LogError(ex, "Načítanie diagnostiky zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PublishCurrent()
    {
        // One current-result at a time (ReportService): the visible tab wins.
        // Stable semantic names match the PowerShell ResultName values.
        switch (SelectedTabIndex)
        {
            case 1:
                _report.SetCurrent(CsvLowFreeRows.ToList(), "CSVLowFree");
                break;
            case 2:
                _report.SetCurrent(ResourceRows.ToList(), "ResourcesNotOnline");
                break;
            default:
                _report.SetCurrent(AdvisorRows.ToList(), "Advisor");
                break;
        }
    }

    private void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning) && !NodeWarnings.Contains(warning))
        {
            NodeWarnings.Add(warning);
            HasWarnings = true;
        }
    }
}
