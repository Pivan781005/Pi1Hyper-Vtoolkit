namespace Pi1.HyperVToolkit.Core.Export;

// CSV mirrors Export-Csv -NoTypeInformation: comma delimiter, CRLF line
// endings, EVERY field double-quoted with embedded quotes doubled, UTF-8
// (the writer emits the BOM; see ExportEncodings). Headers are the stable
// canonical column Ids (never localized), values are invariant display text.
// The comma is INTENTIONAL and locale-independent: a standard CSV stays valid
// even where Slovak Excel does not auto-split it on double-click (Excel's CSV
// import / Text to Columns handles it). No "sep=" hack, no culture coupling.
public static class ReportCsvSerializer
{
    public const string NewLine = "\r\n";

    public static string Serialize(ReportSchema schema, IReadOnlyList<object> rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        var lines = new List<string>(rows.Count + 1)
        {
            Join(schema.Columns.Select(c => Quote(c.Id))),
        };
        foreach (var row in rows)
        {
            lines.Add(Join(schema.Columns.Select(c => Quote(ReportValueFormatter.ToDisplayString(c.GetValue(row))))));
        }

        return string.Join(NewLine, lines) + NewLine;
    }

    private static string Join(IEnumerable<string> fields) => string.Join(",", fields);

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
