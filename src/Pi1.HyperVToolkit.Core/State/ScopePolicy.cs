using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

/// <summary>
/// Pure capability gate for scope requests. No I/O, fully unit-testable.
/// </summary>
public sealed class ScopePolicy : IScopePolicy
{
    // Legacy fixed-Slovak texts, kept for compatibility. New code reads the
    // localized Scope_ClusterUnavailable / Scope_NodeNameRequired resources,
    // so English UI no longer surfaces Slovak validation text.
    public const string ClusterUnavailableMessage =
        "Klastrový scope nie je dostupný, pretože chýbajú nástroje Failover Cluster. Scope zostáva nastavený na lokálny uzol.";

    public const string NodeNameRequiredMessage = "Pre režim Vybraný uzol zadaj názov uzla.";

    private readonly string _machineName;

    public ScopePolicy(string? machineName = null)
    {
        _machineName = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName;
    }

    public ScopeDecision Evaluate(ScopeMode requestedMode, string? requestedNode, bool clusterAvailable)
    {
        switch (requestedMode)
        {
            case ScopeMode.Local:
                return new ScopeDecision(true, new ScopeState(ScopeMode.Local, _machineName), null, false);

            case ScopeMode.Cluster:
                if (!clusterAvailable)
                {
                    return new ScopeDecision(
                        true,
                        new ScopeState(ScopeMode.Local, _machineName),
                        LocalizationService.Instance["Scope_ClusterUnavailable"],
                        true);
                }

                return new ScopeDecision(true, new ScopeState(ScopeMode.Cluster, _machineName), null, false);

            case ScopeMode.Node:
                if (string.IsNullOrWhiteSpace(requestedNode))
                {
                    return new ScopeDecision(false, null, LocalizationService.Instance["Scope_NodeNameRequired"], false);
                }

                return new ScopeDecision(true, new ScopeState(ScopeMode.Node, requestedNode.Trim()), null, false);

            default:
                return new ScopeDecision(true, new ScopeState(ScopeMode.Local, _machineName), null, false);
        }
    }
}
