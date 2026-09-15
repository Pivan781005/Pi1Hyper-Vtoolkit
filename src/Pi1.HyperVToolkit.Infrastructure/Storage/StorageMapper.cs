using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.Storage;

// Pure MSFT_* (root/Microsoft/Windows/Storage) to row-model mapping.
// All enum tables are transcribed VERBATIM from the Storage module's
// Storage.types.ps1xml ScriptProperty blocks on the reference machine — raw
// MMI values are UInt16/UInt32 (PowerShell translates them via that file, MMI
// does not), so C# must apply the same tables for functional parity.
// Every method takes plain property bags: unit-testable without storage.
public static class StorageMapper
{
    public const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public static StorageJobRow MapStorageJob(IReadOnlyDictionary<string, object?> bag) =>
        new(
            CimValues.GetString(bag, "Name"),
            MapJobState(CimValues.GetLong(bag, "JobState")),
            string.Empty, // MSFT_StorageJob carries no JobType (schema-verified).
            CimValues.GetInt(bag, "PercentComplete"),
            CimValues.GetULong(bag, "BytesProcessed"),
            CimValues.GetULong(bag, "BytesTotal"),
            MapElapsedTime(bag.TryGetValue("ElapsedTime", out var elapsed) ? elapsed : null));

    public static StoragePoolRow MapStoragePool(IReadOnlyDictionary<string, object?> bag)
    {
        var size = CimValues.GetULong(bag, "Size") ?? 0;
        var allocated = CimValues.GetULong(bag, "AllocatedSize") ?? 0;
        var free = size >= allocated ? size - allocated : 0;
        return new(
            CimValues.GetString(bag, "FriendlyName"),
            MapHealthStatus(CimValues.GetLong(bag, "HealthStatus")),
            MapOperationalStatus(GetArray(bag, "OperationalStatus"), PoolOperationalExtras),
            CimValues.BytesToGigabytes(size),
            CimValues.BytesToGigabytes(allocated),
            CimValues.Round1((size - allocated) / (1024.0 * 1024.0 * 1024.0)),
            (long)size,
            (long)allocated,
            (long)free);
    }

    public static VirtualDiskRow MapVirtualDisk(IReadOnlyDictionary<string, object?> bag)
    {
        var size = CimValues.GetULong(bag, "Size") ?? 0;
        var allocated = CimValues.GetULong(bag, "AllocatedSize") ?? 0;
        return new(
            CimValues.GetString(bag, "FriendlyName"),
            MapHealthStatus(CimValues.GetLong(bag, "HealthStatus")),
            MapVirtualDiskOperationalStatus(GetArray(bag, "OperationalStatus")),
            CimValues.GetString(bag, "ResiliencySettingName"),
            MapProvisioningType(CimValues.GetLong(bag, "ProvisioningType"), missingAsEmpty: true),
            CimValues.BytesToGigabytes(size),
            CimValues.BytesToGigabytes(allocated),
            (long)size,
            (long)allocated);
    }

    public static PhysicalDiskRow MapPhysicalDisk(IReadOnlyDictionary<string, object?> bag)
    {
        var size = CimValues.GetULong(bag, "Size") ?? 0;
        return new(
            CimValues.GetString(bag, "FriendlyName"),
            MapMediaType(CimValues.GetLong(bag, "MediaType")),
            MapBusType(CimValues.GetLong(bag, "BusType")),
            CimValues.BytesToGigabytes(size),
            MapHealthStatus(CimValues.GetLong(bag, "HealthStatus")),
            MapOperationalStatus(GetArray(bag, "OperationalStatus"), PhysicalDiskOperationalExtras),
            CimValues.GetBool(bag, "CanPool"),
            MapPhysicalDiskUsage(CimValues.GetLong(bag, "Usage")),
            CimValues.GetString(bag, "SerialNumber"),
            (long)size);
    }

