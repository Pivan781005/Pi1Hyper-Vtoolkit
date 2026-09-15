using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

// Export page view model. Consumes the IReportService current-result snapshot
// (never replaces it: navigating here only READS name/count) and the shared
// VM collector for the Full VM Report. Strictly read-only towards Hyper-V /
// cluster / storage — the only new write is the user-selected report file.
// Serializers live in Core/Export; this class only orchestrates snapshot,
// dialog, write and localized status.
public partial class ExportViewModel : ViewModelBase
{
    private readonly IReportService _report;
    private readonly IScopeService _scope;
    private readonly ITargetNodeResolver _targets;
    private readonly IHyperVService _hyperV;
    private readonly ISessionCache _cache;
    private readonly IExportDialogService _dialogs;
    private readonly IExportFileWriter _files;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExportViewModel> _logger;

    public ExportViewModel(
        IReportService report,
        IScopeService scope,
        ITargetNodeResolver targets,
        IHyperVService hyperV,
        ISessionCache cache,
        IExportDialogService dialogs,
        IExportFileWriter files,
        TimeProvider clock,
        ILogger<ExportViewModel> logger)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _hyperV = hyperV ?? throw new ArgumentNullException(nameof(hyperV));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = LocalizationService.Instance["Nav_Export"];
        RefreshMetadata();
        _report.CurrentChanged += (_, _) => RefreshMetadata();
    }

    public ObservableCollection<string> NodeWarnings { get; } = [];

    [ObservableProperty]
    private string currentReportName = string.Empty;

    [ObservableProperty]
    private int currentReportRows;

    [ObservableProperty]
    private bool hasReport;

    [ObservableProperty]
    private string scopeLabel = string.Empty;

    [ObservableProperty]
    private string lastExportedPath = string.Empty;

    [ObservableProperty]
    private bool hasWarnings;

    public override Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshMetadata();
        var loc = LocalizationService.Instance;
        StatusText = HasReport ? loc["Loaded_Simple"] : loc["Exp_NoResult"];
        return Task.CompletedTask;
    }

    public override Task ReloadAsync(CancellationToken cancellationToken) =>
        RefreshAsync(cancellationToken);

    [ObservableProperty]
    private string currentReportDisplayName = string.Empty;

    // Reads metadata only — never publishes, never clears the current result.
    private void RefreshMetadata()
    {
        var loc = LocalizationService.Instance;
        var name = _report.CurrentName;
        HasReport = !string.IsNullOrWhiteSpace(name) && _report.CurrentRows is not null;
        CurrentReportName = HasReport ? name : loc["Exp_NoReport"];
        // Friendly localized tab/section title for display; the canonical
        // ResultName stays available (tooltip) and drives CSV/JSON/export.
        CurrentReportDisplayName = HasReport
            ? ReportSchemas.DisplayName(name, loc.CurrentLanguage)
            : loc["Exp_NoReport"];
        CurrentReportRows = HasReport ? _report.Count : 0;
        // Localized presentation (current application language), same shared
        // mechanism as the shell status bar — never the hard-coded Slovak
        // ScopeService.GetScopeLabel(). RefreshAsync re-runs on navigation and
        // on language change, so the label follows SK/EN without restart.
        ScopeLabel = LocalizationService.Instance.ScopeLabel(_scope.Current, _scope.MachineName);
    }

    [RelayCommand]
    private Task ExportCurrentCsvAsync(CancellationToken cancellationToken) =>
        ExportCurrentAsync(ExportFormat.Csv, cancellationToken);

    [RelayCommand]
    private Task ExportCurrentHtmlAsync(CancellationToken cancellationToken) =>
        ExportCurrentAsync(ExportFormat.Html, cancellationToken);

    [RelayCommand]
    private Task ExportCurrentJsonAsync(CancellationToken cancellationToken) =>
        ExportCurrentAsync(ExportFormat.Json, cancellationToken);

    [RelayCommand]
    private Task ExportFullVmCsvAsync(CancellationToken cancellationToken) =>
        ExportFullVmAsync(ExportFormat.Csv, cancellationToken);

    [RelayCommand]
    private Task ExportFullVmHtmlAsync(CancellationToken cancellationToken) =>
        ExportFullVmAsync(ExportFormat.Html, cancellationToken);

    [RelayCommand]
    private Task ExportFullVmJsonAsync(CancellationToken cancellationToken) =>
        ExportFullVmAsync(ExportFormat.Json, cancellationToken);

    private async Task ExportCurrentAsync(ExportFormat format, CancellationToken cancellationToken)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        try
        {
            var snapshot = ReportSnapshot.Capture(_report);
            if (snapshot is null || snapshot.Rows.Count == 0)
            {
                StatusText = loc["Exp_NoResult"];
                return;
            }

            if (!ReportSchemas.TryGet(snapshot.Name, out var schema))
            {
                StatusText = loc["Exp_Unsupported"];
                _logger.LogWarning("Export: neznáma schéma reportu '{Name}'.", snapshot.Name);
                return;
            }

            await ExportSnapshotAsync(schema, snapshot, format, partialWarning: null, cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportFullVmAsync(ExportFormat format, CancellationToken cancellationToken)
    {
        var loc = LocalizationService.Instance;
        IsBusy = true;
        StatusText = loc["Load_Export"];
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
                StatusText = loc["Empty_NoTargets"];
                return;
            }

            // Fresh live collection (explicit report action, like the
            // reference Get-PiVMBaseRows call) through the SHARED fan-out —
            // no second collector. Cache is refreshed coherently.
            var key = SessionCacheKeys.ScopeKey(_scope.Current.Mode.ToString(), targets.Nodes);
            var (loaded, warnings) = await CacheLoad.NodeListAsync(
                _cache, SessionCacheKeys.VmBase, key, force: true,
                ct => _hyperV.GetVirtualMachinesAsync(targets.Nodes, ct), cancellationToken).ConfigureAwait(true);
            foreach (var warning in warnings)
            {
                AddWarning(warning);
            }

            var rows = loaded
                .OrderBy(r => r.HostNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.VM, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();
            if (rows.Count == 0)
            {
                StatusText = loc["Exp_NoResult"];
                return;
            }

            var schema = ReportSchemas.BuiltInNames.Contains(ReportSchemas.FullVMReportName)
                && ReportSchemas.TryGet(ReportSchemas.FullVMReportName, out var fullSchema)
                ? fullSchema
                : throw new InvalidOperationException($"Schema '{ReportSchemas.FullVMReportName}' is not registered.");
            var snapshot = new ReportSnapshot(ReportSchemas.FullVMReportName, rows);
            var partial = HasWarnings ? string.Join(" ", NodeWarnings) : null;
            await ExportSnapshotAsync(schema, snapshot, format, partial, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = loc["Shell_OpCancelled"];
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportSnapshotAsync(
        ReportSchema schema,
        ReportSnapshot snapshot,
        ExportFormat format,
        string? partialWarning,
        CancellationToken cancellationToken)
    {
        var loc = LocalizationService.Instance;
        var now = _clock.GetLocalNow();
        var extension = format switch
        {
            ExportFormat.Csv => "csv",
            ExportFormat.Html => "html",
            _ => "json",
        };
        var content = format switch
        {
            ExportFormat.Csv => ReportCsvSerializer.Serialize(schema, snapshot.Rows),
            ExportFormat.Html => ReportHtmlSerializer.Serialize(
                schema, snapshot.Rows, ScopeLabel, now, loc.CurrentLanguage),
            _ => ReportJsonSerializer.Serialize(schema, snapshot.Rows, ScopeLabel, now),
        };
        var defaultFileName = ExportFileName.BuildDefaultFileName(snapshot.Name, extension, now);
        var path = _dialogs.ShowSaveDialog(defaultFileName, DialogFilter(format), extension);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = loc["Exp_Cancelled"];
            return;
        }

        try
        {
            await _files.WriteAllTextAsync(path, content, ExportEncodings.Utf8Bom, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = loc["Shell_OpCancelled"];
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogError(ex, "Export reportu {Name} do {Path} zlyhal.", snapshot.Name, path);
            StatusText = string.Format(
                System.Globalization.CultureInfo.InvariantCulture, loc["Exp_Failed"], ex.Message);
            return;
        }

        LastExportedPath = path;
        var ok = string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Exp_Success"], path);
        StatusText = string.IsNullOrWhiteSpace(partialWarning)
            ? ok
            : $"{ok} {string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Exp_PartialNote"], partialWarning)}";
    }

    private static string DialogFilter(ExportFormat format) => format switch
    {
        ExportFormat.Csv => "CSV (*.csv)|*.csv",
        ExportFormat.Html => "HTML (*.html)|*.html",
        _ => "JSON (*.json)|*.json",
    };

    private void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning) && !NodeWarnings.Contains(warning))
        {
            NodeWarnings.Add(warning);
            HasWarnings = true;
        }
    }

    private enum ExportFormat
    {
        Csv = 0,
        Html = 1,
        Json = 2,
    }
}
