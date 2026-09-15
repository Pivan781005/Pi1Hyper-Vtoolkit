namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterHealthScore checks. Detail keeps the exact
// reference text ("{n} not OK/pending/failed", "{n} CSV below 15%", ...)
// so parity compares directly; Area/Status localize in WPF.
public sealed record HealthCheckRow(
    string Area,
    string Status,
    string Detail);

// Pure health-score result. Score is clamped to 0..100; the WPF band colors
// (>=90 green, >=70 yellow, else red) mirror the console colors.
public sealed record HealthScoreResult(
    int Score,
    IReadOnlyList<HealthCheckRow> Checks);
