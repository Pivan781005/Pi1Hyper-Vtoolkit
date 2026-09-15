namespace Pi1.HyperVToolkit.Core.Localization;

// Stable lookup over both presentation languages. Unknown keys fall back to
// the key itself so a missing translation is visible, never blank.
public static partial class UiStrings
{
    public static string Get(AppLanguage language, string key)
    {
        var table = language == AppLanguage.English ? English : Slovak;
        if (table.TryGetValue(key, out var value))
        {
            return value;
        }

        return key;
    }

    public static IReadOnlyList<string> AllKeys() =>
        Slovak.Keys.Union(English.Keys, StringComparer.Ordinal).ToList();
}
