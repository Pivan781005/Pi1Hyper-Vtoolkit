using System.Text.Json;
using Pi1.HyperVToolkit.Tests.Parity;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ParityComparisonTests
{
    private static string Normalize(string json, bool numeric = false)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.EnumerateObject().First().Value;
        return ParityRow.NormalizeValue(element, numeric);
    }

    [Fact]
    public void Normalize_EmptyArray_IsEmpty() =>
        Assert.Equal(string.Empty, Normalize(@"{""A"":[]}"));

    [Fact]
    public void Normalize_NumericArray_Joined() =>
        Assert.Equal("10, 20, 30", Normalize(@"{""A"":[10,20,30]}"));

    [Fact]
    public void Normalize_StringArray_Joined() =>
        Assert.Equal("10, 20, 30", Normalize(@"{""A"":[""10"",""20"",""30""]}"));

    [Fact]
    public void Normalize_ScalarNumber_Raw() =>
        Assert.Equal("42", Normalize(@"{""A"":42}"));

    [Fact]
    public void Normalize_ScalarNumber_OneDecimalWhenNumeric() =>
        Assert.Equal("42.0", Normalize(@"{""A"":42}", numeric: true));

    [Fact]
    public void Normalize_ScalarString_Verbatim() =>
        Assert.Equal("Default Switch", Normalize(@"{""A"":""Default Switch""}"));

    [Fact]
    public void Normalize_Null_IsEmpty() =>
        Assert.Equal(string.Empty, Normalize(@"{""A"":null}"));

    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    public void Normalize_Boolean_PowerShellSpelling(bool raw, string expected) =>
        Assert.Equal(expected, Normalize($"{{\"A\":{raw.ToString().ToLowerInvariant()}}}"));

    [Fact]
    public void ParseRows_VlanLikeUntaggedRow_NoCrash()
    {
        // Live Get-VMNetworkAdapterVlan shape that crashed the old parser:
        // AllowedVlanIdList arrives as an empty JSON array.
        var rows = ParityRow.ParseRows(
            """[{"HostNode":"N","VMName":"","VMNetworkAdapterName":"","OperationMode":"Untagged","AccessVlanId":0,"NativeVlanId":0,"AllowedVlanIdList":[]}]""",
            ["AccessVlanId", "NativeVlanId"]);
        var row = Assert.Single(rows);
        Assert.Equal("Untagged", row["OperationMode"]);
        Assert.Equal(string.Empty, row["AllowedVlanIdList"]);
        Assert.Equal("0.0", row["AccessVlanId"]);
    }

    [Fact]
    public void StableVolumeKey_PrefersUniqueId()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DriveLetter"] = string.Empty,
            ["FileSystemLabel"] = string.Empty,
            ["UniqueId"] = @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\",
            ["Path"] = @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\",
        };
        Assert.StartsWith("UID:", StorageParityTests.StableVolumeKey(row));
    }

    [Fact]
    public void StableVolumeKey_DuplicateDisplayKeys_Distinguished()
    {
        // Regression fixture: TWO volumes, both DriveLetter="" and
        // FileSystemLabel="", different identity and filesystem.
        var ntfs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DriveLetter"] = string.Empty, ["FileSystemLabel"] = string.Empty,
            ["UniqueId"] = @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\",
            ["Path"] = @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\",
            ["FileSystem"] = "NTFS",
        };
        var fat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DriveLetter"] = string.Empty, ["FileSystemLabel"] = string.Empty,
            ["UniqueId"] = @"\\?\Volume{9d1f9fc1-4e06-4a68-847a-0455fe7a395b}\",
            ["Path"] = @"\\?\Volume{9d1f9fc1-4e06-4a68-847a-0455fe7a395b}\",
            ["FileSystem"] = "FAT32",
        };
        Assert.NotEqual(
            StorageParityTests.StableVolumeKey(ntfs),
            StorageParityTests.StableVolumeKey(fat));
    }

    [Theory]
    [InlineData("9h 53m", "9h 53m")] // 1. identical => PASS
    [InlineData("9h 53m", "9h 54m")] // 2. +1 minute => PASS
    [InlineData("9h 54m", "9h 53m")] // 3. -1 minute => PASS
    [InlineData("59m", "1h 0m")] // 4. crossing 59m -> 1h 0m => PASS
    [InlineData("23h 59m", "1d 0h 0m")] // 5. crossing into days => PASS
    [InlineData("2d 4h 10m", "2d 4h 11m")]
    [InlineData("0m", "0m")]
    [InlineData("-", "-")] // unparseable but identical => exact PASS
    public void UptimeTolerance_Accepted(string expected, string actual)
    {
        Assert.True(Parity.ParityUptime.WithinTolerance(expected, actual, toleranceMinutes: 1));
    }

    [Theory]
    [InlineData("9h 53m", "9h 55m")] // 6. 2-minute difference => FAIL
    [InlineData("1h 0m", "1h 2m")]
    [InlineData("59m", "1h 1m")]
    [InlineData("garbage", "9h 53m")] // 7. malformed unequal => FAIL
    [InlineData("9h 53m", "garbage")]
    [InlineData("-", "0m")]
    [InlineData("", "0m")]
    public void UptimeTolerance_Rejected(string expected, string actual)
    {
        Assert.False(Parity.ParityUptime.WithinTolerance(expected, actual, toleranceMinutes: 1));
    }

    [Theory]
    [InlineData("9h 53m", 593)]
    [InlineData("2d 4h 10m", 3130)]
    [InlineData("59m", 59)]
    [InlineData("0m", 0)]
    public void UptimeParser_Minutes(string text, int minutes)
    {
        Assert.Equal(minutes, Parity.ParityUptime.TryParseMinutes(text));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("9x 53m")]
    public void UptimeParser_Malformed_ReturnsNull(string text)
    {
        Assert.Null(Parity.ParityUptime.TryParseMinutes(text));
    }
}
