using Pi1.HyperVToolkit.Core.Localization;

namespace Pi1.HyperVToolkit.Core.Services;

// Presentation-language service. Backed by the global UI language state;
// C# identifiers, Core models and parity values are NEVER mutated here —
// only display strings are produced.
public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    string this[string key] { get; }

    string Get(string key);

    void SetLanguage(AppLanguage language);

    event EventHandler? LanguageChanged;
}