    public static StorageVolumeRow? MapVolume(IReadOnlyDictionary<string, object?> bag)
    {
        if (!IsFixedDrive(bag))
        {
            return null;
        }

        var size = CimValues.GetULong(bag, "Size") ?? 0;
        var remaining = CimValues.GetULong(bag, "SizeRemaining") ?? 0;
        return new(
            CimValues.GetString(bag, "DriveLetter"),
            CimValues.GetString(bag, "FileSystemLabel"),
            CimValues.GetString(bag, "FileSystem"),
            MapVolumeHealth(CimValues.GetLong(bag, "HealthStatus")),
            MapVolumeOperationalStatus(GetArray(bag, "OperationalStatus")),
            CimValues.BytesToGigabytes(size),
            CimValues.BytesToGigabytes(remaining),
            size > 0 ? CimValues.Round1(remaining / (double)size * 100) : 0,
            CimValues.GetString(bag, "UniqueId"),
            CimValues.GetString(bag, "Path"),
            (long)size,
            (long)remaining);
    }

    // Get-Volume DriveType == Fixed parity: raw MMI value is UInt32 3 (the
    // PowerShell layer translates it to the "Fixed" string via types.ps1xml).
    public static bool IsFixedDrive(IReadOnlyDictionary<string, object?> bag)
    {
        if (!bag.TryGetValue("DriveType", out var value) || value is null)
        {
            return false;
        }

        if (value is string text)
        {
            return string.Equals(text, "Fixed", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture) == 3;
        }
        catch (Exception) when (value is IConvertible)
        {
            return false;
        }
    }

    // Shared HealthStatus table (pool / virtual disk / physical disk):
    // 0 Healthy, 1 Warning, 2 Unhealthy, 5 Unknown, default Unknown.
    public static string MapHealthStatus(long? value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        5 => "Unknown",
        _ => "Unknown",
    };

    // MSFT_Volume HealthStatus table: 0 Healthy, 1 Warning, 2 Unhealthy.
    public static string MapVolumeHealth(long? value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown",
    };

    // MSFT_StorageJob.JobState table (DMTF concrete-job states).
    public static string MapJobState(long? value) => value switch
    {
        2 => "New",
        3 => "Starting",
        4 => "Running",
        5 => "Suspended",
        6 => "Shutting Down",
        7 => "Completed",
        8 => "Terminated",
        9 => "Killed",
        10 => "Exception",
        11 => "Service",
        12 => "Query Pending",
        _ => "Unknown",
    };

    // ProvisioningType: 0 Unknown, 1 Thin, 2 Fixed. A missing value renders
    // empty (the ScriptProperty returns $null), never "Unknown".
    public static string MapProvisioningType(long? value, bool missingAsEmpty = false)
    {
        if (value is null)
        {
            return missingAsEmpty ? string.Empty : "Unknown";
        }

        return value switch
        {
            0 => "Unknown",
            1 => "Thin",
            2 => "Fixed",
            _ => "Unknown",
        };
    }

    // MediaType: 0 Unspecified, 3 HDD, 4 SSD, 5 SCM. The switch has no null
    // guard, so a missing value falls to Default "Unspecified".
    public static string MapMediaType(long? value) => value switch
    {
        0 or null => "Unspecified",
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Unspecified",
    };

