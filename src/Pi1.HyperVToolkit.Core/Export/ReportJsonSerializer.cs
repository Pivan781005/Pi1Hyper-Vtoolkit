using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pi1.HyperVToolkit.Core.Localization;

namespace Pi1.HyperVToolkit.Core.Export;

// Machine-readable JSON. Only schema columns are emitted (never the whole
// CLR row). Numbers stay numeric, booleans stay boolean, null stays null;
// everything else is a string. Property order follows the schema.
// Unicode is preserved raw (UnsafeRelaxedJsonEscaping); <script> content
// stays inert JSON string data.
public static class ReportJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(
        ReportSchema schema,
        IReadOnlyList<object> rows,
        string scopeLabel,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        var root = new JsonObject
        {
            ["report"] = schema.Name,
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            ["scope"] = scopeLabel,
            ["rowCount"] = rows.Count,
            ["rows"] = new JsonArray(rows.Select(row =>
            {
                var node = new JsonObject();
                foreach (var column in schema.Columns)
                {
                    node[column.Id] = ToNode(column, row);
                }

                return (JsonNode)node;
            }).ToArray()),
        };
        return root.ToJsonString(Options);
    }

    private static JsonNode? ToNode(ReportColumn column, object row)
    {
        var value = column.GetValue(row);
        if (value is null)
        {
            return null;
        }

        if (column.Kind == ReportColumnKind.Boolean && value is bool flag)
        {
            return JsonValue.Create(flag);
        }

        if (column.Kind == ReportColumnKind.Number)
        {
            if (value is int i)
            {
                return JsonValue.Create(i);
            }

            if (value is long l)
            {
                return JsonValue.Create(l);
            }

            if (value is double d)
            {
                return JsonValue.Create(d);
            }

            if (value is float f)
            {
                return JsonValue.Create((double)f);
            }
        }

        return JsonValue.Create(ReportValueFormatter.ToDisplayString(value));
    }
}
