using System.ComponentModel;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

// Global UI-language state. The single shared instance
// (LocalizationService.Instance, also registered in DI) backs XAML bindings;
// WPF refreshes every Loc-bound text when LanguageChanged fires because the
// indexer raises PropertyChanged for Item[].
public sealed class LocalizationService : ILocalizationService, INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private AppLanguage _language = AppLanguage.Slovak;

    public AppLanguage CurrentLanguage => _language;

    public string this[string key] => Get(key);

    public string Get(string key) => UiStrings.Get(_language, key);

    /// <summary>Count text with language-appropriate plurals ("5 záznamov VM" / "5 VM records").</summary>
    public string Items(int count, string skOne, string skFew, string skMany, string enNoun) =>
        _language == AppLanguage.English
            ? $"{count} {enNoun}{(count == 1 ? string.Empty : "s")}"
            : Utilities.SlovakPlurals.Format(count, skOne, skFew, skMany);

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public string ScopeLabel(Core.Models.ScopeState scope, string machineName)    {
        var node = scope.Mode == Core.Models.ScopeMode.Local ? machineName : scope.SelectedNode;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            Get(scope.Mode switch
            {
                Core.Models.ScopeMode.Cluster => "Scope_AllNodes",
                Core.Models.ScopeMode.Node => "Scope_SelectedNode",
                _ => "Scope_LocalNode",
            }),
            node);
    }

    public event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
}
