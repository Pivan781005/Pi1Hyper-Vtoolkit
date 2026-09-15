using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Infrastructure.Mapping;

/// <summary>
/// Pure root/cimv2 mapping. Parity notes:
/// - RAM: Win32_OperatingSystem TotalVisibleMemorySize/FreePhysicalMemory are
///   kilobytes; PowerShell divides by 1MB (i.e. KB/1048576 = GB), rounded to 1.
/// - CPU: Sockets = processor instance count; Cores/Logical = sums.
/// - Volumes: DriveType=3 only (no network drives).
/// - Adapters: IPEnabled=True only; IPv4 filtered with the tested helper.
/// </summary>
public static class SystemInfoMapper
{
    public static NodeHardwareRow MapHardware(
        string node,
        IReadOnlyDictionary<string, object?>? computerSystem,
        IReadOnlyDictionary<string, object?>? operatingSystem,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> processors,
        DateTime now)
    {
        double? totalGb = null;
        double? freeGb = null;
        double? usedGb = null;
        double? usedPct = null;
        long? totalBytes = null;
        long? freeBytes = null;
        long? usedBytes = null;

        var totalKb = operatingSystem is null ? null : CimValues.GetDouble(operatingSystem, "TotalVisibleMemorySize");
        var freeKb = operatingSystem is null ? null : CimValues.GetDouble(operatingSystem, "FreePhysicalMemory");
        if (totalKb.HasValue)
        {
            totalGb = CimValues.Round1(totalKb.Value / 1048576.0);
            totalBytes = (long)Math.Round(totalKb.Value * 1024.0, MidpointRounding.AwayFromZero);
            if (freeKb.HasValue)
            {
                freeGb = CimValues.Round1(freeKb.Value / 1048576.0);
                usedGb = CimValues.Round1((totalKb.Value - freeKb.Value) / 1048576.0);
                freeBytes = (long)Math.Round(freeKb.Value * 1024.0, MidpointRounding.AwayFromZero);
                usedBytes = totalBytes.Value - freeBytes.Value;
                if (totalKb.Value > 0)
                {
                    usedPct = CimValues.Round1((totalKb.Value - freeKb.Value) / totalKb.Value * 100.0);
                }
            }
        }
        else if (computerSystem is not null)
        {
            var totalPhysBytes = CimValues.GetDouble(computerSystem, "TotalPhysicalMemory");
            if (totalPhysBytes.HasValue)
            {
                totalGb = CimValues.BytesToGigabytes(totalPhysBytes.Value);
                totalBytes = (long)Math.Round(totalPhysBytes.Value, MidpointRounding.AwayFromZero);
            }
        }

        TimeSpan? uptime = null;
        var boot = operatingSystem is null ? null : CimValues.GetDateTime(operatingSystem, "LastBootUpTime");
        if (boot.HasValue)
        {
            var span = now - boot.Value;
            uptime = span.TotalSeconds < 1 ? TimeSpan.Zero : span;
        }

        var cpuNames = processors
            .Select(p => CimValues.GetString(p, "Name"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        int? cores = processors.Count == 0 ? null : processors.Sum(p => CimValues.GetInt(p, "NumberOfCores") ?? 0);
        int? logical = processors.Count == 0 ? null : processors.Sum(p => CimValues.GetInt(p, "NumberOfLogicalProcessors") ?? 0);

        return new NodeHardwareRow(
            node,
            computerSystem is null ? string.Empty : CimValues.GetString(computerSystem, "Manufacturer"),
            computerSystem is null ? string.Empty : CimValues.GetString(computerSystem, "Model"),
            operatingSystem is null ? string.Empty : CimValues.GetString(operatingSystem, "Caption"),
            operatingSystem is null ? string.Empty : CimValues.GetString(operatingSystem, "Version"),
            uptime,
            string.Join(", ", cpuNames),
            processors.Count,
            cores,
            logical,
            totalGb,
            usedGb,
            freeGb,
            usedPct,
            totalBytes,
            usedBytes,
            freeBytes);
    }

    public static IReadOnlyList<NodeVolumeRow> MapVolumes(
        string node, IReadOnlyList<IReadOnlyDictionary<string, object?>> disks)
    {
        var rows = new List<NodeVolumeRow>();
        foreach (var disk in disks)
        {
            var size = CimValues.GetDouble(disk, "Size") ?? 0;
            var free = CimValues.GetDouble(disk, "FreeSpace") ?? 0;
            var sizeBytes = (long)Math.Round(size, MidpointRounding.AwayFromZero);
            var freeBytes = (long)Math.Round(free, MidpointRounding.AwayFromZero);
            rows.Add(new NodeVolumeRow(
                node,
                CimValues.GetString(disk, "DeviceID"),
                CimValues.GetString(disk, "VolumeName"),
                CimValues.GetString(disk, "FileSystem"),
                CimValues.BytesToGigabytes(size),
                CimValues.BytesToGigabytes(free),
                size > 0 ? CimValues.Round1(free / size * 100.0) : 0,
                sizeBytes,
                freeBytes));
        }

        return rows;
    }

    public static IReadOnlyList<NodeNetworkAdapterRow> MapAdapters(
        string node, IReadOnlyList<IReadOnlyDictionary<string, object?>> adapters)
    {
        var rows = new List<NodeNetworkAdapterRow>();
        foreach (var adapter in adapters)
        {
            var ips = CimValues.GetStringArray(adapter, "IPAddress");
            rows.Add(new NodeNetworkAdapterRow(
                node,
                CimValues.GetString(adapter, "Description"),
                NetworkText.FilterIPv4(ips),
                CimValues.GetString(adapter, "MACAddress"),
                string.Join(", ", CimValues.GetStringArray(adapter, "DefaultIPGateway")),
                string.Join(", ", CimValues.GetStringArray(adapter, "DNSServerSearchOrder")),
                CimValues.GetBool(adapter, "DHCPEnabled")));
        }

        return rows;
    }
}
