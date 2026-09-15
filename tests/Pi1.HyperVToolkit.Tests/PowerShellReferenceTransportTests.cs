using Pi1.HyperVToolkit.Tests.Parity;

namespace Pi1.HyperVToolkit.Tests;

// Regression tests for the SHARED PowerShellReference transport
// (CollectSnippetAsync). They prove a multiline snippet containing a
// function, double-quoted empty strings, single-quoted strings,
// PSCustomObject construction, nested property access and multiple
// statements survives the runner and returns JSON — and that a malformed
// generated script fails LOUDLY instead of false-green NOT TESTABLE.
public sealed class PowerShellReferenceTransportTests
{
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task MultilineSnippetWithFunction_ReturnsJson()
    {
        const string snippet = """
            function Get-TestIdentity {
                param($InputObject)
                $name = ""
                $props = $InputObject.PSObject.Properties
                $p = $props['Name']
                if ($p -and $p.Value) { $name = [string]$p.Value }
                $label = 'single-quoted'
                return [PSCustomObject]@{ Name = $name; Label = $label; Nested = $InputObject.Child.Value }
            }
            $objects = @(
                [PSCustomObject]@{ Name = "first"; Child = [PSCustomObject]@{ Value = 1 } },
                [PSCustomObject]@{ Name = "second"; Child = [PSCustomObject]@{ Value = 2 } }
            )
            $objects | ForEach-Object { Get-TestIdentity $_ }
            """;
        var result = await PowerShellReference.CollectSnippetAsync(snippet, [], RunnerTimeout);
        Assert.True(result.IsAvailable, $"Shared transport failed: {result.Reason}");
        var rows = ParityRow.ParseRows(result.Json, ["Nested"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0]["Name"]);
        Assert.Equal("single-quoted", rows[0]["Label"]);
        Assert.Equal("1.0", rows[0]["Nested"]);
        Assert.Equal("second", rows[1]["Name"]);
    }

    [Fact]
    public async Task VlanCanonicalizer_ThroughSharedTransport()
    {
        // The ACTUAL parity canonicalizer through the SAME shared transport:
        // fails if the runner ever falls back to fragile -Command quoting.
        var snippet = NetworkParityTests.VlanIdentityCanonicalizer + """
            $vlan = [PSCustomObject]@{
                OperationMode = 'Untagged'; AccessVlanId = 0; NativeVlanId = 0; AllowedVlanIdList = @()
                ParentAdapter = [PSCustomObject]@{ VMName = 'DOS 6.22'; Name = 'Network Adapter' }
            }
            $id = Get-PiVlanReferenceIdentity $vlan
            [PSCustomObject]@{ VMName = $id.VMName; VMNetworkAdapterName = $id.VMNetworkAdapterName; OperationMode = [string]$vlan.OperationMode }
            """;
        var result = await PowerShellReference.CollectSnippetAsync(snippet, [], RunnerTimeout);
        Assert.True(result.IsAvailable, $"Shared transport failed: {result.Reason}");
        var row = Assert.Single(ParityRow.ParseRows(result.Json, []));
        Assert.Equal("DOS 6.22", row["VMName"]);
        Assert.Equal("Network Adapter", row["VMNetworkAdapterName"]);
        Assert.Equal("Untagged", row["OperationMode"]);
    }

    [Fact]
    public async Task MalformedSnippet_FailsLoudly_NotNotTestable()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PowerShellReference.CollectSnippetAsync("$x = \"unterminated", [], RunnerTimeout));
    }

    [Theory]
    [InlineData("The string is missing the terminator: \". ParserError", true)]
    [InlineData("TerminatorExpectedAtEndOfString", true)]
    [InlineData("Unexpected token 'foo' in expression", true)]
    [InlineData("You do not have the required permission to complete this task.", false)]
    [InlineData("", false)]
    public void IsParserFailure_Classifies(string stderr, bool expected) =>
        Assert.Equal(expected, PowerShellReference.IsParserFailure(stderr));
}
