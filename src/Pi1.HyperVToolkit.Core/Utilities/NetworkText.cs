using System.Text.RegularExpressions;

namespace Pi1.HyperVToolkit.Core.Utilities;

/// <summary>
/// Network string helpers. Parity with Get-PiIPv4 and the Find-PiVMByMac
/// normalization in Pi1.VM.psm1.
/// </summary>
public static partial class NetworkText
{
    [GeneratedRegex(@"^\d+\.")]
    private static partial Regex IPv4LeadRegex();

    [GeneratedRegex(@"[-:. ]")]
    private static partial Regex MacSeparatorRegex();

    /// <summary>
    /// Keeps only IPv4-looking addresses and joins them with ", ".
    /// Parity: ($IPAddresses | Where { $_ -match '^\d+\.' }) -join ', '.
    /// Null input yields an empty string.
    /// </summary>
    public static string FilterIPv4(IEnumerable<string?>? addresses)
    {
        if (addresses is null)
        {
            return string.Empty;
        }

        return string.Join(", ", addresses.Where(a => !string.IsNullOrEmpty(a) && IPv4LeadRegex().IsMatch(a!)));
    }

    /// <summary>
    /// Strips separators so searches match the separator-less Hyper-V MAC format.
    /// Parity: $mac -replace "[-:\. ]", "". Null input yields an empty string.
    /// The result is NOT upper-cased: PowerShell -like is case-insensitive,
    /// and future filtering must use ordinal-ignore-case comparison instead.
    /// </summary>
    public static string NormalizeMac(string? mac)
    {
        if (string.IsNullOrEmpty(mac))
        {
            return string.Empty;
        }

        return MacSeparatorRegex().Replace(mac, string.Empty);
    }
}
