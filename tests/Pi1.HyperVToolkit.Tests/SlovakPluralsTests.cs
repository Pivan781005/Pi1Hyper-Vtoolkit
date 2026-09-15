using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

public sealed class SlovakPluralsTests
{
    [Theory]
    [InlineData(1, "1 záznam")]
    [InlineData(2, "2 záznamy")]
    [InlineData(3, "3 záznamy")]
    [InlineData(4, "4 záznamy")]
    [InlineData(0, "0 záznamov")]
    [InlineData(5, "5 záznamov")]
    [InlineData(11, "11 záznamov")]
    [InlineData(12, "12 záznamov")]
    [InlineData(14, "14 záznamov")]
    [InlineData(22, "22 záznamy")]
    [InlineData(25, "25 záznamov")]
    [InlineData(111, "111 záznamov")]
    public void Format_VmRecords(int count, string expected)
    {
        Assert.Equal(expected, SlovakPlurals.Format(count, "záznam", "záznamy", "záznamov"));
    }

    [Theory]
    [InlineData(1, "1 uzol")]
    [InlineData(2, "2 uzly")]
    [InlineData(5, "5 uzlov")]
    public void Format_Nodes(int count, string expected)
    {
        Assert.Equal(expected, SlovakPlurals.Format(count, "uzol", "uzly", "uzlov"));
    }
}
