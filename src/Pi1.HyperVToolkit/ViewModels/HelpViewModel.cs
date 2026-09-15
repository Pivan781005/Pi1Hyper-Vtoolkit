using CommunityToolkit.Mvvm.ComponentModel;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>
/// Localized help content. Paragraphs come from localization keys plus the
/// real application version (never a hardcoded stale number); they refresh
/// live on language change.
/// </summary>
public partial class HelpViewModel : ViewModelBase
{
    private readonly IAppInfoProvider _appInfo;

    public HelpViewModel(IAppInfoProvider appInfo)
    {
        _appInfo = appInfo ?? throw new ArgumentNullException(nameof(appInfo));
        Title = LocalizationService.Instance["Nav_Help"];
        RefreshTexts();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    private string purpose = string.Empty;

    [ObservableProperty]
    private string readOnly = string.Empty;

    [ObservableProperty]
    private string admin = string.Empty;

    [ObservableProperty]
    private string scope = string.Empty;

    [ObservableProperty]
    private string loading = string.Empty;

    [ObservableProperty]
    private string cache = string.Empty;

    [ObservableProperty]
    private string areas = string.Empty;

    [ObservableProperty]
    private string planned = string.Empty;

    [ObservableProperty]
    private string units = string.Empty;

    [ObservableProperty]
    private string paths = string.Empty;

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshTexts();

    private void RefreshTexts()
    {
        var loc = LocalizationService.Instance;
        Title = loc["Nav_Help"];
        Purpose = string.Format(System.Globalization.CultureInfo.InvariantCulture, loc["Help_Purpose"], _appInfo.ApplicationVersion);
        ReadOnly = loc["Help_ReadOnly"];
        Admin = loc["Help_Admin"];
        Scope = loc["Help_Scope"];
        Loading = loc["Help_Loading"];
        Cache = loc["Help_Cache"];
        Areas = loc["Help_Areas"];
        Planned = loc["Help_Planned"];
        Units = loc["Help_Units"];
        Paths = loc["Help_Paths"];
    }
}
