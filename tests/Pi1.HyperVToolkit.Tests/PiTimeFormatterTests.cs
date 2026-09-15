using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Tests;

public sealed class PiTimeFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsDash()
    {
        Assert.Equal("-", PiTimeFormatter.Format(null));
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(0.5, "0m")]
    [InlineData(-5, "0m")]
    public void Format_SubSecondOrNegative_ReturnsZeroMinutes(double seconds, string expected)
    {
        Assert.Equal(expected, PiTimeFormatter.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    // Parity with PowerShell: "{0}m" -f [int]$Time.Minutes (truncation, TotalHours < 1)
    [InlineData(1, "1m")]
    [InlineData(59, "59m")]
    [InlineData(59.9, "59m")]
    public void Format_Minutes(double minutes, string expected)
    {
        Assert.Equal(expected, PiTimeFormatter.Format(TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(1, 0, "1h 0m")]
    [InlineData(2, 30, "2h 30m")]
    [InlineData(25, 15, "1d 1h 15m")] // TotalDays >= 1 takes the day branch
    public void Format_HoursAndDays(int hours, int minutes, string expected)
    {
        var time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        Assert.Equal(expected, PiTimeFormatter.Format(time));
    }

    [Fact]
    public void Format_MultiDay()
    {
        var time = new TimeSpan(days: 3, hours: 4, minutes: 5, seconds: 6);
        Assert.Equal("3d 4h 5m", PiTimeFormatter.Format(time));
    }
}
