using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Calculations;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>
/// Nodes view model. One refresh collects node hardware once and reuses it
/// for the capacity aggregation and the host-settings join (the PowerShell
/// N+1 re-query is intentionally not reproduced). Parity with Pi1.Nodes.psm1.
/// </summary>
public partial class NodesViewModel : ViewModelBase
{
    private readonly ITargetNodeResolver _targets;
    private readonly ISystemInformationService _systemInfo;
    private readonly IHyperVService _hyperV;
    private readonly IScopeService _scope;
    private readonly IReportService _report;
    private readonly ISessionCache _cache;
    private readonly ILogger<NodesViewModel> _logger;

    private List<NodeHardwareRow> _hardware = [];
    private List<VirtualMachineRow> _vmRows = [];

    public NodesViewModel(
        ITargetNodeResolver targets,
        ISystemInformationService systemInfo,
        IHyperVService hyperV,
        IScopeService scope,
        IReportService report,
        ISessionCache cache,
        ILogger<NodesViewModel> logger)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Nodes"];
    }

    public ObservableCollection<NodeHardwareRow> HardwareRows { get; } = [];
    public ObservableCollection<NodeCapacityRow> CapacityRows { get; } = [];
    public ObservableCollection<NodeVolumeRow> VolumeRows { get; } = [];
    public ObservableCollection<NodeNetworkAdapterRow> AdapterRows { get; } = [];
    public ObservableCollection<HostSettingsRow> HostSettingsRows { get; } = [];
    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private NodeHardwareRow? selectedNode;

    [ObservableProperty]
    private string selectedNodeTitle = LocalizationService.Instance["D_NoNode"];

    [ObservableProperty]
    private HostSettingsRow? selectedHostSettings;

    [ObservableProperty]
    private string selectedHostTitle = LocalizationService.Instance["D_NoHost"];

    [ObservableProperty]
    private bool hasWarnings;

    [ObservableProperty]
    private bool hasData;

    // Currently selected report tab: 0 Hardware, 1 Capacity, 2 Volumes,
    // 3 Network adapters, 4 Host settings. Drives IReportService publication.
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) => PublishCurrent();

    partial void OnSelectedNodeChanged(NodeHardwareRow? value) =>
        SelectedNodeTitle = value?.Node ?? LocalizationService.Instance["D_NoNode"];

    partial void OnSelectedHostSettingsChanged(HostSettingsRow? value) =>
        SelectedHostTitle = value?.Node ?? LocalizationService.Instance["D_NoHost"];

    public override Task RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: false);

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken, force: true);

    private async Task LoadAsync(CancellationToken cancellationToken, bool force)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Nodes"];
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
            var (hwRows, hwWarnings) = await CacheLoad.SinglesAsync(
                _cache, SessionCacheKeys.Hardware, key, force,
                ct => _systemInfo.GetNodeHardwareAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);

            var volTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.NodeVolumes, key, force,
                ct => _systemInfo.GetNodeVolumesAsync(targets.Nodes, ct), cancellationToken);
            var nicTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.NodeAdapters, key, force,
                ct => _systemInfo.GetNodeAdaptersAsync(targets.Nodes, ct), cancellationToken);
            var vmTask = CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmBase, key, force,
                ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken);
            await Task.WhenAll(volTask, nicTask, vmTask).ConfigureAwait(true);

            var (volRows, volWarnings) = await volTask.ConfigureAwait(true);
            var (nicRows, nicWarnings) = await nicTask.ConfigureAwait(true);
            var (vmLoaded, vmWarnings) = await vmTask.ConfigureAwait(true);
            foreach (var warning in hwWarnings.Concat(volWarnings).Concat(nicWarnings).Concat(vmWarnings))
            {
                AddWarning(warning);
            }

            _hardware = hwRows
                .OrderBy(h => h.Node, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // VM rows feed the capacity aggregation (same cached dataset as
            // the VMs view — no second collector).
            _vmRows = vmLoaded.ToList();

            var hwByNode = _hardware.ToDictionary(h => h.Node, h => (NodeHardwareRow?)h, StringComparer.OrdinalIgnoreCase);
            var (hostRows, hostWarnings) = await CacheLoad.SinglesAsync(
                _cache, SessionCacheKeys.HostSettings, key, force,
                ct => _hyperV.GetHostSettingsAsync(targets.Nodes, hwByNode, ct), cancellationToken).ConfigureAwait(true);
            foreach (var warning in hostWarnings)
            {
                AddWarning(warning);
            }

            HardwareRows.Clear();
            foreach (var hw in _hardware)
            {
                HardwareRows.Add(hw);
            }

            CapacityRows.Clear();
            foreach (var node in targets.Nodes)
            {
                CapacityRows.Add(NodeCapacityCalculator.Compute(node, _vmRows, hwByNode.TryGetValue(node, out var h) ? h : null));
            }

            VolumeRows.Clear();
            foreach (var row in volRows.OrderBy(v => v.Node).ThenBy(v => v.Drive))
            {
                VolumeRows.Add(row);
            }

            AdapterRows.Clear();
            foreach (var row in nicRows.OrderBy(a => a.Node).ThenBy(a => a.Description))
            {
                AdapterRows.Add(row);
            }

            HostSettingsRows.Clear();
            foreach (var row in hostRows)
            {
                HostSettingsRows.Add(row);
            }

            PublishCurrent();

            if (SelectedNode is not null && !_hardware.Contains(SelectedNode))
            {
                SelectedNode = null;
            }

            HasData = _hardware.Count > 0;
            var countText = loc.Items(_hardware.Count, "uzol", "uzly", "uzlov", "node");
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
            _logger.LogError(ex, "Načítanie uzlov zlyhalo.");
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
        _hardware = [];
        _vmRows = [];
        HardwareRows.Clear();
        CapacityRows.Clear();
        VolumeRows.Clear();
        AdapterRows.Clear();
        HostSettingsRows.Clear();
        PublishCurrent();
        HasData = false;
        StatusText = status;
    }

    // Publishes the currently selected tab's rows — including empty results.
    // Pure local projection: no provider calls.
    private void PublishCurrent()
    {
        switch (SelectedTabIndex)
        {
            case 1:
                _report.SetCurrent(CapacityRows.ToList(), "NodeCapacity");
                break;
            case 2:
                _report.SetCurrent(VolumeRows.ToList(), "NodeVolumes");
                break;
            case 3:
                _report.SetCurrent(AdapterRows.ToList(), "NodeAdapters");
                break;
            case 4:
                _report.SetCurrent(HostSettingsRows.ToList(), "HostSettings");
                break;
            default:
                _report.SetCurrent(HardwareRows.ToList(), "NodeHardware");
                break;
        }
    }
}
