using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Tests;

public sealed class SystemInfoMapperTests
{
    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MapHardware_RamMath_MatchesPowerShell()
    {
        // 16 GB total, 8 GB free (kilobytes, like Win32_OperatingSystem).
        var row = SystemInfoMapper.MapHardware(
            "NODE1",
            Bag(("Manufacturer", "Dell"), ("Model", "R740")),
            Bag(
                ("Caption", "Microsoft Windows Server 2022 Standard"),
                ("Version", "10.0.20348"),
                ("LastBootUpTime", new DateTime(2026, 9, 10, 12, 0, 0)),
                ("TotalVisibleMemorySize", (ulong)16_777_216),
                ("FreePhysicalMemory", (ulong)8_388_608)),
            [Bag(("Name", "Intel Xeon"), ("NumberOfCores", (uint)8), ("NumberOfLogicalProcessors", (uint)16))],
            new DateTime(2026, 9, 14, 12, 0, 0));

        Assert.Equal(16.0, row.RAMGB);
        Assert.Equal(8.0, row.FreeRAMGB);
        Assert.Equal(8.0, row.UsedRAMGB);
        Assert.Equal(50.0, row.RAMUsedPct);
        Assert.Equal(1, row.Sockets);
        Assert.Equal(8, row.Cores);
        Assert.Equal(16, row.LogicalCPU);
        Assert.Equal("Intel Xeon", row.CPUName);
        Assert.Equal("4d 0h 0m", row.UptimeText);
    }

    [Fact]
    public void MapHardware_FallsBackToComputerSystemMemory()
    {
        var row = SystemInfoMapper.MapHardware(
            "NODE1",
            Bag(("Manufacturer", "Dell"), ("Model", "R740"), ("TotalPhysicalMemory", (ulong)17_179_869_184)),
            null, [], DateTime.Now);
        Assert.Equal(16.0, row.RAMGB);
        Assert.Null(row.FreeRAMGB);
        Assert.Equal("-", row.UptimeText);
        Assert.Equal(0, row.Sockets);
        Assert.Null(row.Cores);
    }

    [Fact]
    public void MapHardware_CpuNames_DistinctJoined()
    {
        var row = SystemInfoMapper.MapHardware(
            "NODE1", null, null,
            [
                Bag(("Name", "Intel Xeon"), ("NumberOfCores", (uint)8), ("NumberOfLogicalProcessors", (uint)8)),
                Bag(("Name", "Intel Xeon"), ("NumberOfCores", (uint)8), ("NumberOfLogicalProcessors", (uint)8)),
            ],
            DateTime.Now);
        Assert.Equal("Intel Xeon", row.CPUName);
        Assert.Equal(2, row.Sockets);
        Assert.Equal(16, row.Cores);
    }

    [Fact]
    public void MapVolumes_ComputesGigabytesAndPercent()
    {
        var rows = SystemInfoMapper.MapVolumes("NODE1",
        [
            Bag(
                ("DeviceID", "C:"),
                ("VolumeName", "System"),
                ("FileSystem", "NTFS"),
                ("Size", (ulong)107_374_182_400),
                ("FreeSpace", (ulong)26_843_545_600)),
        ]);
        var row = Assert.Single(rows);
        Assert.Equal(100.0, row.SizeGB);
        Assert.Equal(25.0, row.FreeGB);
        Assert.Equal(25.0, row.FreePercent);
    }

    [Fact]
    public void MapVolumes_ZeroSize_YieldsZeroPercent()
    {
        var rows = SystemInfoMapper.MapVolumes("NODE1",
            [Bag(("DeviceID", "X:"), ("VolumeName", ""), ("FileSystem", ""), ("Size", (ulong)0), ("FreeSpace", (ulong)0))]);
        Assert.Equal(0, Assert.Single(rows).FreePercent);
    }

    [Fact]
    public void MapAdapters_FiltersIPv4AndJoins()
    {
        var rows = SystemInfoMapper.MapAdapters("NODE1",
        [
            Bag(
                ("Description", "Intel NIC"),
                ("MACAddress", "AA:BB:CC:DD:EE:FF"),
                ("IPAddress", new string[] { "192.168.1.5", "fe80::2" }),
                ("DefaultIPGateway", new string[] { "192.168.1.1" }),
                ("DNSServerSearchOrder", new string[] { "8.8.8.8", "8.8.4.4" }),
                ("DHCPEnabled", true)),
        ]);
        var row = Assert.Single(rows);
        Assert.Equal("192.168.1.5", row.IPv4);
        Assert.Equal("192.168.1.1", row.Gateway);
        Assert.Equal("8.8.8.8, 8.8.4.4", row.DNS);
        Assert.True(row.DHCP);
    }
}
