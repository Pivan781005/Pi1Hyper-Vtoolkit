using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Cluster;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Cluster view model. ONE snapshot per refresh (ClusterService, cached);
// every tab projects from it — nodes through health never re-query.
// VM base / hardware / CSV / jobs come from the SHARED session datasets
// (no duplicate collectors). Strictly read-only; failover simulation is a
// pure calculation over snapshots. Empty/unavailable cluster states are
// clean banners, never crashes or fake data.
public partial class ClusterViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IClusterService _cluster;
    private readonly IHyperVService _hyperV;
    private readonly ISystemInformationService _systemInfo;
    private readonly IStorageService _storage;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<ClusterViewModel> _logger;

    private List<VirtualMachineRow> _vmRows = [];
    private List<NodeHardwareRow> _hwRows = [];
    private List<string> _resolvedTargets = [];
    private ClusterTopologySnapshot? _snapshot;

    public ClusterViewModel(
        ITargetNodeResolver targets,
        IClusterService cluster,
        IHyperVService hyperV,
        ISystemInformationService systemInfo,
        IStorageService storage,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<ClusterViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Cluster"];
    }

    public ObservableCollection<ClusterNodeRow> NodeRows { get; } = [];
    public ObservableCollection<ClusterGroupRow> GroupRows { get; } = [];
    public ObservableCollection<ClusterResourceRow> ResourceRows { get; } = [];
    public ObservableCollection<ClusterNetworkRow> NetworkRows { get; } = [];
    public ObservableCollection<QuorumInfo> QuorumRows { get; } = [];
    public ObservableCollection<WitnessRow> WitnessRows { get; } = [];
    public ObservableCollection<ClusterEventRow> EventRows { get; } = [];
    public ObservableCollection<VmPlacementRow> PlacementRows { get; } = [];
    public ObservableCollection<VmDistributionRow> DistributionRows { get; } = [];
    public ObservableCollection<PlacementAdviceRow> AdviceRows { get; } = [];
    public ObservableCollection<FailoverRow> FailoverRows { get; } = [];
    public ObservableCollection<VirtualMachineRow> MoveVmRows { get; } = [];
    public ObservableCollection<HealthCheckRow> HealthRows { get; } = [];
    public ObservableCollection<CsvRow> CsvRows { get; } = [];
    public ObservableCollection<string> FailoverNodeOptions { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private string? selectedFailedNode;

    [ObservableProperty]
    private int? healthScore;

    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    // Currently selected report tab: 0 Nodes, 1 Roles, 2 Resources,
    // 3 Networks, 4 Quorum, 5 Witness, 6 Events, 7 Ownership, 8 Preferred
    // Owners (same placement rows), 9 Distribution, 10 PlacementAdvisor,
    // 11 Failover, 12 HealthScore, 13 CSV. Drives IReportService publication.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    partial void OnSelectedFailedNodeChanged(string? value) => RebuildFailover();

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Cluster"];
        NodeWarnings.Clear();
        HasWarnings = false;

        try
        {
            var targets = await _targets.ResolveAsync(_scope.Current, cancellationToken).ConfigureAwait(true);
            foreach (var warning in targets.Warnings)
            {
                AddWarning(warning);
            }

            _resolvedTargets = targets.IsSuccess ? targets.Nodes.ToList() : [];

            // Cluster-wide snapshot first (cached single-item list).
            var (snapshots, snapshotWarnings) = await CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.ClusterSnapshot, SessionCacheKeys.ClusterScopeKey, force,
                async ct => (IReadOnlyList<ClusterTopologySnapshot>)[await _cluster.GetSnapshotAsync(ct).ConfigureAwait(false)],
                ex => string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message),
                cancellationToken).ConfigureAwait(true);
            foreach (var warning in snapshotWarnings)
            {
                AddWarning(warning);
            }

            _snapshot = snapshots.FirstOrDefault();
            if (_snapshot is null || !_snapshot.IsAvailable)
            {
                // Unavailable whole-cluster state: the XAML banner bound to
                // the localized Cluster_Unavailable key is the ONE user-facing
                // message. Snapshot diagnostics (e.g. the raw English
                // "Cluster capability unavailable on this machine.") stay
                // internal — forwarding them would duplicate the banner and
                // leak backend English into localized UI.
                SetUnavailable();
                return;
            }

            foreach (var warning in _snapshot.Warnings)
            {
                AddWarning(warning);
            }

            // Shared scope-aware + local datasets (SessionCache serves repeat
            // visits; failover joins reuse them without re-querying).
            var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);
            var vmTask = targets.IsSuccess
                ? CacheLoad.NodeListAsync(
                    _cache, SessionCacheKeys.VmBase, key, force,
                    ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken)
                : Task.FromResult((new List<VirtualMachineRow>(), new List<string>()));
            var hwTask = targets.IsSuccess
                ? CacheLoad.SinglesAsync(
                    _cache, SessionCacheKeys.Hardware, key, force,
                    ct => _systemInfo.GetNodeHardwareAsync(targets.Nodes, ct), cancellationToken)
                : Task.FromResult((new List<NodeHardwareRow>(), new List<string>()));
            var csvTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.Csv, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetCsvRowsAsync(ct),
                ex => string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message),
                cancellationToken);
            var jobsTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StorageJobs, SessionCacheKeys.LocalScopeKey, force,
                ct => _storage.GetStorageJobsAsync(ct),
                ex => string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message),
                cancellationToken);
            await Task.WhenAll(vmTask, hwTask, csvTask, jobsTask).ConfigureAwait(true);

            var (vmLoaded, vmWarnings) = await vmTask.ConfigureAwait(true);
            var (hwLoaded, hwWarnings) = await hwTask.ConfigureAwait(true);
            var (csvs, csvWarnings) = await csvTask.ConfigureAwait(true);
            var (jobs, jobsWarnings) = await jobsTask.ConfigureAwait(true);
            foreach (var warning in vmWarnings.Concat(hwWarnings).Concat(csvWarnings).Concat(jobsWarnings))
            {
                AddWarning(warning);
            }

            _vmRows = vmLoaded;
            _hwRows = hwLoaded;

            FillCollections(_snapshot, csvs);

            // Health score over the same immutable inputs.
            var health = HealthScoreCalculator.Evaluate(
                _snapshot.Nodes,
                _snapshot.Resources,
                csvs,
                jobs,
                _snapshot.Quorum is null
                    ? []
                    : [_snapshot.Quorum],
                _vmRows.Where(v => string.Equals(v.State, "Running", StringComparison.Ordinal)));
            HealthScore = health.Score;
            HealthRows.Clear();
            foreach (var check in health.Checks)
            {
                HealthRows.Add(check);
            }

            // Failover source defaults to the first cluster node.
            FailoverNodeOptions.Clear();
            foreach (var node in _snapshot.Nodes.Select(n => n.Name).Where(n => !string.IsNullOrEmpty(n)))
            {
                FailoverNodeOptions.Add(node);
            }

            if (SelectedFailedNode is null || !FailoverNodeOptions.Contains(SelectedFailedNode))
            {
                SelectedFailedNode = FailoverNodeOptions.FirstOrDefault();
            }
            else
            {
                RebuildFailover();
            }

            PublishCurrent();

            IsAvailable = true;
            HasData = true;
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
            _logger.LogError(ex, "Načítanie klastra zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FillCollections(ClusterTopologySnapshot snapshot, IReadOnlyList<CsvRow> csvs)
    {
        NodeRows.Clear();
        foreach (var row in snapshot.Nodes)
        {
            NodeRows.Add(row);
        }

        GroupRows.Clear();
        foreach (var row in snapshot.Groups)
        {
            GroupRows.Add(row);
        }

        ResourceRows.Clear();
        foreach (var row in snapshot.Resources)
        {
            ResourceRows.Add(row);
        }

        NetworkRows.Clear();
        foreach (var row in snapshot.Networks)
        {
            NetworkRows.Add(row);
        }

        QuorumRows.Clear();
        if (snapshot.Quorum is not null)
        {
            QuorumRows.Add(snapshot.Quorum);
        }

        WitnessRows.Clear();
        foreach (var row in snapshot.WitnessRows)
        {
            WitnessRows.Add(row);
        }

        EventRows.Clear();
        foreach (var row in snapshot.Events)
        {
            EventRows.Add(row);
        }

        PlacementRows.Clear();
        foreach (var row in snapshot.PlacementRows
                     .OrderBy(r => r.OwnerNode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.VMGroup, StringComparer.OrdinalIgnoreCase))
        {
            PlacementRows.Add(row);
        }

        DistributionRows.Clear();
        foreach (var row in VmDistributionCalculator.Build(PlacementRows, _vmRows))
        {
            DistributionRows.Add(row);
        }

        AdviceRows.Clear();
        foreach (var row in PlacementAdvisorCalculator.Build(
                     _vmRows.Where(v => string.Equals(v.State, "Running", StringComparison.OrdinalIgnoreCase)),
                     _hwRows,
                     PlacementRows))
        {
            AdviceRows.Add(row);
        }

        CsvRows.Clear();
        foreach (var row in csvs)
        {
            CsvRows.Add(row);
        }
    }

    private void RebuildFailover()
    {
        FailoverRows.Clear();
        MoveVmRows.Clear();
        if (_snapshot is null || SelectedFailedNode is null)
        {
            PublishCurrent();
            return;
        }

        // Target list parity: Cluster scope uses the resolved scope targets,
        // otherwise the cluster node names from the snapshot (the reference
        // re-queries cluster nodes there; the snapshot already holds them —
        // no live re-query defect).
        var effectiveTargets = _scope.Current.Mode == ScopeMode.Cluster && _resolvedTargets.Count > 0
            ? _resolvedTargets
            : _snapshot.Nodes.Select(n => n.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        if (!effectiveTargets.Any(t => !string.Equals(t, SelectedFailedNode, StringComparison.OrdinalIgnoreCase)))
        {
            AddWarning(LocalizationService.Instance["Cluster_NoTargets"]);
            PublishCurrent();
            return;
        }

        var result = FailoverSimulator.Simulate(SelectedFailedNode, effectiveTargets, _vmRows, _hwRows);
        foreach (var row in result.TargetRows)
        {
            FailoverRows.Add(row);
        }

        foreach (var vm in result.MoveVms
                     .OrderByDescending(v => v.DemandGB)
                     .ThenBy(v => v.VM, StringComparer.OrdinalIgnoreCase))
        {
            MoveVmRows.Add(vm);
        }

        if (result.MoveVms.Count == 0)
        {
            AddWarning(LocalizationService.Instance["Cluster_NoRunningOnSource"]);
        }

        PublishCurrent();
    }

    private void SetUnavailable()
    {
        NodeRows.Clear();
        GroupRows.Clear();
        ResourceRows.Clear();
        NetworkRows.Clear();
        QuorumRows.Clear();
        WitnessRows.Clear();
        EventRows.Clear();
        PlacementRows.Clear();
        DistributionRows.Clear();
        AdviceRows.Clear();
        FailoverRows.Clear();
        MoveVmRows.Clear();
        HealthRows.Clear();
        CsvRows.Clear();
        FailoverNodeOptions.Clear();
        HealthScore = null;
        PublishCurrent();
        IsAvailable = false;
        HasData = false;
        StatusText = string.Empty;
    }

    // Publishes the currently selected tab's rows — including empty results,
    // so a stale report can never survive a tab switch. Pure local
    // projection over loaded collections/snapshot: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1:
                _report.SetCurrent(GroupRows.ToList(), "ClusterRoles");
                break;
            case 2:
                _report.SetCurrent(ResourceRows.ToList(), "ClusterResources");
                break;
            case 3:
                _report.SetCurrent(NetworkRows.ToList(), "ClusterNetworks");
                break;
            case 4:
                _report.SetCurrent(QuorumRows.ToList(), "ClusterQuorum");
                break;
            case 5:
                _report.SetCurrent(WitnessRows.ToList(), "ClusterWitness");
                break;
            case 6:
                _report.SetCurrent(EventRows.ToList(), "ClusterEvents");
                break;
            case 7:
                _report.SetCurrent(PlacementRows.ToList(), "ClusterPlacement");
                break;
            case 8:
                _report.SetCurrent(PlacementRows.ToList(), "ClusterPreferredOwners");
                break;
            case 9:
                _report.SetCurrent(DistributionRows.ToList(), "VmDistribution");
                break;
            case 10:
                _report.SetCurrent(AdviceRows.ToList(), "PlacementAdvice");
                break;
            case 11:
                _report.SetCurrent(FailoverRows.ToList(), "FailoverSimulation");
                break;
            case 12:
                _report.SetCurrent(HealthRows.ToList(), "HealthChecks");
                break;
            case 13:
                _report.SetCurrent(CsvRows.ToList(), "CSV");
                break;
            default:
                _report.SetCurrent(NodeRows.ToList(), "ClusterNodes");
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