    // BusType table (Storage.types.ps1xml). Missing renders empty.
    public static string MapBusType(long? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            0 => "Unknown",
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "1394",
            5 => "SSA",
            6 => "Fibre Channel",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "Virtual",
            15 => "File Backed Virtual",
            16 => "Spaces",
            17 => "NVMe",
            18 => "SCM",
            19 => "UFS",
            _ => "Unknown",
        };
    }

    // PhysicalDisk Usage: 0 Unknown, 1 Auto-Select, 2 Manual-Select,
    // 3 Hot Spare, 4 Retired, 5 Journal. Missing renders empty.
    public static string MapPhysicalDiskUsage(long? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            0 => "Unknown",
            1 => "Auto-Select",
            2 => "Manual-Select",
            3 => "Hot Spare",
            4 => "Retired",
            5 => "Journal",
            _ => "Unknown",
        };
    }

    // Shared OperationalStatus base (1..19) plus per-class vendor codes.
    // PowerShell yields a string ARRAY; C# joins with ", " (single "OK" stays "OK").
    // Class quirk, transcribed exactly: code 11 spells "In Service" in the
    // pool/physical-disk/job tables but "InService" in the virtual-disk and
    // volume tables.
    public static string MapOperationalStatus(
        IEnumerable<object?> rawValues,
        IReadOnlyDictionary<long, string> extras,
        string inService = "In Service")
    {
        var mapped = new List<string>();
        foreach (var raw in rawValues)
        {
            long? code = raw is string text && !IsNumericText(text)
                ? MapOperationalNameBack(text)
                : ToLong(raw);
            var name = code switch
            {
                1 => "Other",
                2 => "OK",
                3 => "Degraded",
                4 => "Stressed",
                5 => "Predictive Failure",
                6 => "Error",
                7 => "Non-Recoverable Error",
                8 => "Starting",
                9 => "Stopping",
                10 => "Stopped",
                11 => inService,
                12 => "No Contact",
                13 => "Lost Communication",
                14 => "Aborted",
                15 => "Dormant",
                16 => "Supporting Entity in Error",
                17 => "Completed",
                18 => "Power Mode",
                19 => "Relocating",
                not null when extras.TryGetValue(code.Value, out var extra) => extra,
                _ => "Unknown",
            };
            mapped.Add(name);
        }

        return string.Join(", ", mapped);
    }

    public static string MapPoolOperationalStatus(IEnumerable<object?> raw) =>
        MapOperationalStatus(raw, PoolOperationalExtras);

    public static string MapPhysicalDiskOperationalStatus(IEnumerable<object?> raw) =>
        MapOperationalStatus(raw, PhysicalDiskOperationalExtras);

    public static string MapVirtualDiskOperationalStatus(IEnumerable<object?> raw) =>
        MapOperationalStatus(raw, VirtualDiskOperationalExtras, inService: "InService");

    public static string MapVolumeOperationalStatus(IEnumerable<object?> raw) =>
        MapOperationalStatus(raw, VolumeOperationalExtras, inService: "InService");

    private static readonly IReadOnlyDictionary<long, string> PoolOperationalExtras =
        new Dictionary<long, string> { [53248] = "Read-only", [53249] = "Incomplete" };

    private static readonly IReadOnlyDictionary<long, string> VirtualDiskOperationalExtras =
        new Dictionary<long, string>
        {
            [53250] = "Detached", [53251] = "Incomplete",
            [53275] = "Suboptimal", [53284] = "No Redundancy",
        };

    private static readonly IReadOnlyDictionary<long, string> PhysicalDiskOperationalExtras =
        new Dictionary<long, string>
        {
            [53252] = "Failed Media", [53253] = "Split", [53254] = "Stale Metadata",
            [53255] = "IO Error", [53256] = "Unrecognized Metadata",
            [53269] = "Removing From Pool", [53270] = "In Maintenance Mode",
            [53271] = "Updating Firmware", [53272] = "Device Hardware Error",
            [53273] = "Not Usable", [53274] = "Transient Error",
            [53276] = "Starting Maintenance Mode", [53277] = "Stopping Maintenance Mode",
            [53285] = "Threshold Exceeded", [53286] = "Abnormal Latency",
        };

    private static readonly IReadOnlyDictionary<long, string> VolumeOperationalExtras =
        new Dictionary<long, string>
        {
            [53261] = "Scan Needed", [53262] = "Spot Fix Needed",
            [53263] = "Full Repair Needed", [53267] = "Offline",
        };

    private static IEnumerable<object?> GetArray(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>();
        }

        return [value];
    }

    private static long? ToLong(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }

    private static bool IsNumericText(string text) =>
        long.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    // Defensive back-mapping: if MMI ever returns an already-translated name
    // (observed with Get-CimInstance under the Storage type adaptation), keep
    // it stable instead of collapsing to Unknown.
    private static long? MapOperationalNameBack(string text) => text.Trim() switch
    {
        "Other" => 1,
        "OK" => 2,
        "Degraded" => 3,
        "Stressed" => 4,
        "Predictive Failure" => 5,
        "Error" => 6,
        "Non-Recoverable Error" => 7,
        "Starting" => 8,
        "Stopping" => 9,
        "Stopped" => 10,
        "In Service" or "InService" => 11,
        "No Contact" => 12,
        "Lost Communication" => 13,
        "Aborted" => 14,
        "Dormant" => 15,
        "Supporting Entity in Error" => 16,
        "Completed" => 17,
        "Power Mode" => 18,
        "Relocating" => 19,
        _ => null,
    };

    private static TimeSpan? MapElapsedTime(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is TimeSpan span)
        {
            return span;
        }

        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // CIM datetime intervals arrive either as TimeSpan (above) or as DMTF
        // strings ("00000000000000.000000:000"); PowerShell renders TimeSpan.
        try
        {
            return System.Management.ManagementDateTimeConverter.ToTimeSpan(text);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }
}
