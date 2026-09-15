using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Storage view model. One refresh collects every LOCAL inventory ONCE plus the
// scope-aware VM list / VM storage map ONCE; all tabs are pure projections
// (no repeated queries, no N+1). Parity with Pi1.Storage.psm1 features 1–11.
public partial class StorageViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IStorageService _storage;
    private readonly IHyperVService _hyperV;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<StorageViewModel> _logger;

    private List<VmStorageRow> _allStorageRows = [];
    private List<VirtualMachineRow> _allVmRows = [];

    public StorageViewModel(
        ITargetNodeResolver targets,
        IStorageService storage,
        IHyperVService hyperV,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<StorageViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Storage"];
    }

    public ObservableCollection<StorageJobRow> JobRows { get; } = [];
    public ObservableCollection<StorageSummaryRow> SummaryRows { get; } = [];
    public ObservableCollection<StoragePoolRow> PoolRows { get; } = [];
    public ObservableCollection<VirtualDiskRow> VirtualDiskRows { get; } = [];
    public ObservableCollection<PhysicalDiskRow> PhysicalDiskRows { get; } = [];
    public ObservableCollection<PhysicalDiskSummaryRow> PhysicalDiskSummaryRows { get; } = [];
    public ObservableCollection<CsvRow> CsvRows { get; } = [];
    public ObservableCollection<StorageVolumeRow> VolumeRows { get; } = [];
    public ObservableCollection<VmStorageRow> StorageMapRows { get; } = [];
    public ObservableCollection<VmStorageByCsvRow> ByCsvRows { get; } = [];
    public ObservableCollection<VmStorageRow> SelectedVmStorageRows { get; } = [];
    public ObservableCollection<VirtualMachineRow> VmOptions { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private VirtualMachineRow? selectedVm;

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isLocalScopeNote;

    // Currently selected report tab: 0 Jobs, 1 Summary, 2 Pools,
    // 3 VirtualDisks, 4 PhysicalDisks, 5 DiskSummary, 6 CSV, 7 Volumes,
    // 8 VMMap, 9 ByCSV, 10 SelectedVM. Drives IReportService publication.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    partial void OnSelectedVmChanged(VirtualMachineRow? value)
    {
        RebuildSelectedVm();
        PublishCurrent();
    }

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Storage"];
        NodeWarnings.Clear();
        HasWarnings = false;

        try
        {
            var targets = await _targets.ResolveAsync(_scope.Current, cancellationToken).ConfigureAwait(true);
            foreach (var warning in targets.Warnings)
            {
                AddWarning(warning);
            }

            // Local inventories never fan out (reference LOCAL semantics);
            // VM storage + VM list follow the resolved scope. Session cache
            // serves repeat visits; EMPTY results are valid, not failures.
            var local = SessionCacheKeys.LocalScopeKey;
            var jobsTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StorageJobs, local, force,
                ct => _storage.GetStorageJobsAsync(ct), ex => AreaWarning("Storage Jobs", ex), cancellationToken);
            var poolsTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StoragePools, local, force,
                ct => _storage.GetStoragePoolsAsync(ct), ex => AreaWarning("Storage Pools", ex), cancellationToken);
            var vdisksTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.VirtualDisks, local, force,
                ct => _storage.GetVirtualDisksAsync(ct), ex => AreaWarning("Virtual Disks", ex), cancellationToken);
            var pdisksTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.PhysicalDisks, local, force,
                ct => _storage.GetPhysicalDisksAsync(ct), ex => AreaWarning("fyzické disky", ex), cancellationToken);
            var volumesTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.StorageVolumes, local, force,
                ct => _storage.GetVolumesAsync(ct), ex => AreaWarning("volumes", ex), cancellationToken);
            var csvTask = CacheLoad.LocalAsync(
                _cache, SessionCacheKeys.Csv, local, force,
                ct => _storage.GetCsvRowsAsync(ct), ex => AreaWarning("CSV", ex), cancellationToken);
            await Task.WhenAll(jobsTask, poolsTask, vdisksTask, pdisksTask, volumesTask, csvTask)
                .ConfigureAwait(true);

            var jobs = UnpackLocal(await jobsTask, JobRows);
            var pools = UnpackLocal(await poolsTask, PoolRows);
            var vdisks = UnpackLocal(await vdisksTask, VirtualDiskRows);
            var pdisks = UnpackLocal(await pdisksTask, PhysicalDiskRows);
            var volumes = UnpackLocal(await volumesTask, VolumeRows);
            var csvs = UnpackLocal(await csvTask, CsvRows);

            _allVmRows = [];
            if (targets.IsSuccess)
            {
                var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);
                var (vmLoaded, vmWarnings) = await CacheLoad.NodeListAsync(
                    _cache, SessionCacheKeys.VmBase, key, force,
                    ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
                foreach (var warning in vmWarnings)
                {
                    AddWarning(warning);
                }

                _allVmRows = vmLoaded
                    .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // VM storage map: CSV rows collected once above, joined per disk.
            _allStorageRows = [];
            if (targets.IsSuccess)
            {
                var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);
                var (mapLoaded, mapWarnings) = await CacheLoad.NodeListAsync(
                    _cache, SessionCacheKeys.VmStorage, key, force,
                    ct => _hyperV.GetVmStorageAsync(targets.Nodes, csvs, ct), cancellationToken).ConfigureAwait(true);
                foreach (var warning in mapWarnings)
                {
                    AddWarning(warning);
                }

                _allStorageRows = StorageAggregations.SortStorageMap(mapLoaded).ToList();
            }
            else
            {
                AddWarning(loc["Empty_NoTargets"]);
            }

            PhysicalDiskSummaryRows.Clear();
            foreach (var row in StorageAggregations.SummarizePhysicalDisks(pdisks))
            {
                PhysicalDiskSummaryRows.Add(row);
            }

            SummaryRows.Clear();
            foreach (var row in StorageAggregations.BuildStorageSummary(pools, vdisks, csvs, jobs))
            {
                SummaryRows.Add(row);
            }

            StorageMapRows.Clear();
            foreach (var row in _allStorageRows)
            {
                StorageMapRows.Add(row);
            }

            ByCsvRows.Clear();
            foreach (var row in StorageAggregations.GroupVmStorageByCsv(_allStorageRows))
            {
                ByCsvRows.Add(row);
            }

            RebuildVmOptions();
            RebuildSelectedVm();
            PublishCurrent();

            IsLocalScopeNote = true; // fondy/disky/zväzky/jobs sú vždy lokálne.
            HasData = jobs.Count > 0 || pools.Count > 0 || vdisks.Count > 0 || pdisks.Count > 0 ||
                volumes.Count > 0 || csvs.Count > 0 || _allStorageRows.Count > 0;
            var countText = loc.Items(StorageMapRows.Count, "disk VM", "disky VM", "diskov VM", "VM disk");
            StatusText = !HasData && !HasWarnings
                ? loc["Empty_Storage"]
                : HasWarnings
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Loaded_WithWarnings"], countText)
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Loaded_Plain"], countText);
        }
        catch (OperationCanceledException)
        {
            StatusText = loc["Shell_OpCancelled"];
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Načítanie úložiska zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildVmOptions()
    {
        VmOptions.Clear();
        foreach (var vm in _allVmRows
                     .GroupBy(r => (r.HostNode, r.VM))
                     .Select(g => g.First())
                     .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase))
        {
            VmOptions.Add(vm);
        }

        if (SelectedVm is not null && !VmOptions.Contains(SelectedVm))
        {
            SelectedVm = null;
        }
    }

    private void RebuildSelectedVm()
    {
        SelectedVmStorageRows.Clear();
        if (SelectedVm is null)
        {
            return;
        }

        foreach (var row in StorageAggregations.FilterForVm(_allStorageRows, SelectedVm.HostNode, SelectedVm.VM))
        {
            SelectedVmStorageRows.Add(row);
        }
    }

    // Publishes the currently selected tab's rows — including empty results,
    // so a stale report can never survive a tab switch. Pure local
    // projection: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 0:
                _report.SetCurrent(JobRows.ToList(), "StorageJobs");
                break;
            case 1:
                _report.SetCurrent(SummaryRows.ToList(), "StorageSummary");
                break;
            case 2:
                _report.SetCurrent(PoolRows.ToList(), "StoragePools");
                break;
            case 3:
                _report.SetCurrent(VirtualDiskRows.ToList(), "VirtualDisks");
                break;
            case 4:
                _report.SetCurrent(PhysicalDiskRows.ToList(), "PhysicalDisks");
                break;
            case 5:
                _report.SetCurrent(PhysicalDiskSummaryRows.ToList(), "PhysicalDiskSummary");
                break;
            case 6:
                _report.SetCurrent(CsvRows.ToList(), "CSV");
                break;
            case 7:
                _report.SetCurrent(VolumeRows.ToList(), "StorageVolumes");
                break;
            case 9:
                _report.SetCurrent(ByCsvRows.ToList(), "VMStorageByCsv");
                break;
            case 10:
                _report.SetCurrent(SelectedVmStorageRows.ToList(), "SelectedVmStorage");
                break;
            default:
                _report.SetCurrent(StorageMapRows.ToList(), "VMStorageMap");
                break;
        }
    }

    private string AreaWarning(string area, Exception ex)
    {
        _logger.LogWarning(ex, "Načítanie {Area} zlyhalo.", area);
        var loc = LocalizationService.Instance;
        // An EMPTY result is valid (no banner); only real failures warn.
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"],
            ex is InvalidOperationException ? ex.Message : $"{ex.Message}");
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
}
