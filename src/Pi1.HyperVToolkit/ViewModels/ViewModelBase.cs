using CommunityToolkit.Mvvm.ComponentModel;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>
/// Base class for all view models. Carries only shell-level state
/// (title, busy flag, status text) and the refresh contract used by the
/// shell Refresh/Cancel buttons. Cancellation is explicit: every load
/// receives a <see cref="CancellationToken"/> and no background threads
/// are created outside Task-based async work.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>
    /// Loads (or reloads) the view content. The default implementation reports
    /// that the section is pending migration; real views override it.
    /// Uses the session cache when valid: navigation and startup do NOT need
    /// an explicit Refresh. Must honour <paramref name="cancellationToken"/>
    /// and never block the UI thread.
    /// </summary>
    public virtual Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusText = Core.State.LocalizationService.Instance["Section_Pending"];
        return Task.CompletedTask;
    }

    /// <summary>
    /// Forces a new live read, replacing the cached snapshot. Wired to the
    /// shell Obnoviť/Refresh button. Default: same as <see cref="RefreshAsync"/>.
    /// </summary>
    public virtual Task ReloadAsync(CancellationToken cancellationToken) =>
        RefreshAsync(cancellationToken);
}
