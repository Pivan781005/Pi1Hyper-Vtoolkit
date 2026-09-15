using System.Text;
using Pi1.HyperVToolkit.Core.Localization;

namespace Pi1.HyperVToolkit.Core.Export;

// Human-readable single self-contained .html file: embedded CSS only, no
// JavaScript, no CDN, no external assets, no network access. Headers are
// localized per the CURRENT UI language; every dynamic value is HTML-encoded
// so hostile text (e.g. <script>) renders as inert text. Long fields wrap via
// CSS (word-wrap), never truncated.
public static class ReportHtmlSerializer
{
    public static string Serialize(
        ReportSchema schema,
        IReadOnlyList<object> rows,
        string scopeLabel,
        DateTimeOffset generatedAt,
        AppLanguage language)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        var when = generatedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var lang = language == AppLanguage.English ? "en" : "sk";
        var title = $"π1 Hyper-V Toolkit – {schema.Name}";
        var headers = schema.Columns
            .Select(c => UiStrings.Get(language, c.HeaderKey))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(lang).Append("\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n<title>").Append(Encode(title)).Append("</title>\n");
        sb.Append("<style>\n");
        sb.Append("body { font-family: sans-serif; margin: 24px; color: #212121; }\n");
        sb.Append("h1 { font-size: 20px; }\n");
        sb.Append("p.meta { color: #616161; font-size: 13px; }\n");
        sb.Append("table { border-collapse: collapse; }\n");
        sb.Append("th, td { border: 1px solid #BDBDBD; padding: 6px 10px; text-align: left; vertical-align: top; }\n");
        sb.Append("th { background: #EEEEEE; }\n");
        sb.Append("td { word-wrap: break-word; max-width: 480px; }\n");
        sb.Append("</style>\n</head>\n<body>\n");
        sb.Append("<h1>").Append(Encode(title)).Append("</h1>\n");
        sb.Append("<p class=\"meta\">").Append(Encode(when)).Append(" | ")
            .Append(Encode(scopeLabel)).Append(" | ")
            .Append(Encode(rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Append("</p>\n");
        sb.Append("<table>\n<thead>\n<tr>");
        foreach (var header in headers)
        {
            sb.Append("<th>").Append(Encode(header)).Append("</th>");
        }

        sb.Append("</tr>\n</thead>\n<tbody>\n");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var column in schema.Columns)
            {
                sb.Append("<td>").Append(Encode(ReportValueFormatter.ToDisplayString(column.GetValue(row)))).Append("</td>");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n</body>\n</html>\n");
        return sb.ToString();
    }

    // Minimal markup escaping: neutralizes &<>"' for inert text rendering
    // while keeping Slovak diacritics raw (the file itself is UTF-8).
    private static string Encode(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&#39;");
}
