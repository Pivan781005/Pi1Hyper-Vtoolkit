namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterEvents (Get-WinEvent, MaxEvents 30), PLUS the
// approved presentation improvement: ProviderName is a first-class column
// (the console table omitted it). Native .NET event-log read, newest first
// as returned by the provider. Message is never truncated.
public sealed record ClusterEventRow(
    DateTime? TimeCreated,
    int? Id,
    string Level,
    string Provider,
    string Message);
