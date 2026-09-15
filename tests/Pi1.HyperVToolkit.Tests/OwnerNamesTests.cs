using Pi1.HyperVToolkit.Core.Cluster;

namespace Pi1.HyperVToolkit.Tests;

public sealed class OwnerNamesTests
{
    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void PlainStrings_Kept() =>
        Assert.Equal(["N1", "N2"], OwnerNames.Normalize(["N1", "N2"]));

    [Fact]
    public void NullWhitespace_Skipped() =>
        Assert.Equal(["N1"], OwnerNames.Normalize([null, "  ", "", "N1"]));

    [Theory]
    [InlineData("Name", "N1")]
    [InlineData("NodeName", "N1")]
    [InlineData("OwnerNode", "N1")]
    [InlineData("ClusterNode", "N1")]
    [InlineData("Node", "N1")]
    public void CandidateProperties_EachContributes(string property, string expected) =>
        Assert.Equal([expected], OwnerNames.Normalize([Bag((property, expected))]));

    [Fact]
    public void NestedClusterObjectName_Used() =>
        Assert.Equal(["N2"], OwnerNames.Normalize(
            [Bag(("ClusterObject", Bag(("Name", "N2"))))]));

    [Fact]
    public void Duplicates_UniqueCaseInsensitive_FirstOrder()
    {
        // INTENTIONAL improvement over the reference (verified live:
        // PowerShell Select-Object -Unique is case-SENSITIVE, keeping both
        // N1 and n1). Node names are case-insensitive: one entry wins.
        var result = OwnerNames.Normalize(["N2", "n1", "N1", "n2", "N3"]);
        Assert.Equal(["N2", "n1", "N3"], result);
    }

    [Fact]
    public void TypeNameLeak_Excluded()
    {
        // INTENTIONAL improvement over the reference (verified live: a plain
        // "ClusterOwnerNodeList" string passes the reference string branch).
        // A type name is never a node name.
        Assert.Empty(OwnerNames.Normalize(["ClusterOwnerNodeList"]));
        Assert.Empty(OwnerNames.Normalize(["System.Management.ManagementObject collection"]));
    }

    [Fact]
    public void UnknownObject_SkippedNeverToString()
    {
        Assert.Empty(OwnerNames.Normalize([new object()]));
        Assert.Empty(OwnerNames.Normalize([42]));
    }

    [Fact]
    public void EmptyInput_Empty() =>
        Assert.Empty(OwnerNames.Normalize([]));

    [Fact]
    public void MixedInput_Structured()
    {
        var result = OwnerNames.Normalize(["N1", null, Bag(("OwnerNode", "N2")), 42, "ClusterOwnerNodeList"]);
        Assert.Equal(["N1", "N2"], result);
    }
}
