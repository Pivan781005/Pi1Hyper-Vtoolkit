using System.Text.RegularExpressions;

namespace Pi1.HyperVToolkit.Core.Storage;

// Pure parser for the Msvm_VirtualHardDiskSettingData embedded instance
// returned by Msvm_ImageManagementService.GetVirtualHardDiskSettingData.
// The real provider returns CIM-XML ("<INSTANCE CLASSNAME=...>...<PROPERTY
// NAME="Type"><VALUE>4</VALUE>...</INSTANCE>"), parsed with XDocument as the
// PRIMARY format; legacy MOF assignment text ("Type = 3;") is retained as a
// fallback for compatibility and unit fixtures. Only scalar fields needed for
// Get-VHD parity (Type, Format, MaxInternalSize, Path) are extracted.
// No WMI here — fully unit-testable.
public static partial class VhdSettingDataParser
{
    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>""(?:[^""\\]|\\.)*""|[^;]*);")]
    private static partial Regex AssignmentRegex();

    public sealed record VhdSettingData(
        int? Type,
        int? Format,
        ulong? MaxInternalSize,
        string Path);

    public static VhdSettingData Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(null, null, null, string.Empty);
        }

        var fromXml = TryParseCimXml(text);
        if (fromXml is not null)
        {
            return fromXml;
        }

        return ParseMof(text);
    }

    private static VhdSettingData? TryParseCimXml(string text)
    {
        if (!text.Contains("<PROPERTY", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("<INSTANCE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var document = System.Xml.Linq.XDocument.Parse(text, System.Xml.Linq.LoadOptions.None);
            string? Property(string name)
            {
                var property = document.Descendants()
                    .FirstOrDefault(e =>
                        string.Equals(e.Name.LocalName, "PROPERTY", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            (string?)e.Attribute("NAME"), name, StringComparison.OrdinalIgnoreCase));
                var value = property?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "VALUE", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            var typeText = Property("Type");
            var formatText = Property("Format");
            var sizeText = Property("MaxInternalSize");
            var path = Property("Path") ?? string.Empty;
            if (typeText is null && formatText is null && sizeText is null && string.IsNullOrEmpty(path))
            {
                return null;
            }

            return new(
                ParseIntText(typeText),
                ParseIntText(formatText),
                ParseULongText(sizeText),
                path);
        }
        catch (Exception) when (text is string)
        {
            return null;
        }
    }

    private static VhdSettingData ParseMof(string mofText)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AssignmentRegex().Matches(mofText))
        {
            values[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
        }

        return new(
            ParseIntText(values.TryGetValue("Type", out var type) ? Unquote(type) : null),
            ParseIntText(values.TryGetValue("Format", out var format) ? Unquote(format) : null),
            ParseULongText(values.TryGetValue("MaxInternalSize", out var size) ? Unquote(size) : null),
            Unquote(values.TryGetValue("Path", out var path) ? path : string.Empty));
    }

    // Msvm_VirtualHardDiskSettingData.Type (MS docs, verified ValueMap 2|3|4).
    public static string MapVhdType(int? type) => type switch
    {
        2 => "Fixed",
        3 => "Dynamic",
        4 => "Differencing",
        null => "Unknown",
        _ => "Unknown",
    };

    // Msvm_VirtualHardDiskSettingData.Format (MS docs: 2=VHD, 3=VHDX, 4=VHDSet).
    public static string MapVhdFormat(int? format) => format switch
    {
        2 => "VHD",
        3 => "VHDX",
        4 => "VHDSet",
        null => "Unknown",
        _ => "Unknown",
    };

    // Get-VHD VhdType strings map back onto the same canonical names; anything
    // unexpected normalizes to Unknown (never invent a type).
    public static string NormalizeVhdType(string? text) => text switch
    {
        "Fixed" or "Dynamic" or "Differencing" => text,
        null or "" => "Unknown",
        _ => "Unknown",
    };

    public static string NormalizeVhdFormat(string? text) => text switch
    {
        "VHD" or "VHDX" or "VHDSet" => text,
        null or "" => "Unknown",
        _ => "Unknown",
    };

    private static int? ParseIntText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static ulong? ParseULongText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            ulong.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string Unquote(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return trimmed;
    }
}
