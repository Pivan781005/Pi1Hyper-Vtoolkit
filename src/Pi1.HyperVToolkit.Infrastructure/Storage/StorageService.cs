using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.Storage;

// Native local storage inventory. LOCAL-ONLY by design (reference semantics):
// every query targets the local machine, never the scope fan-out. An empty
// result is a valid state (idle jobs, primordial-only pools); provider errors
// surface as Slovak InvalidOperationExceptions for the view-model banner.
public sealed class StorageService : IStorageService
{
    private const string Ns = StorageMapper.StorageNamespace;

    private readonly ICimQuerier _cim;
    private readonly ILogger<StorageService> _logger;

    public StorageService(ICimQuerier cim, ILogger<StorageService> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<StorageJobRow>> GetStorageJobsAsync(
        CancellationToken cancellationToken = default)
    {
        var bags = await QueryLocalAsync(
            "SELECT Name, JobState, PercentComplete, BytesProcessed, BytesTotal, ElapsedTime FROM MSFT_StorageJob",
            "Storage Jobs", cancellationToken).ConfigureAwait(false);
        return bags.Select(StorageMapper.MapStorageJob).ToList();
    }

    public async Task<IReadOnlyList<StoragePoolRow>> GetStoragePoolsAsync(
        CancellationToken cancellationToken = default)
    {
        var bags = await QueryLocalAsync(
            "SELECT FriendlyName, HealthStatus, OperationalStatus, Size, AllocatedSize, IsPrimordial FROM MSFT_StoragePool",
            "Storage Pools", cancellationToken).ConfigureAwait(false);
        return bags
            .Where(b => !IsPrimordial(b))
            .Select(StorageMapper.MapStoragePool)
            .OrderBy(r => r.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<VirtualDiskRow>> GetVirtualDisksAsync(
        CancellationToken cancellationToken = default)
    {
        var bags = await QueryLocalAsync(
            "SELECT FriendlyName, HealthStatus, OperationalStatus, ResiliencySettingName, ProvisioningType, Size, AllocatedSize FROM MSFT_VirtualDisk",
            "Virtual Disks", cancellationToken).ConfigureAwait(false);
        return bags
            .Select(StorageMapper.MapVirtualDisk)
            .OrderBy(r => r.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<PhysicalDiskRow>> GetPhysicalDisksAsync(
        CancellationToken cancellationToken = default)
    {
        var bags = await QueryLocalAsync(
            "SELECT FriendlyName, MediaType, BusType, Size, HealthStatus, OperationalStatus, CanPool, Usage, SerialNumber FROM MSFT_PhysicalDisk",
            "fyzické disky", cancellationToken).ConfigureAwait(false);
        return bags
            .Select(StorageMapper.MapPhysicalDisk)
            .OrderBy(r => r.MediaType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.BusType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<StorageVolumeRow>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var bags = await QueryLocalAsync(
            "SELECT DriveLetter, FileSystemLabel, FileSystem, HealthStatus, OperationalStatus, Size, SizeRemaining, DriveType, UniqueId, Path FROM MSFT_Volume",
            "volumes", cancellationToken).ConfigureAwait(false);
        return bags
            .Select(StorageMapper.MapVolume)
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => r.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FileSystemLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<CsvRow>> GetCsvRowsAsync(
        CancellationToken cancellationToken = default)
    {
        const string clusterNs = @"root\MSCluster";
        IReadOnlyList<IReadOnlyDictionary<string, object?>> volumes;
        try
        {
            volumes = await _cim.QueryAsync(
                ".", clusterNs,
                "SELECT Name, State, OwnerNode FROM MSCluster_ClusterSharedVolume",
                TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Expected on non-cluster hosts (namespace missing): empty rows,
            // exactly like the reference when FailoverClusters is unavailable.
            _logger.LogDebug(ex, "CSV: klastrová funkcia nie je dostupná, vraciam prázdny zoznam.");
            return [];
        }

        var partitions = await QueryClusterPartitionsAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<CsvRow>();
        foreach (var volume in volumes)
        {
            var name = CimValues.GetString(volume, "Name");
            var state = CimValues.GetString(volume, "State");
            var owner = CimValues.GetString(volume, "OwnerNode");
            var matched = partitions
                .Where(p => IsPartitionOf(p, name))
                .ToList();
            if (matched.Count == 0)
            {
                // Volume without visible partitions: keep the inventory row
                // with zero capacity rather than dropping the CSV silently.
                rows.Add(new CsvRow(name, state, owner, 0, 0, 0, 0, string.Empty));
                continue;
            }

            foreach (var partition in matched)
            {
                var size = CimValues.GetULong(partition, "Size") ?? 0;
                var free = CimValues.GetULong(partition, "FreeSpace") ?? 0;
                var used = size >= free ? size - free : 0;
                rows.Add(new CsvRow(
                    name,
                    state,
                    owner,
                    CimValues.BytesToGigabytes(size),
                    CimValues.BytesToGigabytes(free),
                    CimValues.BytesToGigabytes(used),
                    size > 0 ? CimValues.Round1(free / (double)size * 100) : 0,
                    CimValues.GetString(partition, "FriendlyVolumeName"),
                    (long)size,
                    (long)free,
                    (long)used));
            }
        }

        return rows.OrderBy(r => r.FreePercent).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryClusterPartitionsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cim.QueryAsync(
                ".", @"root\MSCluster",
                "SELECT Name, Size, FreeSpace, FriendlyVolumeName FROM MSCluster_DiskPartition",
                TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CSV: oddiely MSCluster_DiskPartition nie sú dostupné.");
            return [];
        }
    }

    private static bool IsPartitionOf(IReadOnlyDictionary<string, object?> partition, string volumeName)
    {
        if (string.IsNullOrEmpty(volumeName))
        {
            return false;
        }

        // Best-effort association: the partition's hosting path or name embeds
        // the CSV name. Verified mapping requires a cluster-capable host
        // (PARTIAL until elevated cluster parity); unmatched partitions are
        // reported via the zero-capacity fallback above, never invented.
        foreach (var key in new[] { "Name", "Path", "FriendlyVolumeName" })
        {
            var text = CimValues.GetString(partition, key);
            if (!string.IsNullOrEmpty(text) &&
                text.Contains(volumeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrimordial(IReadOnlyDictionary<string, object?> bag)
    {
        if (!bag.TryGetValue("IsPrimordial", out var value) || value is null)
        {
            return false;
        }

        if (value is bool flag)
        {
            return flag;
        }

        return bool.TryParse(
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            out var parsed) && parsed;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryLocalAsync(
        string wql, string area, CancellationToken cancellationToken)
    {
        try
        {
            return await _cim.QueryAsync(
                ".", Ns, wql, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Načítanie {Area} zlyhalo.", area);
            throw new InvalidOperationException(
                $"Nepodarilo sa načítať {area}.", ex);
        }
    }
}
