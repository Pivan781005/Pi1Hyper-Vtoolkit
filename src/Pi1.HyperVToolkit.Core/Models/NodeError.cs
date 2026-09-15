namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Machine-readable failure classification for per-node queries.
/// Backs the Invoke-PiSafe parity rule: one failing node must not destroy
/// successful rows from other nodes. Cancellation is reported, never an error.
/// </summary>
public enum NodeErrorKind
{
    Unknown = 0,
    AccessDenied = 1,
    HostUnreachable = 2,
    Timeout = 3,
    WmiUnavailable = 4,
    HyperVUnavailable = 5,
    ClusterUnavailable = 6,
    Cancelled = 7,
}

/// <summary>Slovak short message plus technical detail for one node failure.</summary>
public sealed record NodeError(NodeErrorKind Kind, string Message, string? Detail = null);
