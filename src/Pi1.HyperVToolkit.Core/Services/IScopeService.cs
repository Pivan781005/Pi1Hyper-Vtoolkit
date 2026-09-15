using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Single source of truth for the read scope.
/// Replaces Get-PiScopeLabel / Get-PiTargetNodes / Show-PiScopeMenu state handling.
/// </summary>
public interface IScopeService
{
    /// <summary>Local machine name used for <see cref="ScopeMode.Local"/>.</summary>
    string MachineName { get; }

    ScopeState Current { get; }

    /// <summary>
    /// True when target-node enumeration needs a live cluster query
    /// (a later migration phase). Local/Node scopes resolve statically.
    /// </summary>
    bool ClusterEnumerationRequired { get; }

    /// <summary>Scope label. Parity with PowerShell Get-PiScopeLabel.</summary>
    string GetScopeLabel();

    /// <summary>
    /// Applies a new scope. <see cref="ScopeMode.Node"/> requires a non-empty node name.
    /// <see cref="ScopeMode.Local"/> normalizes <see cref="ScopeState.SelectedNode"/>
    /// to the local machine name (PowerShell parity).
    /// </summary>
    /// <exception cref="ArgumentException">Node mode with an empty node name.</exception>
    void SetScope(ScopeMode mode, string? selectedNode = null);

    /// <summary>
    /// Statically known target nodes without any remote query.
    /// Returns false for <see cref="ScopeMode.Cluster"/> (enumeration is a later phase).
    /// </summary>
    bool TryGetStaticTargetNodes(out IReadOnlyList<string> nodes);

    event EventHandler<ScopeState>? ScopeChanged;
}
