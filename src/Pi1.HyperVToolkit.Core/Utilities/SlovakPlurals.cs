namespace Pi1.HyperVToolkit.Core.Utilities;

/// <summary>
/// Slovak count + noun agreement (1 záznam / 2 záznamy / 5 záznamov).
/// UI text only; no effect on collected data or thresholds.
/// </summary>
public static class SlovakPlurals
{
    public static string Format(int count, string one, string few, string many)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(one);
        ArgumentException.ThrowIfNullOrWhiteSpace(few);
        ArgumentException.ThrowIfNullOrWhiteSpace(many);

        var form = (count % 10 == 1 && count % 100 != 11) ? one
            : (count % 10 is >= 2 and <= 4 && (count % 100 < 12 || count % 100 > 14)) ? few
            : many;
        return $"{count} {form}";
    }
}
