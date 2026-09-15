using System.Text.Json;
using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

// One schema drives all three formats: CSV (legacy Export-Csv parity),
// HTML (localized human-readable) and JSON (typed machine-readable).
[Collection("LanguageSerial")]
public sealed class ReportSerializerTests
{
    private static ReportSchema AdvisorSchema()
    {
        Assert.True(ReportSchemas.TryGet("Advisor", out var schema));
        return schema;
    }

    private static IReadOnlyList<object> AdvisorRows() =>
    [
        new AdvisorRow("Warning", "N1", "Piešťany", 12.5, 3.0, 9.5, AdvisorRule.Reserve, false),
        new AdvisorRow("Info", "N1", "a\"b,c", 20.0, 5.0, 15.0, AdvisorRule.LargeReserve, false),
    ];

    [Fact]
    public void Csv_ExactBytes_QuotingOrderDiacritics()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var csv = ReportCsvSerializer.Serialize(AdvisorSchema(), AdvisorRows());
            var expected =
                "\"Severity\",\"HostNode\",\"VM\",\"AssignedGB\",\"DemandGB\",\"WasteGB\",\"Recommendation\"\r\n" +
                "\"Warning\",\"N1\",\"Piešťany\",\"12.5\",\"3\",\"9.5\",\"RAM rezerva > 8 GB. Sledovať.\"\r\n" +
                "\"Info\",\"N1\",\"a\"\"b,c\",\"20\",\"5\",\"15\",\"Veľká RAM rezerva. Kandidát na zníženie po sledovaní.\"\r\n";
            Assert.Equal(expected, csv);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Csv_SkSkCurrentCulture_DoesNotAlterCsvFormat()
    {
        // Guard against locale coupling: even on Slovak Windows (semicolon
        // list separator, comma decimals) the export stays standard comma CSV
        // with invariant decimals. Excel users import/split it explicitly.
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("sk-SK");
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var csv = ReportCsvSerializer.Serialize(AdvisorSchema(), AdvisorRows());
            var expected =
                "\"Severity\",\"HostNode\",\"VM\",\"AssignedGB\",\"DemandGB\",\"WasteGB\",\"Recommendation\"\r\n" +
                "\"Warning\",\"N1\",\"Piešťany\",\"12.5\",\"3\",\"9.5\",\"RAM rezerva > 8 GB. Sledovať.\"\r\n" +
                "\"Info\",\"N1\",\"a\"\"b,c\",\"20\",\"5\",\"15\",\"Veľká RAM rezerva. Kandidát na zníženie po sledovaní.\"\r\n";
            Assert.Equal(expected, csv);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Csv_PreservesRowOrder_CrLfAndNull()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.True(ReportSchemas.TryGet("Dashboard", out var schema));
            var rows = new List<object>
            {
                new DashboardRow("A", "OK", "line1\r\nline2"),
                new DashboardRow("B", "OK", string.Empty),
                new DashboardRow("C", "OK", "C:\\ClusterStorage\\Volume1"),
            };
            var csv = ReportCsvSerializer.Serialize(schema, rows);
            Assert.Contains("\"line1\r\nline2\"", csv, StringComparison.Ordinal);
            Assert.True(csv.IndexOf("\"A\"", StringComparison.Ordinal) < csv.IndexOf("\"B\"", StringComparison.Ordinal));
            Assert.True(csv.IndexOf("\"B\"", StringComparison.Ordinal) < csv.IndexOf("\"C\"", StringComparison.Ordinal));
            Assert.DoesNotContain("Rule", csv, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Csv_NumbersAreLocaleIndependent()
    {
        // InvariantCulture: 12.5 never becomes "12,5" on an SK-locale machine.
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            var csv = ReportCsvSerializer.Serialize(AdvisorSchema(), AdvisorRows());
            Assert.Contains("\"12.5\"", csv, StringComparison.Ordinal);
            Assert.DoesNotContain("12,5", csv, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Html_DocumentStructure_MetaAndLocalizedHeaders()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var when = new DateTimeOffset(2026, 9, 15, 12, 34, 56, TimeSpan.Zero);
            var html = ReportHtmlSerializer.Serialize(
                AdvisorSchema(), AdvisorRows(), "Lokálny uzol (N1)", when, AppLanguage.Slovak);
            Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
            Assert.Contains("<html lang=\"sk\">", html, StringComparison.Ordinal);
            Assert.Contains("Advisor", html, StringComparison.Ordinal);
            Assert.Contains("2026-09-15 12:34:56", html, StringComparison.Ordinal);
            Assert.Contains("Lokálny uzol (N1)", html, StringComparison.Ordinal);
            Assert.Contains("<th>Závažnosť</th>", html, StringComparison.Ordinal);
            Assert.Contains("<th>Odporúčanie</th>", html, StringComparison.Ordinal);
            Assert.True(
                html.IndexOf("<th>Závažnosť</th>", StringComparison.Ordinal) <
                html.IndexOf("<th>Odporúčanie</th>", StringComparison.Ordinal));
            Assert.Contains("Piešťany", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Rule", html, StringComparison.Ordinal);
            Assert.DoesNotContain("IsSensitive", html, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Html_EscapesHostileValues_NoExternalResources()
    {
        var rows = new List<object>
        {
            new DashboardRow("X", "OK", "<script>alert('x')</script>"),
        };
        Assert.True(ReportSchemas.TryGet("Dashboard", out var schema));
        var html = ReportHtmlSerializer.Serialize(
            schema, rows, "scope", new DateTimeOffset(2026, 9, 15, 12, 34, 56, TimeSpan.Zero), AppLanguage.English);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<th>Status</th>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_MetadataTypesOrderAndUnicode()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            var when = new DateTimeOffset(2026, 9, 15, 12, 34, 56, TimeSpan.FromHours(2));
            var json = ReportJsonSerializer.Serialize(AdvisorSchema(), AdvisorRows(), "scope", when);
            Assert.Contains("Piešťany", json, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal("Advisor", root.GetProperty("report").GetString());
            Assert.Equal("2026-09-15T10:34:56.0000000Z", root.GetProperty("generatedAt").GetString());
            Assert.Equal("scope", root.GetProperty("scope").GetString());
            Assert.Equal(2, root.GetProperty("rowCount").GetInt32());
            var first = root.GetProperty("rows").EnumerateArray().First();
            Assert.Equal(12.5, first.GetProperty("AssignedGB").GetDouble());
            Assert.Equal("Piešťany", first.GetProperty("VM").GetString());
            // Schema order preserved in the raw payload.
            Assert.True(
                json.IndexOf("\"Severity\"", StringComparison.Ordinal) <
                json.IndexOf("\"Recommendation\"", StringComparison.Ordinal));
            Assert.False(TryGetPropertyDeep(root, "Rule"), "JSON leaks Advisor.Rule.");
            Assert.False(TryGetPropertyDeep(root, "IsSensitive"), "JSON leaks Advisor.IsSensitive.");
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Json_PreservesBooleansAndNulls()
    {
        Assert.True(ReportSchemas.TryGet(ReportSchemas.FullVMReportName, out var schema));
        var rows = new List<object>
        {
            new VirtualMachineRow("N1", "APP1", "Running", 2, 4.0, 3.0, 1.0,
                "10.0.0.1", "AA", "SW", null, true, 4.0, 1.0, 8.0),
        };
        var json = ReportJsonSerializer.Serialize(
            schema, rows, "scope", new DateTimeOffset(2026, 9, 15, 12, 34, 56, TimeSpan.Zero));
        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("rows").EnumerateArray().First();
        Assert.Equal(JsonValueKind.True, first.GetProperty("Dynamic").ValueKind);
        Assert.Equal(2, first.GetProperty("CPU").GetInt32());
        Assert.Equal(4.0, first.GetProperty("AssignedGB").GetDouble());
        Assert.False(TryGetPropertyDeep(document.RootElement, "AssignedBytes"), "JSON leaks raw byte fields.");
        Assert.False(TryGetPropertyDeep(document.RootElement, "DemandBytes"), "JSON leaks raw byte fields.");
        Assert.False(TryGetPropertyDeep(document.RootElement, "UptimeText"), "JSON must use the canonical Uptime field.");
        Assert.Equal("-", first.GetProperty("Uptime").GetString());

        Assert.True(ReportSchemas.TryGet("VMStorageMap", out var storage));
        var emptyDisk = new List<object>
        {
            new VmStorageRow("N1", "APP1", "Running", "SCSI 0:0", "Unknown", "Unknown",
                null, null, string.Empty, null, null, "C:\\disk.vhdx"),
        };
        var storageJson = ReportJsonSerializer.Serialize(
            storage, emptyDisk, "scope", new DateTimeOffset(2026, 9, 15, 12, 34, 56, TimeSpan.Zero));
        using var storageDocument = JsonDocument.Parse(storageJson);
        var disk = storageDocument.RootElement.GetProperty("rows").EnumerateArray().First();
        Assert.Equal(JsonValueKind.Null, disk.GetProperty("VHDSizeGB").ValueKind);
    }

    private static bool TryGetPropertyDeep(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }

                if (TryGetPropertyDeep(property.Value, name))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetPropertyDeep(item, name))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
