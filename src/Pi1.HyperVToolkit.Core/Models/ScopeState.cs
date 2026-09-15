namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>
/// Immutable snapshot of the current read scope.
/// Replaces the PowerShell pair ($Script:ScopeMode, $Script:SelectedNode)
/// with a single record so readers and writers can never disagree
/// (the cross-module $Script: state bug of v0.9.4 is intentionally not reproduced).
/// </summary>
/// <param name="Mode">Active scope mode.</param>
/// <param name="SelectedNode">
/// Node selected for <see cref="ScopeMode.Node"/>.
/// For <see cref="ScopeMode.Local"/> this is the local machine name.
/// For <see cref="ScopeMode.Cluster"/> the value is kept but unused.
/// </param>
public sealed record ScopeState(ScopeMode Mode, string SelectedNode);
