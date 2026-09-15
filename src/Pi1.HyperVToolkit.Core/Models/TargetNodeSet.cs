namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Resolved query targets for the current scope, plus non-fatal warnings
/// (e.g. cluster enumeration fell back). Replaces Get-PiTargetNodes.
/// </summary>
public sealed record TargetNodeSet(IReadOnlyList<string> Nodes, IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Nodes.Count > 0;
}
