namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Persisted application settings. Serialized with System.Text.Json to the
/// application-data location (see <see cref="Services.IAppPaths"/>).
/// </summary>
/// <param name="ScopeMode">Last selected scope mode.</param>
/// <param name="SelectedNode">Last selected node (only meaningful for <see cref="Models.ScopeMode.Node"/>.</param>
/// <param name="ShowLegend">Whether RAM legends are shown. Parity with $Script:ShowLegend.</param>
/// <param name="Language">Stable UI language code: "sk" (Slovenčina, default) or "en" (English).</param>
public sealed record AppSettings(ScopeMode ScopeMode, string SelectedNode, bool ShowLegend, string Language = "sk")
{
    public static AppSettings Default => new(ScopeMode.Local, string.Empty, true);

    public Localization.AppLanguage AppLanguage => Language == "en"
        ? Localization.AppLanguage.English
        : Localization.AppLanguage.Slovak;
}
