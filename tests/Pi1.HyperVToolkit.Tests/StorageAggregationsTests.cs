using Pi1.HyperVToolkit.Core.Aggregations;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Tests;

public sealed class StorageAggregationsTests
{
    private static CsvRow Csv(string name, double freePercent, string path, double freeGb = 10) =>
        new(name, "Online", "N1", 100, freeGb, 90, freePercent, path);

    // ---- Get-PiCsvMatchForPath parity ----

    [Fact]
    public void CsvMatch_NullPath_ReturnsNull() =>
        Assert.Null(StorageAggregations.FindBestCsvMatch(null, [Csv("C1", 50, "C:\\ClusterStorage\\V1")]));

    [Fact]
    public void CsvMatch_EmptyPath_ReturnsNull() =>
        Assert.Null(StorageAggregations.FindBestCsvMatch("  ", [Csv("C1", 50, "C:\\ClusterStorage\\V1")]));

    [Fact]
    public void CsvMatch_NullRows_ReturnsNull() =>
        Assert.Null(StorageAggregations.FindBestCsvMatch("C:\\x.vhdx", null));

    [Fact]
    public void CsvMatch_CaseInsensitivePrefix()
    {
        var rows = new[] { Csv("CSV1", 50, @"C:\ClusterStorage\Volume1") };
        Assert.NotNull(StorageAggregations.FindBestCsvMatch(@"c:\clusterstorage\volume1\vm\disk.vhdx", rows));
    }

    [Fact]
    public void CsvMatch_LongestPrefixWins()
    {
        var rows = new[]
        {
            Csv("Short", 50, @"C:\ClusterStorage\Volume1"),
            Csv("Long", 40, @"C:\ClusterStorage\Volume1\Sub"),
        };
        var best = StorageAggregations.FindBestCsvMatch(@"C:\ClusterStorage\Volume1\Sub\disk.vhdx", rows);
        Assert.Equal("Long", best?.Name);
    }

    [Fact]
    public void CsvMatch_OutsideCsv_ReturnsNull()
    {
        var rows = new[] { Csv("C1", 50, @"C:\ClusterStorage\Volume1") };
        Assert.Null(StorageAggregations.FindBestCsvMatch(@"D:\VMs\disk.vhdx", rows));
    }

    // ---- Physical disk summary ----

    [Fact]
    public void DiskSummary_GroupsByMediaAndBus()
    {
        var disks = new[]
        {
            new PhysicalDiskRow("A", "SSD", "RAID", 100, "Healthy", "OK", false, "Auto-Select", "S1"),
            new PhysicalDiskRow("B", "SSD", "RAID", 200, "Warning", "Degraded", true, "Auto-Select", "S2"),
            new PhysicalDiskRow("C", "HDD", "SATA", 500, "Healthy", "OK", true, "Manual-Select", "S3"),
        };
        var summary = StorageAggregations.SummarizePhysicalDisks(disks);
        Assert.Equal(2, summary.Count);
        var raid = Assert.Single(summary, r => r.Group == "SSD, RAID");
        Assert.Equal(2, raid.Count);
        Assert.Equal(300, raid.TotalGB);
        Assert.Equal(1, raid.Healthy);
        Assert.Equal(1, raid.NotHealthy);
        Assert.Equal(1, raid.CanPool);
        // Sorted by Group.
        Assert.Equal("HDD, SATA", summary[0].Group);
    }

    [Fact]
    public void DiskSummary_OneDecimalRounding() =>
        // Verified runtime value: Math.Round(100.125, 1) == 100.1 on .NET.
        // PowerShell [math]::Round calls the same method on the same double,
        // so parity holds by construction; the test pins the shared behavior.
        Assert.Equal(100.1, StorageAggregations.SummarizePhysicalDisks(
            [new PhysicalDiskRow("A", "SSD", "NVMe", 100.125, "Healthy", "OK", false, "Auto-Select", "S1")])[0].TotalGB);

    // ---- Storage summary: exact 4 rows ----

    [Fact]
    public void StorageSummary_ExactFourRows_AllOk()
    {
        var rows = StorageAggregations.BuildStorageSummary(
            [new StoragePoolRow("P", "Healthy", "OK", 10, 5, 5)],
            [new VirtualDiskRow("V", "Healthy", "OK", "Parity", "Thin", 10, 5)],
            [Csv("C1", 50, "C:\\CSV1")],
            []);
        Assert.Equal(["Storage Pools", "Virtual Disks", "CSV", "Storage Jobs"], rows.Select(r => r.Area));
        Assert.Equal("OK", rows[0].Status);
        Assert.Equal("OK", rows[1].Status);
        Assert.Equal("OK", rows[2].Status);
        Assert.Equal("None", rows[3].Status); // no jobs => None, not OK.
    }

