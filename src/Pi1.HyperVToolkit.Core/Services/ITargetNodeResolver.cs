using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Single scope-resolution mechanism shared by all views (replaces
/// Get-PiTargetNodes; no per-ViewModel duplication).
/// Local resolves statically; Node resolves to the selected node;
/// Cluster enumerates cluster nodes when the capability is present.
/// </summary>
public interface ITargetNodeResolver
{
    Task<TargetNodeSet> ResolveAsync(ScopeState scope, CancellationToken cancellationToken = default);
}
