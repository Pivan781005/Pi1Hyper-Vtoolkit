using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>Language option with a stable code (persisted) and an endonym display name.</summary>
public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// Working settings editor: scope, selected node, UI language and RAM legend
/// visibility. Persisted as JSON; missing/corrupt files fall back to defaults.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IScopeService _scope;
    private readonly ISettingsService _settings;
    private readonly ICapabilitySnapshot _capabilities;
    private readonly IScopePolicy _scopePolicy;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IScopeService scope,
        ISettingsService settings,
        ICapabilitySnapshot capabilities,
        IScopePolicy scopePolicy,
        ILogger<SettingsViewModel> logger)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _scopePolicy = scopePolicy ?? throw new ArgumentNullException(nameof(scopePolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = LocalizationService.Instance["Nav_Settings"];
        var saved = _settings.Load();
        ScopeMode = saved.ScopeMode;
        NodeName = saved.SelectedNode;
        ShowLegend = saved.ShowLegend;
        Language = saved.Language;
        RebuildNodeOptions();
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new("sk", "Slovenčina"),
        new("en", "English"),
    ];

    public List<string> NodeOptions { get; } = [];

    [ObservableProperty]
    private ScopeMode scopeMode;

    [ObservableProperty]
    private string nodeName = string.Empty;

    [ObservableProperty]
    private bool showLegend = true;

    [ObservableProperty]
    private string language = "sk";

    [ObservableProperty]
    private string saveMessage = string.Empty;

    partial void OnScopeModeChanged(ScopeMode value) => RebuildNodeOptions();

    private void RebuildNodeOptions()
    {
        // Local machine is always available; the last saved node is offered
        // too. No new cluster collectors: available nodes come from the
        // existing scope architecture only.
        NodeOptions.Clear();
        NodeOptions.Add(_scope.MachineName);
        var saved = _settings.Load();
        if (!string.IsNullOrWhiteSpace(saved.SelectedNode) &&
            !NodeOptions.Contains(saved.SelectedNode, StringComparer.OrdinalIgnoreCase))
        {
            NodeOptions.Add(saved.SelectedNode);
        }

        if (!string.IsNullOrWhiteSpace(NodeName) &&
            !NodeOptions.Contains(NodeName, StringComparer.OrdinalIgnoreCase))
        {
            NodeOptions.Add(NodeName);
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var decision = _scopePolicy.Evaluate(ScopeMode, NodeName, _capabilities.IsClusterAvailable);
            if (!decision.IsValid || decision.Allowed is null)
            {
                SaveMessage = decision.Message ?? string.Empty;
                return;
            }

            _scope.SetScope(decision.Allowed.Mode, decision.Allowed.SelectedNode);

            var code = Language == "en" ? "en" : "sk";
            // Persist the allowed scope — never an invalid Cluster request.
            await _settings.SaveAsync(
                new AppSettings(decision.Allowed.Mode, decision.Allowed.SelectedNode, ShowLegend, code),
                cancellationToken).ConfigureAwait(true);

            // Language applies immediately (static texts rebind live; the
            // shell reloads the current section for converters/status texts).
            Language = code;
            loc.SetLanguage(code == "en"
                ? Core.Localization.AppLanguage.English
                : Core.Localization.AppLanguage.Slovak);
            RebuildNodeOptions();

            if (decision.FellBack)
            {
                _logger.LogWarning("Scope fallback (nastavenia): {Message}", decision.Message);
            }

            SaveMessage = decision.FellBack
                ? decision.Message ?? string.Empty
                : loc["Set_Saved"];
        }
        catch (ArgumentException ex)
        {
            SaveMessage = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveMessage = string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Set_SaveFailed"], ex.Message);
            _logger.LogError(ex, "Uloženie nastavení zlyhalo.");
        }
    }
}
