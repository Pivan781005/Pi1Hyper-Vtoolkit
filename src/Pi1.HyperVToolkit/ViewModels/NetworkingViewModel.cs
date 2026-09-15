using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Filtering;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Networking view model. One refresh collects switches, VM adapters (Phase 3
// collector, reused — no second implementation) and VLAN rows ONCE; search
// tabs are pure substring projections with Phase 3 semantics.
public partial class NetworkingViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly IHyperVService _hyperV;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<NetworkingViewModel> _logger;

    private List<VmNetworkRow> _allNetworkRows = [];

    public NetworkingViewModel(
        ITargetNodeResolver targets,
        IHyperVService hyperV,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<NetworkingViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Networking"];
    }

    public ObservableCollection<VmSwitchRow> SwitchRows { get; } = [];
    public ObservableCollection<VmNetworkRow> AdapterRows { get; } = [];
    public ObservableCollection<VmVlanRow> VlanRows { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private string ipFilter = string.Empty;

    [ObservableProperty]
    private string macFilter = string.Empty;

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    // Currently selected report tab: 0 Switches, 1 Adapters, 2 VLAN.
    // Drives IReportService publication so Export means the visible table.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    partial void OnIpFilterChanged(string value) => RebuildAdapters();
    partial void OnMacFilterChanged(string value) => RebuildAdapters();

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Network"];
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
            var switchTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.Switches, key, force,
                ct => _hyperV.GetVirtualSwitchesAsync(targets.Nodes, ct), cancellationToken);
            var nicTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmNetwork, key, force,
                ct => _hyperV.GetVmNetworkAdaptersAsync(targets.Nodes, ct), cancellationToken);
            var vlanTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.Vlans, key, force,
                ct => _hyperV.GetVmAdapterVlansAsync(targets.Nodes, ct), cancellationToken);
            await Task.WhenAll(switchTask, nicTask, vlanTask).ConfigureAwait(true);

            var (switchLoaded, switchWarnings) = await switchTask.ConfigureAwait(true);
            var (nicLoaded, nicWarnings) = await nicTask.ConfigureAwait(true);
            var (vlanLoaded, vlanWarnings) = await vlanTask.ConfigureAwait(true);
            foreach (var warning in switchWarnings.Concat(nicWarnings).Concat(vlanWarnings))
            {
                AddWarning(warning);
            }

            SwitchRows.Clear();
            foreach (var row in switchLoaded
                         .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                SwitchRows.Add(row);
            }

            _allNetworkRows = nicLoaded
                .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.VMName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            VlanRows.Clear();
            foreach (var row in vlanLoaded)
            {
                VlanRows.Add(row);
            }

            RebuildAdapters();

            HasData = SwitchRows.Count > 0 || _allNetworkRows.Count > 0 || VlanRows.Count > 0;
            var countText = loc.Items(_allNetworkRows.Count, "adaptér", "adaptéry", "adaptérov", "adapter");
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
            _logger.LogError(ex, "Načítanie siete zlyhalo.");
            AddWarning(string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Warn_LoadFailed"], ex.Message));
            StatusText = loc["Shell_LoadFailedGeneric"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildAdapters()
    {
        var rows = VmRowFilters.ByIp(_allNetworkRows, IpFilter);
        rows = VmRowFilters.ByMac(rows, MacFilter);
        AdapterRows.Clear();
        foreach (var row in rows)
        {
            AdapterRows.Add(row);
        }

        PublishCurrent();
    }

    // Publishes the currently selected tab's rows — including empty results.
    // Pure local projection: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 0:
                _report.SetCurrent(SwitchRows.ToList(), "VmSwitches");
                break;
            case 2:
                _report.SetCurrent(VlanRows.ToList(), "VmVlans");
                break;
            default:
                _report.SetCurrent(AdapterRows.ToList(), "VMNetwork");
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

    private void SetEmpty(string status)
    {
        _allNetworkRows = [];
        SwitchRows.Clear();
        AdapterRows.Clear();
        VlanRows.Clear();
        PublishCurrent();
        HasData = false;
        StatusText = status;
    }
}
