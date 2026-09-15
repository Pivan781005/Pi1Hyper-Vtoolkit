using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

public sealed class NetworkTextTests
{
    [Fact]
    public void FilterIPv4_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NetworkText.FilterIPv4(null));
    }

    [Fact]
    public void FilterIPv4_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NetworkText.FilterIPv4([]));
    }

    [Fact]
    public void FilterIPv4_KeepsOnlyV4AndJoins()
    {
        string?[] input = ["192.168.1.10", "fe80::1", "10.0.0.5", null, string.Empty];
        Assert.Equal("192.168.1.10, 10.0.0.5", NetworkText.FilterIPv4(input));
    }

    [Fact]
    public void FilterIPv4_NoV4_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NetworkText.FilterIPv4(["fe80::1", "::1"]));
    }

    [Theory]
    // Parity: $mac -replace "[-:\. ]", ""
    [InlineData("00:15:5D:01:02:03", "00155D010203")]
    [InlineData("00-15-5D-01-02-03", "00155D010203")]
    [InlineData("0015.5D01.0203", "00155D010203")]
    [InlineData("00 15 5D 01 02 03", "00155D010203")]
    [InlineData("00155D010203", "00155D010203")]
    public void NormalizeMac_StripsSeparators(string input, string expected)
    {
        Assert.Equal(expected, NetworkText.NormalizeMac(input));
    }

    [Fact]
    public void NormalizeMac_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NetworkText.NormalizeMac(null));
        Assert.Equal(string.Empty, NetworkText.NormalizeMac(string.Empty));
    }

    [Fact]
    public void NormalizeMac_PreservesCase()
    {
        // PowerShell -like is case-insensitive, so normalization must not change case;
        // filtering uses ordinal-ignore-case comparison instead.
        Assert.Equal("aaBBccDDeeFF", NetworkText.NormalizeMac("aa:BB:cc:DD:ee:FF"));
    }
}
