using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Infrastructure.Cluster;

// Native .NET event-log read of Microsoft-Windows-FailoverClustering/Operational
// (Get-WinEvent parity, MaxEvents 30). Read-only. A missing/unreadable log
// yields zero rows — never an exception, never fake events.
public sealed class ClusterEventReader
{
    private const string Channel = "Microsoft-Windows-FailoverClustering/Operational";

    private readonly ILogger<ClusterEventReader> _logger;

    public ClusterEventReader(ILogger<ClusterEventReader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ClusterEventRow> Read(int maxEvents = Thresholds.ClusterEventsMaxCount)
    {
        var rows = new List<ClusterEventRow>();
        try
        {
            var query = new EventLogQuery(Channel, PathType.LogName) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var taken = 0; taken < maxEvents; taken++)
            {
                using var record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                rows.Add(MapRecord(record));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Absent log / no access / stopped service: legitimate empty state.
            _logger.LogDebug(ex, "Cluster event log nie je dostupný.");
        }

        return rows;
    }

    internal static ClusterEventRow MapRecord(EventRecord record)
    {
        string? level = null;
        try
        {
            level = record.LevelDisplayName;
        }
        catch (Exception)
        {
            // Display name resolution can fail for unregistered providers.
        }

        return new ClusterEventRow(
            TryGetTime(record),
            TryGetId(record),
            string.IsNullOrWhiteSpace(level) ? MapLevel(record.Level) : level,
            record.ProviderName ?? string.Empty,
            record.FormatDescription() ?? string.Empty);
    }

    private static DateTime? TryGetTime(EventRecord record)
    {
        try
        {
            return record.TimeCreated;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? TryGetId(EventRecord record)
    {
        try
        {
            return record.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Standard event level bytes (0 LogAlways … 5 Verbose). Fallback only;
    // LevelDisplayName is preferred above.
    internal static string MapLevel(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Informational",
        5 => "Verbose",
        null => string.Empty,
        _ => $"Unknown ({level})",
    };
}
