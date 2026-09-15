using Pi1.HyperVToolkit.Tests.Parity;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ParityNumbersTests
{
    [Theory]
    [InlineData("8", "8.0")]
    [InlineData("8.0", "8")]
    [InlineData("0", "0.0")]
    [InlineData("31.9", "31.90")]
    [InlineData("", "")]
    [InlineData("Running", "Running")]
    public void Equals_EquivalentValues(string expected, string actual)
    {
        Assert.True(ParityNumbers.Equals(expected, actual));
    }

    [Theory]
    [InlineData("8", "9")]
    [InlineData("8.0", "8.1")]
    [InlineData("Running", "Off")]
    [InlineData("00155D03B500", "00155D03B501")]
    [InlineData("", "8")]
    public void Equals_DifferentValues(string expected, string actual)
    {
        Assert.False(ParityNumbers.Equals(expected, actual));
    }

    // Live-counter drift policy (NodeHardware/HostSettings parity): volatile
    // memory counters tolerate small sampling gaps; static fields never do.
    [Theory]
    [InlineData("19.8", "19.7", 0.5)] // UsedRAMGB live sample.
    [InlineData("12.0", "12.1", 0.5)] // FreeRAMGB live sample.
    [InlineData("62.1", "61.9", 1.0)] // RAMUsedPct live sample.
    public void Within_VolatileDrift_Accepted(string expected, string actual, double tolerance)
    {
        Assert.True(ParityNumbers.Within(expected, actual, tolerance));
    }

    [Theory]
    [InlineData("19.8", "19.0", 0.5)] // outside Used/FreeRAMGB tolerance.
    [InlineData("62.1", "60.0", 1.0)] // outside RAMUsedPct tolerance.
    [InlineData("31.9", "32.9", 0.5)] // RAMGB-class drift is NOT tolerated.
    public void Within_OutsideTolerance_Rejected(string expected, string actual, double tolerance)
    {
        Assert.False(ParityNumbers.Within(expected, actual, tolerance));
    }

    [Theory]
    [InlineData("32.0", "31.9")] // static RAMGB mismatch => FAIL.
    [InlineData("8", "7")] // CPU/core mismatch => FAIL.
    public void Equals_StaticMismatch_Rejected(string expected, string actual)
    {
        Assert.False(ParityNumbers.Equals(expected, actual));
    }
}
