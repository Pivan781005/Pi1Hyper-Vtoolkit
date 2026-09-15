using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>Scope option shown in the shell scope bar (Slovak labels, English enum values).</summary>
public sealed record ScopeOption(ScopeMode Mode, string Label);

/// <summary>
/// Main shell view model: navigation, scope bar state, capability badges,
/// status bar text and the shell-level Refresh/Cancel commands.
/// Navigation targets are supplied via constructor injection; the migration
/// placeholders are dependency-free and created inline.
/// Sections auto-load on startup and on navigation (session cache serves
/// repeat visits); Refresh forces a new live read.
/// Sections auto-load on startup and on navigation (session cache serves
/// repeat visits); Refresh forces a new live read.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IScopeService _scope;
    private readonly ISettingsService _settings;
    private readonly ICapabilityProbeService _probes;
    private readonly IPrivilegeService _privilege;
    private readonly ICapabilitySnapshot _capabilities;
    private readonly IScopePolicy _scopePolicy;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<(string Key, ViewModelBase ViewModel)> _sections = [];
    private readonly Dispatcher? _uiDispatcher;
    private CancellationTokenSource? _refreshCts;
    private bool _initialized;
    public MainViewModel(
        IScopeService scope,
        ISettingsService settings,
        ICapabilityProbeService probes,
        IPrivilegeService privilege,
        ICapabilitySnapshot capabilities,
        IScopePolicy scopePolicy,
        VmsViewModel vmsViewModel,
        NodesViewModel nodesViewModel,
        DashboardViewModel dashboardViewModel,
        StorageViewModel storageViewModel,
        NetworkingViewModel networkingViewModel,
        ClusterViewModel clusterViewModel,
        DiagnosticsViewModel diagnosticsViewModel,
        ExportViewModel exportViewModel,
        SettingsViewModel settingsViewModel,
        HelpViewModel helpViewModel,
        ILogger<MainViewModel> logger)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _probes = probes ?? throw new ArgumentNullException(nameof(probes));
        _privilege = privilege ?? throw new ArgumentNullException(nameof(privilege));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _scopePolicy = scopePolicy ?? throw new ArgumentNullException(nameof(scopePolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // UI thread affinity for language-change handling: title/collection
        // updates must run where the shell was built. Null off-UI-thread
        // (tests) means synchronous inline handling.
        _uiDispatcher = Dispatcher.FromThread(Thread.CurrentThread);

        Title = "π1 Hyper-V Toolkit";
        StatusText = LocalizationService.Instance["Shell_Probing"];

        _sections.Add(("Nav_Dashboard", dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel))));
        _sections.Add(("Nav_VMs", vmsViewModel ?? throw new ArgumentNullException(nameof(vmsViewModel))));
        _sections.Add(("Nav_Nodes", nodesViewModel ?? throw new ArgumentNullException(nameof(nodesViewModel))));
        _sections.Add(("Nav_Cluster", clusterViewModel ?? throw new ArgumentNullException(nameof(clusterViewModel))));
        _sections.Add(("Nav_Storage", storageViewModel ?? throw new ArgumentNullException(nameof(storageViewModel))));
        _sections.Add(("Nav_Networking", networkingViewModel ?? throw new ArgumentNullException(nameof(networkingViewModel))));
        _sections.Add(("Nav_Diagnostics", diagnosticsViewModel ?? throw new ArgumentNullException(nameof(diagnosticsViewModel))));
        _sections.Add(("Nav_Export", exportViewModel ?? throw new ArgumentNullException(nameof(exportViewModel))));
        Diagnostics = diagnosticsViewModel;
        Dashboard = dashboardViewModel;
        dashboardViewModel.AdvisorRequested += (_, _) => GoToDiagnosticsAdvisor();
        _sections.Add(("Nav_Settings", settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel))));
        _sections.Add(("Nav_Help", helpViewModel ?? throw new ArgumentNullException(nameof(helpViewModel))));

        NavItems = [];
        RefreshTitles();
        RefreshScopeOptions();

        SelectedNavItem = NavItems[0];
        SelectedScopeMode = _scope.Current.Mode;
        NodeName = _scope.Current.SelectedNode;
        IsNodeScope = SelectedScopeMode == ScopeMode.Node;
        OnPropertyChanged(nameof(ScopeLabel));

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public DashboardViewModel Dashboard { get; }

    /// <summary>
    /// Dashboard Advisor shortcut target: activates Diagnostics and selects
    /// the Advisor tab. Single Advisor implementation — no duplicate.
    /// </summary>
    public void GoToDiagnosticsAdvisor()
    {
        Diagnostics.SelectAdvisorTab();
        var item = NavItems.FirstOrDefault(n => ReferenceEquals(n.ViewModel, Diagnostics));
        if (item is not null)
        {
            SelectedNavItem = item;
        }
    }

    public IReadOnlyList<ScopeOption> ScopeOptions { get; private set; } = [];

    private void RefreshTitles()
    {
        var loc = LocalizationService.Instance;
        var current = SelectedNavItem?.ViewModel;
        NavItems.Clear();
        foreach (var (key, vm) in _sections)
        {
            if (vm is SectionPlaceholderViewModel placeholder)
            {
                placeholder.RefreshTexts();
            }
            else
            {
                vm.Title = loc[key];
            }

            NavItems.Add(new NavItem(vm.Title, vm));
        }

        SelectedNavItem = NavItems.FirstOrDefault(n => current is not null && n.ViewModel == current) ?? NavItems[0];
    }

    private void RefreshScopeOptions()
    {
        var loc = LocalizationService.Instance;
        ScopeOptions =
        [
            new(ScopeMode.Local, loc["ScopeOption_Local"]),
            new(ScopeMode.Cluster, loc["ScopeOption_Cluster"]),
            new(ScopeMode.Node, loc["ScopeOption_Node"]),
        ];
        OnPropertyChanged(nameof(ScopeOptions));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Language can be switched from any thread (e.g. Settings save is UI,
        // but nothing guarantees it); bound collections demand UI affinity.
        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(new Action(ApplyLanguageChange));
            return;
        }

        ApplyLanguageChange();
    }

    private void ApplyLanguageChange()
    {
        RefreshTitles();
        RefreshScopeOptions();
        OnPropertyChanged(nameof(ScopeLabel));
        OnPropertyChanged(nameof(AdminLabel));
        OnPropertyChanged(nameof(HyperVStatusText));
        OnPropertyChanged(nameof(ClusterStatusText));
        OnPropertyChanged(nameof(CimStatusText));
        // Views re-render bound texts live; reload the current section so
        // converters and status texts follow the new language as well.
        _ = LoadCurrentViewAsync(force: false);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentView))]
    private NavItem? selectedNavItem;

    public ViewModelBase? CurrentView => SelectedNavItem?.ViewModel;

    [ObservableProperty]
    private ScopeMode selectedScopeMode;

    [ObservableProperty]
    private string nodeName = string.Empty;

    [ObservableProperty]
    private bool isNodeScope;

    [ObservableProperty]
    private string scopeApplyMessage = string.Empty;

    [ObservableProperty]
    private CapabilityReport? hyperV;

    [ObservableProperty]
    private CapabilityReport? cluster;

    [ObservableProperty]
    private CapabilityReport? cim;

    [ObservableProperty]
    private bool capabilitiesLoaded;

    [ObservableProperty]
    private bool hyperVWarningVisible;

    [ObservableProperty]
    private string hyperVWarningText = string.Empty;

    public string AdminLabel
    {
        get
        {
            var loc = LocalizationService.Instance;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                loc["Status_Admin"],
                loc[_privilege.IsAdministrator ? "V_Yes" : "V_No"]);
        }
    }

    public bool IsAdministrator => _privilege.IsAdministrator;

    public string ScopeLabel => LocalizationService.Instance.ScopeLabel(_scope.Current, _scope.MachineName);

    public string HyperVStatusText => CapabilityStatusText(HyperV, "Cap_HyperV_Ok", "Cap_HyperV_Missing");

    public string ClusterStatusText => CapabilityStatusText(Cluster, "Cap_Cluster_Ok", "Cap_Cluster_Missing");

    public string CimStatusText => CapabilityStatusText(Cim, "Cap_Cim_Ok", "Cap_Cim_Missing");

    private static string CapabilityStatusText(CapabilityReport? report, string okKey, string missingKey)
    {
        var loc = LocalizationService.Instance;
        if (report is null)
        {
            return "…";
        }

        return loc[report.IsAvailable ? okKey : missingKey];
    }

    partial void OnSelectedScopeModeChanged(ScopeMode value)
    {
        IsNodeScope = value == ScopeMode.Node;
        ScopeApplyMessage = string.Empty;
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        _ = value;
        if (_initialized)
        {
            // Navigation auto-loads the section (cache serves repeat visits).
            _ = LoadCurrentViewAsync(force: false);
        }
    }

    /// <summary>
    /// Startup initialization: restores persisted settings (including UI
    /// language), applies the scope and probes capabilities, then
    /// automatically loads the Dashboard. Never throws — failures become
    /// status text.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var loc = LocalizationService.Instance;
        try
        {
            StatusText = loc["Shell_Probing"];
            HyperV = await _probes.CheckHyperVAsync(cancellationToken).ConfigureAwait(true);
            Cluster = await _probes.CheckClusterAsync(cancellationToken).ConfigureAwait(true);
            Cim = await _probes.CheckCimAsync(cancellationToken).ConfigureAwait(true);
            CapabilitiesLoaded = true;
            _capabilities.Update(HyperV, Cluster, Cim);
            OnPropertyChanged(nameof(HyperVStatusText));
            OnPropertyChanged(nameof(ClusterStatusText));
            OnPropertyChanged(nameof(CimStatusText));

            var saved = _settings.Load();
            loc.SetLanguage(saved.AppLanguage);
            var decision = _scopePolicy.Evaluate(
                saved.ScopeMode,
                string.IsNullOrWhiteSpace(saved.SelectedNode) ? null : saved.SelectedNode,
                _capabilities.IsClusterAvailable);
            ApplyScopeDecision(decision, "uložených nastavení");
            _scope.ScopeChanged += OnScopeChanged;

            HyperVWarningVisible = HyperV is { IsAvailable: false };
            HyperVWarningText = loc["Banner_HyperVUnavailable"];

            if (string.IsNullOrEmpty(ScopeApplyMessage))
            {
                StatusText = HyperV is { IsAvailable: false }
                    ? loc["Shell_HyperVUnavailable"]
                    : loc["Shell_Ready"];
            }

            _initialized = true;
            // Startup auto-load: the Dashboard appears with data, no first
            // Refresh click required.
            await LoadCurrentViewAsync(force: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = loc["Shell_InitCancelled"];
            _logger.LogInformation("Inicializácia MainViewModel bola zrušená.");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Shell_InitFailed"], ex.Message);
            _logger.LogError(ex, "Inicializácia MainViewModel zlyhala.");
        }
    }

    private void OnScopeChanged(object? sender, ScopeState state)
    {
        OnPropertyChanged(nameof(ScopeLabel));
        SelectedScopeMode = state.Mode;
        NodeName = state.SelectedNode;
        if (_initialized)
        {
            // Scope keys isolate cached snapshots, so stale data can never
            // leak across scopes; reload the current section right away.
            _ = LoadCurrentViewAsync(force: false);
        }
    }

    /// <summary>
    /// Activates a policy decision: applies the allowed scope, syncs the scope bar
    /// and surfaces fallback/invalid messages. Never persists anything.
    /// </summary>
    private void ApplyScopeDecision(ScopeDecision decision, string source)
    {
        if (!decision.IsValid || decision.Allowed is null)
        {
            _logger.LogWarning("Neplatný scope z {Source}: {Message}", source, decision.Message);
            _scope.SetScope(ScopeMode.Local);
            ScopeApplyMessage = decision.Message ?? string.Empty;
            StatusText = ScopeApplyMessage;
        }
        else
        {
            _scope.SetScope(decision.Allowed.Mode, decision.Allowed.SelectedNode);
            if (decision.FellBack)
            {
                _logger.LogWarning("Scope fallback ({Source}): {Message}", source, decision.Message);
                ScopeApplyMessage = decision.Message ?? string.Empty;
                StatusText = ScopeApplyMessage;
            }
            else
            {
                ScopeApplyMessage = string.Empty;
            }
        }

        SelectedScopeMode = _scope.Current.Mode;
        NodeName = _scope.Current.SelectedNode;
        OnPropertyChanged(nameof(ScopeLabel));
    }

    [RelayCommand]
    private async Task ApplyScopeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var decision = _scopePolicy.Evaluate(SelectedScopeMode, NodeName, _capabilities.IsClusterAvailable);
            if (!decision.IsValid || decision.Allowed is null)
            {
                ScopeApplyMessage = decision.Message ?? string.Empty;
                return;
            }

            _scope.SetScope(decision.Allowed.Mode, decision.Allowed.SelectedNode);

            // Persist the allowed scope — never an invalid Cluster request.
            var current = _settings.Load();
            await _settings.SaveAsync(
                current with { ScopeMode = decision.Allowed.Mode, SelectedNode = decision.Allowed.SelectedNode },
                cancellationToken).ConfigureAwait(true);

            var loc = LocalizationService.Instance;
            ScopeApplyMessage = decision.FellBack
                ? decision.Message ?? string.Empty
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    loc["Scope_Applied"],
                    loc.ScopeLabel(_scope.Current, _scope.MachineName));
            StatusText = ScopeApplyMessage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScopeApplyMessage = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                LocalizationService.Instance["Scope_SaveFailed"],
                ex.Message);
            _logger.LogError(ex, "Uloženie scope do nastavení zlyhalo.");
        }
    }

    [RelayCommand]
    private Task RefreshCurrentAsync(CancellationToken cancellationToken) =>
        LoadCurrentViewAsync(force: true, cancellationToken);

    /// <summary>
    /// Loads the current section: from session cache when valid, otherwise
    /// from the backend. Force means a new live read. Never throws — section
    /// failures become section status text.
    /// </summary>
    private async Task LoadCurrentViewAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (CurrentView is null)
        {
            return;
        }

        var loc = LocalizationService.Instance;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            CurrentView.IsBusy = true;
            CancelRefreshCommand.NotifyCanExecuteChanged();
            if (force)
            {
                await CurrentView.ReloadAsync(_refreshCts.Token).ConfigureAwait(true);
            }
            else
            {
                await CurrentView.RefreshAsync(_refreshCts.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            CurrentView.StatusText = loc["Shell_OpCancelled"];
        }
        catch (Exception ex)
        {
            CurrentView.StatusText = string.Format(
                System.Globalization.CultureInfo.InvariantCulture, loc["Shell_LoadFailed"], ex.Message);
            _logger.LogError(ex, "Obnovenie sekcie {Title} zlyhalo.", CurrentView.Title);
        }
        finally
        {
            CurrentView.IsBusy = false;
            CancelRefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancelRefresh() => CurrentView?.IsBusy == true;

    [RelayCommand(CanExecute = nameof(CanCancelRefresh))]
    private void CancelRefresh()
    {
        _refreshCts?.Cancel();
    }
}