    [Fact]
    public void StorageSummary_Warnings()
    {
        var rows = StorageAggregations.BuildStorageSummary(
            [new StoragePoolRow("P", "Warning", "Degraded", 10, 5, 5)],
            [new VirtualDiskRow("V", "Unhealthy", "Error", "Parity", "Thin", 10, 5)],
            [Csv("C1", 14.9, "C:\\CSV1")],
            [new StorageJobRow("Repair", "Running", "", 10, 1, 2, null)]);
        Assert.Equal("Warning", rows[0].Status);
        Assert.Equal("Warning", rows[1].Status);
        Assert.Equal("Warning", rows[2].Status);
        Assert.Equal("Warning", rows[3].Status);
    }

    [Fact]
    public void StorageSummary_CsvThreshold_15IsOk()
    {
        var rows = StorageAggregations.BuildStorageSummary([], [], [Csv("C1", 15.0, "C:\\CSV1")], []);
        Assert.Equal("OK", rows[2].Status);
    }

    // ---- VM storage by CSV ----

    private static VmStorageRow StorageRow(
        string vm, string csv, double? size, double? file, double? pct, double? freeGb = 5) =>
        new("N1", vm, "Running", "SCSI 0:0", "VHDX", "Dynamic", size, file, csv, freeGb, pct, $"C:\\{vm}.vhdx");

    [Fact]
    public void ByCsv_EmptyNameBecomesFixedLabel_UniqueVmCount()
    {
        var rows = new[]
        {
            StorageRow("VM1", "", 10, 8, null),
            StorageRow("VM1", "", 20, 16, null),
            StorageRow("VM2", "CSV1", 30, 30, 40.0),
        };
        var grouped = StorageAggregations.GroupVmStorageByCsv(rows);
        Assert.Equal(2, grouped.Count);
        var outside = Assert.Single(grouped, g => g.CSV == "(mimo CSV / nezistené)");
        Assert.Equal(1, outside.VMCount); // distinct VMs, not rows.
        Assert.Equal(2, outside.DiskCount);
        Assert.Equal(30, outside.VHDSizeGB);
        Assert.Equal(24, outside.VHDFileGB);
        Assert.Null(outside.CSVFreePercent);
        var csv1 = Assert.Single(grouped, g => g.CSV == "CSV1");
        Assert.Equal(40.0, csv1.CSVFreePercent);
        Assert.Equal(5, csv1.CSVFreeGB);
    }

    [Fact]
    public void ByCsv_EmptyGroupSortsFirst()
    {
        var rows = new[]
        {
            StorageRow("VM2", "CSV1", 30, 30, 40.0),
            StorageRow("VM1", "", 10, 8, null),
        };
        var grouped = StorageAggregations.GroupVmStorageByCsv(rows);
        Assert.Equal("(mimo CSV / nezistené)", grouped[0].CSV);
    }

    [Fact]
    public void ByCsv_MultipleVmsOnOneCsv()
    {
        var rows = new[]
        {
            StorageRow("VM1", "CSV1", 10.5, 10, 20.0),
            StorageRow("VM2", "CSV1", 15.25, 5, 20.0),
        };
        var grouped = Assert.Single(StorageAggregations.GroupVmStorageByCsv(rows));
        Assert.Equal(2, grouped.VMCount);
        Assert.Equal(2, grouped.DiskCount);
        Assert.Equal(25.8, grouped.VHDSizeGB); // exact binary sum, ToEven parity.
    }

    // ---- Map sort / selected VM ----

    [Fact]
    public void StorageMap_SortsByPercentHostVmPath()
    {
        var rows = new[]
        {
            StorageRow("N1", "CSV1", 1, 1, 40.0),
            new VmStorageRow("N1", "VM0", "Off", "SCSI 0:0", "VHDX", "Fixed", 1, 1, "", null, null, "C:\\a.vhdx"),
            StorageRow("N1", "CSV1", 1, 1, 5.0),
        };
        var sorted = StorageAggregations.SortStorageMap(rows);
        Assert.Null(sorted[0].CSVFreePercent); // PowerShell "" sorts first.
        Assert.Equal(5.0, sorted[1].CSVFreePercent);
        Assert.Equal(40.0, sorted[2].CSVFreePercent);
    }

    [Fact]
    public void FilterForVm_MatchesHostAndVm_SortedByPath()
    {
        var rows = new[]
        {
            new VmStorageRow("N1", "VM1", "Running", "SCSI 0:1", "VHDX", "Dynamic", 2, 2, "", null, null, "C:\\b.vhdx"),
            new VmStorageRow("N1", "VM1", "Running", "SCSI 0:0", "VHDX", "Dynamic", 1, 1, "", null, null, "C:\\a.vhdx"),
            new VmStorageRow("N1", "VM2", "Running", "SCSI 0:0", "VHDX", "Dynamic", 1, 1, "", null, null, "C:\\c.vhdx"),
            new VmStorageRow("N2", "VM1", "Running", "SCSI 0:0", "VHDX", "Dynamic", 1, 1, "", null, null, "C:\\d.vhdx"),
        };
        var filtered = StorageAggregations.FilterForVm(rows, "N1", "VM1");
        Assert.Equal(2, filtered.Count);
        Assert.Equal("C:\\a.vhdx", filtered[0].Path);
        Assert.Equal("C:\\b.vhdx", filtered[1].Path);
    }
}
