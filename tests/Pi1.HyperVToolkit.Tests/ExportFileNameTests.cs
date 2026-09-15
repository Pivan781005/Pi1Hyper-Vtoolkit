using Pi1.HyperVToolkit.Core.Export;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ExportFileNameTests
{
    // Frozen clock from the spec: 2026-09-15 12:34:56.
    private static readonly DateTimeOffset Frozen =
        new(2026, 9, 15, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public void FrozenTimestamp_ProducesLegacyFileName()
    {
        Assert.Equal(
            "Advisor_20260915_123456.csv",
            ExportFileName.BuildDefaultFileName("Advisor", "csv", Frozen));
    }

    [Theory]
    [InlineData("Advisor", "Advisor")]
    [InlineData("CSVLowFree", "CSVLowFree")]
    [InlineData("FullVMReport", "FullVMReport")]
    [InlineData("Cluster Nodes", "Cluster_Nodes")]
    [InlineData("A/B\\C:D", "A_B_C_D")]
    [InlineData("---", "---")]
    [InlineData("!!!", "___")] // legacy parity: non-blank result is kept as-is
    [InlineData("   ", "___")] // spaces are replaced too; the result is not blank
    public void Sanitizer_ReplacesNonWordChars(string input, string expected)
    {
        Assert.Equal(expected, ExportFileName.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitizer_FallsBackToPi1Report(string? input)
    {
        Assert.Equal("Pi1_Report", ExportFileName.Sanitize(input));
    }

    [Fact]
    public void ExtensionLeadingDot_IsTrimmed()
    {
        Assert.Equal(
            "Advisor_20260915_123456.json",
            ExportFileName.BuildDefaultFileName("Advisor", ".json", Frozen));
    }

    [Fact]
    public void DefaultPath_UsesDesktopFolder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Advisor_20260915_123456.csv");
        Assert.Equal(expected, ExportFileName.BuildDefaultPath("Advisor", "csv", Frozen));
    }
}
