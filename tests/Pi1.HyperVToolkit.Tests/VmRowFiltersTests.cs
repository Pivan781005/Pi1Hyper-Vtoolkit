using Pi1.HyperVToolkit.Core.Filtering;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Tests;

public sealed class VmRowFiltersTests
{
    private static VirtualMachineRow Vm(string name, string state = "Running") =>
        new("NODE1", name, state, 2, 4.0, 3.0, 1.0, "192.168.1.1", "00155D010203", "LAN", null, false, 4.0, 1.0, 8.0);

    private static VmNetworkRow Nic(string vm, string ipv4, string mac, string allIps) =>
        new("NODE1", vm, ipv4, mac, "LAN", allIps);

    [Fact]
    public void ByName_IsCaseInsensitiveSubstring()
    {
        var rows = new[] { Vm("APP-SQL01"), Vm("web02"), Vm("APP-VEEAM") };
        Assert.Equal(2, VmRowFilters.ByName(rows, "app").Count);
        Assert.Single(VmRowFilters.ByName(rows, "sql01"));
        Assert.Equal(3, VmRowFilters.ByName(rows, "").Count);
        Assert.Equal(3, VmRowFilters.ByName(rows, null).Count);
    }

    [Fact]
    public void ByState_FiltersExactly()
    {
        var rows = new[] { Vm("A", "Running"), Vm("B", "Off"), Vm("C", "Paused") };
        Assert.Single(VmRowFilters.ByState(rows, "Running"));
        Assert.Single(VmRowFilters.ByState(rows, "Off"));
        Assert.Equal(3, VmRowFilters.ByState(rows, null).Count);
    }

    [Fact]
    public void ByIp_SearchesIPv4AndAllIps()
    {
        var rows = new[]
        {
            Nic("A", "192.168.1.10", "00155D010203", "192.168.1.10, fe80::1"),
            Nic("B", "", "00155D040506", "fe80::2"),
        };
        Assert.Single(VmRowFilters.ByIp(rows, "192.168.1.10"));
        Assert.Single(VmRowFilters.ByIp(rows, "fe80::2"));
        Assert.Equal(2, VmRowFilters.ByIp(rows, "fe80").Count);
        Assert.Empty(VmRowFilters.ByIp(rows, "10.99."));
    }

    [Theory]
    [InlineData("00-15-5D-AA-BB-CC")]
    [InlineData("00:15:5D:AA:BB:CC")]
    [InlineData("00155DAABBCC")]
    [InlineData("0015.5DAA.BBCC")]
    [InlineData("aabbcc")]
    public void ByMac_NormalizesAllFormats(string search)
    {
        var rows = new[] { Nic("A", "", "00155DAABBCC", "") };
        Assert.Single(VmRowFilters.ByMac(rows, search));
    }

    [Fact]
    public void WithoutIp_KeepsOnlyEmptyAllIps()
    {
        var rows = new[]
        {
            Nic("A", "192.168.1.1", "00155D010203", "192.168.1.1"),
            Nic("B", "", "00155D040506", ""),
            Nic("C", "", "00155D070809", "   "),
        };
        var result = VmRowFilters.WithoutIp(rows);
        Assert.Equal(2, result.Count);
    }
}
