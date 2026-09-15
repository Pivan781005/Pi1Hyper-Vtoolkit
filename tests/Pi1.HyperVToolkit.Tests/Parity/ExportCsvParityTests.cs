using System.Text.Json;
using Pi1.HyperVToolkit.Core.Export;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

// Legacy CSV semantics probe: the SAME synthetic string values through the
// real Export-Csv -NoTypeInformation -Encoding UTF8 vs ReportCsvSerializer.
// Proves delimiter, all-fields-quoted style, embedded quotes/commas/CRLF,
// diacritics and empty-field behavior match.
//
// Numeric note (verified by design, not probed): Export-Csv renders doubles
// with the CURRENT PowerShell culture (12.5 vs 12,5), while C# deliberately
// uses InvariantCulture so reports stay unambiguous on any machine locale.
// (The broken Export-PiLastResultCsv cross-module LastResult bug is
// intentionally NOT reproduced.)
public sealed class ExportCsvParityTests : ParityTestBase
{
    private static readonly TimeSpan ReferenceTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Modules = ["Pi1.Core.psm1"];

    public ExportCsvParityTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task ExportCsv_QuotingMatchesReference()
    {
        var probe = Path.Combine(Path.GetTempPath(), $"Pi1ExportProbe_{Guid.NewGuid():N}.csv");
        try
        {
            var snippet = "$rows = @(" +
                "[PSCustomObject]@{ Area='hello'; Status='OK'; Value='Piešťany' }, " +
                "[PSCustomObject]@{ Area='q'; Status='OK'; Value='a\"b,c' }, " +
                "[PSCustomObject]@{ Area='multi'; Status='OK'; Value=\"line1`r`nline2\" }, " +
                "[PSCustomObject]@{ Area='path'; Status='OK'; Value='C:\\ClusterStorage\\Volume1' }, " +
                "[PSCustomObject]@{ Area='empty'; Status='OK'; Value='' }); " +
                $"@($rows) | Export-Csv -Path '{probe}' -NoTypeInformation -Encoding UTF8 -ErrorAction Stop; " +
                "Get-Content -LiteralPath '" + probe + "' -Raw -Encoding UTF8";
            var reference = await PowerShellReference.CollectSnippetAsync(snippet, Modules, ReferenceTimeout);
            if (!reference.IsAvailable)
            {
                ReportNotTestable(Output, reference.Reason);
                return;
            }

            // The harness JSON-encodes snippet output. Note: Get-Content -Raw
            // returns a string WITH ETS note properties (PSPath/PSDrive…), so
            // ConvertTo-Json yields an object and the CSV payload is .value.
            using var payload = JsonDocument.Parse(reference.Json);
            var psCsv = payload.RootElement.GetProperty("value").GetString();
            Assert.False(string.IsNullOrEmpty(psCsv), "Reference probe returned no CSV payload.");

            Assert.True(ReportSchemas.TryGet("Dashboard", out var schema));
            var ours = ReportCsvSerializer.Serialize(schema, new List<object>
            {
                new Core.Models.DashboardRow("hello", "OK", "Piešťany"),
                new Core.Models.DashboardRow("q", "OK", "a\"b,c"),
                new Core.Models.DashboardRow("multi", "OK", "line1\r\nline2"),
                new Core.Models.DashboardRow("path", "OK", "C:\\ClusterStorage\\Volume1"),
                new Core.Models.DashboardRow("empty", "OK", string.Empty),
            });

            Output.WriteLine("PS : " + psCsv.Replace("\r\n", "\\r\\n"));
            Output.WriteLine("C# : " + ours.Replace("\r\n", "\\r\\n"));
            Assert.Equal(
                psCsv.Split(["\r\n"], StringSplitOptions.None),
                ours.Split(["\r\n"], StringSplitOptions.None));
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception)
            {
                // Best-effort temp cleanup only.
            }
        }
    }
}
