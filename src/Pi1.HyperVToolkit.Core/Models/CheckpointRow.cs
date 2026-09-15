namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>One checkpoint row. Parity with Show-PiCheckpoints columns.</summary>
public sealed record CheckpointRow(
    string HostNode,
    string VM,
    string Name,
    DateTime? Created,
    string Type);
