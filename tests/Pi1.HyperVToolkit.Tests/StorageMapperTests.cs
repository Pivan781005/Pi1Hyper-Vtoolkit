using Pi1.HyperVToolkit.Infrastructure.Storage;

namespace Pi1.HyperVToolkit.Tests;

public sealed class StorageMapperTests
{
    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    // ---- Health / state tables (transcribed from Storage.types.ps1xml) ----

    [Theory]
    [InlineData(0, "Healthy")]
    [InlineData(1, "Warning")]
    [InlineData(2, "Unhealthy")]
    [InlineData(5, "Unknown")]
    [InlineData(99, "Unknown")]
    [InlineData(null, "Unknown")]
    public void HealthStatus_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapHealthStatus(raw));

    [Theory]
    [InlineData(0, "Healthy")]
    [InlineData(1, "Warning")]
    [InlineData(2, "Unhealthy")]
    [InlineData(5, "Unknown")]
    [InlineData(null, "Unknown")]
    public void VolumeHealth_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapVolumeHealth(raw));

    [Theory]
    [InlineData(2, "New")]
    [InlineData(3, "Starting")]
    [InlineData(4, "Running")]
    [InlineData(5, "Suspended")]
    [InlineData(6, "Shutting Down")]
    [InlineData(7, "Completed")]
    [InlineData(8, "Terminated")]
    [InlineData(9, "Killed")]
    [InlineData(10, "Exception")]
    [InlineData(11, "Service")]
    [InlineData(12, "Query Pending")]
    [InlineData(99, "Unknown")]
    [InlineData(null, "Unknown")]
    public void JobState_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapJobState(raw));

    [Theory]
    [InlineData(0, "Unknown")]
    [InlineData(1, "Thin")]
    [InlineData(2, "Fixed")]
    [InlineData(9, "Unknown")]
    public void Provisioning_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapProvisioningType(raw));

    [Fact]
    public void Provisioning_Missing_IsEmpty() =>
        Assert.Equal(string.Empty, StorageMapper.MapProvisioningType(null, missingAsEmpty: true));

    [Theory]
    [InlineData(0, "Unspecified")]
    [InlineData(3, "HDD")]
    [InlineData(4, "SSD")]
    [InlineData(5, "SCM")]
    [InlineData(null, "Unspecified")]
    [InlineData(42, "Unspecified")]
    public void MediaType_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapMediaType(raw));

    [Theory]
    [InlineData(8, "RAID")]
    [InlineData(10, "SAS")]
    [InlineData(11, "SATA")]
    [InlineData(17, "NVMe")]
    [InlineData(0, "Unknown")]
    [InlineData(42, "Unknown")]
    public void BusType_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapBusType(raw));

    [Fact]
    public void BusType_Missing_IsEmpty() =>
        Assert.Equal(string.Empty, StorageMapper.MapBusType(null));

    [Theory]
    [InlineData(1, "Auto-Select")]
    [InlineData(2, "Manual-Select")]
    [InlineData(3, "Hot Spare")]
    [InlineData(4, "Retired")]
    [InlineData(5, "Journal")]
    [InlineData(0, "Unknown")]
    public void Usage_Table(int? raw, string expected) =>
        Assert.Equal(expected, StorageMapper.MapPhysicalDiskUsage(raw));

    [Fact]
    public void Usage_Missing_IsEmpty() =>
        Assert.Equal(string.Empty, StorageMapper.MapPhysicalDiskUsage(null));

    // ---- OperationalStatus joins ----

    [Fact]
    public void OpStatus_SingleOk() =>
        Assert.Equal("OK", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)2 }));

    [Fact]
    public void OpStatus_JoinsMultiple() =>
        Assert.Equal("Degraded, OK", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)3, (ushort)2 }));

    [Fact]
    public void OpStatus_PoolExtras()
    {
        Assert.Equal("Read-only", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)53248 }));
        Assert.Equal("Incomplete", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)53249 }));
    }

    [Fact]
    public void OpStatus_InServiceSpellingQuirk()
    {
        Assert.Equal("In Service", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)11 }));
        Assert.Equal("In Service", StorageMapper.MapPhysicalDiskOperationalStatus(new object?[] { (ushort)11 }));
        Assert.Equal("InService", StorageMapper.MapVirtualDiskOperationalStatus(new object?[] { (ushort)11 }));
        Assert.Equal("InService", StorageMapper.MapVolumeOperationalStatus(new object?[] { (ushort)11 }));
    }

    [Fact]
    public void OpStatus_VolumeExtras() =>
        Assert.Equal("Offline", StorageMapper.MapVolumeOperationalStatus(new object?[] { (ushort)53267 }));

    [Fact]
    public void OpStatus_UnknownCode() =>
        Assert.Equal("Unknown", StorageMapper.MapPoolOperationalStatus(new object?[] { (ushort)60000 }));

    // ---- Row mappings ----

    [Fact]
    public void MapPhysicalDisk_Realistic()
    {
        var row = StorageMapper.MapPhysicalDisk(Bag(
            ("FriendlyName", "KINGSTON SA400M8240G"),
            ("MediaType", (ushort)4),
            ("BusType", (ushort)8),
            ("Size", 240057409536UL),
            ("HealthStatus", (ushort)0),
            ("OperationalStatus", new ushort[] { 2 }),
            ("CanPool", false),
            ("Usage", (ushort)1),
            ("SerialNumber", "50026B7685B857FC")));
        Assert.Equal("KINGSTON SA400M8240G", row.FriendlyName);
        Assert.Equal("SSD", row.MediaType);
        Assert.Equal("RAID", row.BusType);
        Assert.Equal(223.6, row.SizeGB); // Round(240057409536/1GB,1).
        Assert.Equal("Healthy", row.HealthStatus);
        Assert.Equal("OK", row.OperationalStatus);
        Assert.False(row.CanPool);
        Assert.Equal("Auto-Select", row.Usage);
        Assert.Equal("50026B7685B857FC", row.SerialNumber);
    }

    [Fact]
    public void MapStoragePool_FreeMath()
    {
        var row = StorageMapper.MapStoragePool(Bag(
            ("FriendlyName", "P1"),
            ("HealthStatus", (ushort)0),
            ("OperationalStatus", new ushort[] { 2 }),
            ("Size", 10737418240UL),
            ("AllocatedSize", 4294967296UL)));
        Assert.Equal(10.0, row.SizeGB);
        Assert.Equal(4.0, row.AllocatedGB);
        Assert.Equal(6.0, row.FreeGB);
    }

    [Fact]
    public void MapVolume_FixedOnly_ZeroSize()
    {
        Assert.Null(StorageMapper.MapVolume(Bag(("DriveType", (uint)5)))); // CD-ROM excluded.
        Assert.Null(StorageMapper.MapVolume(Bag(("DriveType", (uint)4)))); // Remote excluded.
        Assert.Null(StorageMapper.MapVolume(Bag())); // missing DriveType excluded.

        var zero = StorageMapper.MapVolume(Bag(
            ("DriveLetter", 'C'),
            ("FileSystemLabel", ""),
            ("FileSystem", "NTFS"),
            ("HealthStatus", (ushort)0),
            ("OperationalStatus", new ushort[] { 2 }),
            ("Size", 0UL),
            ("SizeRemaining", 0UL),
            ("DriveType", (uint)3)));
        Assert.NotNull(zero);
        Assert.Equal(0, zero!.FreePercent); // size 0 => 0, not NaN.
    }

    [Fact]
    public void MapVolume_FreePercentMath()
    {
        var row = StorageMapper.MapVolume(Bag(
            ("DriveLetter", 'C'),
            ("FileSystemLabel", "System"),
            ("FileSystem", "NTFS"),
            ("HealthStatus", (ushort)0),
            ("OperationalStatus", new ushort[] { 2 }),
            ("Size", 100UL * 1024 * 1024 * 1024),
            ("SizeRemaining", 25UL * 1024 * 1024 * 1024),
            ("DriveType", (uint)3)));
        Assert.NotNull(row);
        Assert.Equal(100.0, row!.SizeGB);
        Assert.Equal(25.0, row.FreeGB);
        Assert.Equal(25.0, row.FreePercent);
    }

    [Fact]
    public void MapVolume_CarriesTestOnlyIdentity()
    {
        var row = StorageMapper.MapVolume(Bag(
            ("DriveLetter", ""),
            ("FileSystemLabel", ""),
            ("FileSystem", "NTFS"),
            ("HealthStatus", (ushort)0),
            ("OperationalStatus", new ushort[] { 2 }),
            ("Size", 931131392UL),
            ("SizeRemaining", 120324096UL),
            ("DriveType", (uint)3),
            ("UniqueId", @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\"),
            ("Path", @"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\")));
        Assert.NotNull(row);
        Assert.Equal(@"\\?\Volume{5d84904f-cfb8-4b7b-a689-e9795f565650}\", row!.UniqueId);
        Assert.Equal(0.9, row.SizeGB);
    }

    [Fact]
    public void MapStorageJob_NoJobTypeInSchema()
    {
        var row = StorageMapper.MapStorageJob(Bag(
            ("Name", "Repair"),
            ("JobState", (ushort)4),
            ("PercentComplete", (ushort)42),
            ("BytesProcessed", 10UL),
            ("BytesTotal", 100UL),
            ("ElapsedTime", TimeSpan.FromMinutes(5))));
        Assert.Equal("Repair", row.Name);
        Assert.Equal("Running", row.JobState);
        Assert.Equal(string.Empty, row.JobType);
        Assert.Equal(42, row.PercentComplete);
        Assert.Equal(TimeSpan.FromMinutes(5), row.ElapsedTime);
    }
}
