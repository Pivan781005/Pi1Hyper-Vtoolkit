using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Outcome of a scope request after capability gating.
/// </summary>
/// <param name="IsValid">False when the request is unusable (e.g. Node mode without a node name).</param>
/// <param name="Allowed">Scope to activate and persist. Null when <see cref="IsValid"/> is false.</param>
/// <param name="Message">Slovak user-facing message. Set on fallback and on invalid requests.</param>
/// <param name="FellBack">True when the request was replaced (Cluster without capability).</param>
public sealed record ScopeDecision(bool IsValid, ScopeState? Allowed, string? Message, bool FellBack);

/// <summary>
/// Capability gate for scope requests. Parity with the PowerShell rule:
/// Cluster scope stays inactive (falls back to Local with a warning) while
/// the Failover Cluster capability is unavailable.
/// </summary>
public interface IScopePolicy
{
    ScopeDecision Evaluate(ScopeMode requestedMode, string? requestedNode, bool clusterAvailable);
}
