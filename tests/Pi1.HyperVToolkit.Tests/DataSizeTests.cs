using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

public sealed class DataSizeTests
{
    [Fact]
    public void Zero_FormatsAsMb() => Assert.Equal("0.00 MB", DataSize.FormatBytes(0L));

    [Fact]
    public void Null_FormatsAsDash()
    {
        Assert.Equal("-", DataSize.FormatBytes((long?)null));
        Assert.Equal("-", DataSize.FormatBytes((double?)null));
    }

    [Fact]
    public void Megabytes_WithDecimals() =>
        Assert.Equal("63.75 MB", DataSize.FormatBytes(63.75 * 1024 * 1024));

    [Fact]
    public void Boundary1024Mb_BecomesOneGb() =>
        Assert.Equal("1.00 GB", DataSize.FormatBytes(1024L * 1024 * 1024));

    [Fact]
    public void Gigabytes_WithDecimals() =>
        Assert.Equal("31.90 GB", DataSize.FormatBytes(31.9 * 1024 * 1024 * 1024));

    [Fact]
    public void FifteenHundredMb_IsOneAndHalfGb() =>
        Assert.Equal("1.50 GB", DataSize.FormatBytes(1536L * 1024 * 1024));

    [Fact]
    public void Boundary1024Gb_BecomesOneTb() =>
        Assert.Equal("1.00 TB", DataSize.FormatBytes(1024L * 1024 * 1024 * 1024));

    [Fact]
    public void Terabytes_WithDecimals() =>
        Assert.Equal("2.47 TB", DataSize.FormatBytes(2.47 * 1024 * 1024 * 1024 * 1024));

    [Fact]
    public void OneAndQuarterTb() =>
        Assert.Equal("1.25 TB", DataSize.FormatBytes(1.25 * 1024 * 1024 * 1024 * 1024));

    [Fact]
    public void NegativeMemoryDifference_KeepsSign() =>
        Assert.Equal("-512.00 MB", DataSize.FormatBytes(-512L * 1024 * 1024));

    [Fact]
    public void UlongOverload_Works() =>
        Assert.Equal("2.00 GB", DataSize.FormatBytes((ulong)(2L * 1024 * 1024 * 1024)));

    [Fact]
    public void SmallFile_RoundsToHundredthsMb() =>
        Assert.Equal("0.08 MB", DataSize.FormatBytes(81920L));
}
