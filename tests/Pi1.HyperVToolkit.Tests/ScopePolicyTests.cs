using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

/// <summary>
/// Rules for the Phase 2 correction: Cluster scope must never become active
/// while the Failover Cluster capability is unavailable (PowerShell parity:
/// Test-PiClusterModule gate with fallback to Local).
/// </summary>
public sealed class ScopePolicyTests
{
    [Fact]
    public void Evaluate_Local_AlwaysAllowed()
    {
        var policy = new ScopePolicy("TESTNODE");
        var decision = policy.Evaluate(ScopeMode.Local, "ANYTHING", clusterAvailable: false);
        Assert.True(decision.IsValid);
        Assert.False(decision.FellBack);
        Assert.Equal(new ScopeState(ScopeMode.Local, "TESTNODE"), decision.Allowed);
    }

    [Fact]
    public void Evaluate_ClusterAvailable_Allowed()
    {
        var policy = new ScopePolicy("TESTNODE");
        var decision = policy.Evaluate(ScopeMode.Cluster, null, clusterAvailable: true);
        Assert.True(decision.IsValid);
        Assert.False(decision.FellBack);
        Assert.Equal(ScopeMode.Cluster, decision.Allowed?.Mode);
        Assert.Null(decision.Message);
    }

    [Fact]
    public void Evaluate_ClusterUnavailable_FallsBackToLocal()
    {
        var policy = new ScopePolicy("TESTNODE");
        var decision = policy.Evaluate(ScopeMode.Cluster, "TESTNODE", clusterAvailable: false);
        Assert.True(decision.IsValid);
        Assert.True(decision.FellBack);
        Assert.Equal(new ScopeState(ScopeMode.Local, "TESTNODE"), decision.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(decision.Message));
    }

    [Fact]
    public void Evaluate_Node_RequiresName()
    {
        var policy = new ScopePolicy("TESTNODE");
        foreach (var bad in new string?[] { null, "", "   " })
        {
            var decision = policy.Evaluate(ScopeMode.Node, bad, clusterAvailable: true);
            Assert.False(decision.IsValid);
            Assert.Null(decision.Allowed);
            Assert.False(string.IsNullOrWhiteSpace(decision.Message));
        }
    }

    [Fact]
    public void Evaluate_Node_TrimsName()
    {
        var policy = new ScopePolicy("TESTNODE");
        var decision = policy.Evaluate(ScopeMode.Node, "  NODE2  ", clusterAvailable: false);
        Assert.True(decision.IsValid);
        Assert.Equal(new ScopeState(ScopeMode.Node, "NODE2"), decision.Allowed);
    }
}
