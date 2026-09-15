using System.Text.RegularExpressions;
using Microsoft.Management.Infrastructure;

namespace Pi1.HyperVToolkit.Infrastructure.Mapping;

// Canonicalization for CIM/WMI reference-typed properties (e.g. Parent).
// A reference arrives EITHER as a live CimInstance (Microsoft.Management.
// Infrastructure represents reference values that way) OR as a textual WMI
// object path (unit fakes, some providers):
//   \\NODE\root\virtualization\v2:Msvm_ResourceAllocationSettingData.
//     InstanceID="Microsoft:VSSD\\CTRL\\0"
// Both shapes canonicalize to the bare key value:
//   Microsoft:VSSD\CTRL\0
// (the doubled backslash inside the quoted WMI value is ONE real backslash).
// Matching stays exact-ID based: NO GUID-overlap merging, NO cross-VSSD joins.
public static partial class ReferenceIds
{
    [GeneratedRegex(@"(?:InstanceID|Name|DeviceID)\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex InstanceIdKeyRegex();

    /// <summary>
    /// Extracts the canonical InstanceID from a raw CIM reference value:
    /// a CimInstance yields its key property directly — InstanceID first
    /// (Hyper-V references), then any Key-flagged property (e.g. DeviceID on
    /// Win32 associations). Convert.ToString on such an object is NOT
    /// sufficient. A string goes through path normalization.
    /// Returns null when no identity can be determined.
    /// </summary>
    public static string? TryGetReferencedInstanceId(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CimInstance instance)
        {
            var key = instance.CimInstanceProperties
                .FirstOrDefault(p => string.Equals(p.Name, "InstanceID", StringComparison.OrdinalIgnoreCase))
                ?? instance.CimInstanceProperties
                    .FirstOrDefault(p => (p.Flags & CimFlags.Key) == CimFlags.Key);
            var keyText = key?.Value is string s
                ? s
                : Convert.ToString(key?.Value, System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(keyText) ? null : keyText.Trim();
        }

        if (value is string pathText)
        {
            var normalized = NormalizeReferenceInstanceId(pathText);
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }

        return null;
    }

    /// <summary>
    /// Normalizes a textual reference: a full WMI object path reduces to its
    /// quoted InstanceID key (unescaped); an already-plain InstanceID passes
    /// through trimmed.
    /// </summary>
    public static string NormalizeReferenceInstanceId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var match = InstanceIdKeyRegex().Match(trimmed);
        if (match.Success)
        {
            return UnescapeWmiQuotedValue(match.Groups[1].Value);
        }

        return trimmed;
    }

    private static string UnescapeWmiQuotedValue(string value) =>
        value.Replace("\\\\", "\\").Replace("\\\"", "\"");
}
