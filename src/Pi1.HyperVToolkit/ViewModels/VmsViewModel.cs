using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Filtering;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>
/// Virtual Machines view model. One refresh loads base rows, network rows and
/// checkpoints once; tabs are filtered projections (no repeated queries).
/// Parity with Pi1.VM.psm1 features 1–11.
/// </summary>
public partial class VmsViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IHyperVService _hyperV;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<VmsViewModel> _logger;

    private List<VirtualMachineRow> _allBaseRows = [];
    private List<VmNetworkRow> _allNetworkRows = [];

    public VmsViewModel(
        ITargetNodeResolver targets,
        IHyperVService hyperV,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<VmsViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_VMs"];
    }

    public ObservableCollection<VirtualMachineRow> OverviewRows { get; } = [];
    public ObservableCollection<VmNetworkRow> NetworkRows { get; } = [];
    public ObservableCollection<VirtualMachineRow> MemoryRows { get; } = [];
    public ObservableCollection<CheckpointRow> CheckpointRows { get; } = [];
    public ObservableCollection<VmNetworkRow> WithoutIpRows { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private string stateFilter = "All";

    public IReadOnlyList<string> StateOptions { get; } = ["All", "Running", "Off"];

    [ObservableProperty]
    private string nameFilter = string.Empty;

    [ObservableProperty]
    private string ipFilter = string.Empty;

    [ObservableProperty]
    private string macFilter = string.Empty;

    [ObservableProperty]
    private VirtualMachineRow? selectedVm;

    [ObservableProperty]
    private string selectedVmTitle = LocalizationService.Instance["D_NoVM"];

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    // Currently selected report tab: 0 Overview, 1 Network, 2 Memory,
    // 3 Checkpoints, 4 Without IP. Drives IReportService publication so
    // Export Current Report always means the visible table.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    partial void OnStateFilterChanged(string value) => RebuildViews();
    partial void OnNameFilterChanged(string value) => RebuildViews();
    partial void OnIpFilterChanged(string value) => RebuildViews();
    partial void OnMacFilterChanged(string value) => RebuildViews();

    partial void OnSelectedVmChanged(VirtualMachineRow? value) =>
        SelectedVmTitle = value?.VM ?? LocalizationService.Instance["D_NoVM"];

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_VMs"];
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
            var (vmRows, vmWarnings) = await CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmBase, key, force,
                ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
            var (nicRows, nicWarnings) = await CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmNetwork, key, force,
                ct => _hyperV.GetVmNetworkAdaptersAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
            var (cpRows, cpWarnings) = await CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.Checkpoints, key, force,
                ct => _hyperV.GetCheckpointsAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
            foreach (var warning in vmWarnings.Concat(nicWarnings).Concat(cpWarnings))
            {
                AddWarning(warning);
            }

            _allBaseRows = vmRows
                .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _allNetworkRows = nicRows
                .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.VMName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CheckpointRows.Clear();
            foreach (var cp in cpRows
                         .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase))
            {
                CheckpointRows.Add(cp);
            }

            RebuildViews();
            HasData = _allBaseRows.Count > 0 || _allNetworkRows.Count > 0 || CheckpointRows.Count > 0;
            var countText = loc.Items(_allBaseRows.Count, "záznam VM", "záznamy VM", "záznamov VM", "VM record");
            StatusText = HasWarnings
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
            _logger.LogError(ex, "Načítanie virtuálnych počítačov zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
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

    private void SetEmpty(string status)
    {
        _allBaseRows = [];
        _allNetworkRows = [];
        CheckpointRows.Clear();
        RebuildViews();
        HasData = false;
        StatusText = status;
    }

    private void RebuildViews()
    {
        var overview = VmRowFilters.ByState(_allBaseRows, StateFilter == "All" ? null : StateFilter);
        overview = VmRowFilters.ByName(overview, NameFilter);
        OverviewRows.Clear();
        foreach (var row in overview)
        {
            OverviewRows.Add(row);
        }

        MemoryRows.Clear();
        foreach (var row in _allBaseRows)
        {
            MemoryRows.Add(row);
        }

        var network = VmRowFilters.ByIp(_allNetworkRows, IpFilter);
        network = VmRowFilters.ByMac(network, MacFilter);
        NetworkRows.Clear();
        foreach (var row in network)
        {
            NetworkRows.Add(row);
        }

        WithoutIpRows.Clear();
        foreach (var row in VmRowFilters.WithoutIp(_allNetworkRows))
        {
            WithoutIpRows.Add(row);
        }

        if (SelectedVm is not null && !_allBaseRows.Contains(SelectedVm))
        {
            SelectedVm = null;
        }

        PublishCurrent();
    }

    // Publishes the currently selected tab's rows — including empty results,
    // so a stale report can never survive a tab switch. Pure local
    // projection: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1:
                _report.SetCurrent(NetworkRows.ToList(), "VMNetwork");
                break;
            case 2:
                _report.SetCurrent(MemoryRows.ToList(), "VMMemory");
                break;
            case 3:
                _report.SetCurrent(CheckpointRows.ToList(), "VMCheckpoints");
                break;
            case 4:
                _report.SetCurrent(WithoutIpRows.ToList(), "VMWithoutIP");
                break;
            default:
                _report.SetCurrent(OverviewRows.ToList(), "VirtualMachines");
                break;
        }
    }
}
