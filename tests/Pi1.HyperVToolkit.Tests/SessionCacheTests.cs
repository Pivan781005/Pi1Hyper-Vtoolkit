using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

public sealed class SessionCacheTests
{
    [Fact]
    public void Miss_ReturnsFalse()
    {
        var cache = new SessionCache();
        Assert.False(cache.TryGet<string>("D", "K", out _));
        Assert.False(cache.TryGetWarnings("D", "K", out _));
    }

    [Fact]
    public void SetThenGet_RoundTripsRowsAndWarnings()
    {
        var cache = new SessionCache();
        cache.Set("D", "K", (IReadOnlyList<string>)["a", "b"], ["w1"]);
        Assert.True(cache.TryGet<string>("D", "K", out var rows));
        Assert.Equal(["a", "b"], rows);
        Assert.True(cache.TryGetWarnings("D", "K", out var warnings));
        Assert.Equal(["w1"], warnings);
    }

    [Fact]
    public void ScopeKey_IsolatesScopes()
    {
        var cache = new SessionCache();
        var local = SessionCacheKeys.ScopeKey("Local", ["N1"]);
        var node = SessionCacheKeys.ScopeKey("Node", ["N1"]);
        var cluster = SessionCacheKeys.ScopeKey("Cluster", ["N1", "N2"]);
        Assert.NotEqual(local, node);
        Assert.NotEqual(local, cluster);
        cache.Set("D", local, (IReadOnlyList<string>)["x"]);
        Assert.False(cache.TryGet<string>("D", node, out _));
        Assert.False(cache.TryGet<string>("D", cluster, out _));
        Assert.True(cache.TryGet<string>("D", local, out _));
    }

    [Fact]
    public void ScopeKey_NodeOrderIndependent()
    {
        Assert.Equal(
            SessionCacheKeys.ScopeKey("Cluster", ["N2", "N1"]),
            SessionCacheKeys.ScopeKey("Cluster", ["N1", "N2"]));
    }

    [Fact]
    public void Invalidate_DatasetOnly()
    {
        var cache = new SessionCache();
        cache.Set("A", "K", (IReadOnlyList<string>)["a"]);
        cache.Set("B", "K", (IReadOnlyList<string>)["b"]);
        cache.Invalidate(dataset: "A");
        Assert.False(cache.TryGet<string>("A", "K", out _));
        Assert.True(cache.TryGet<string>("B", "K", out _));
    }

    [Fact]
    public void Invalidate_ScopeOnly()
    {
        var cache = new SessionCache();
        cache.Set("A", "K1", (IReadOnlyList<string>)["a"]);
        cache.Set("A", "K2", (IReadOnlyList<string>)["b"]);
        cache.Invalidate(scopeKey: "K1");
        Assert.False(cache.TryGet<string>("A", "K1", out _));
        Assert.True(cache.TryGet<string>("A", "K2", out _));
    }

    [Fact]
    public void Invalidate_All()
    {
        var cache = new SessionCache();
        cache.Set("A", "K", (IReadOnlyList<string>)["a"]);
        cache.Invalidate();
        Assert.False(cache.TryGet<string>("A", "K", out _));
    }

    [Fact]
    public void Set_OverwritesPreviousSnapshot()
    {
        var cache = new SessionCache();
        cache.Set("A", "K", (IReadOnlyList<string>)["old"], ["w"]);
        cache.Set("A", "K", (IReadOnlyList<string>)["new"]);
        Assert.True(cache.TryGet<string>("A", "K", out var rows));
        Assert.Equal(["new"], rows);
        Assert.True(cache.TryGetWarnings("A", "K", out var warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void TypeMismatch_DoesNotLeak()
    {
        var cache = new SessionCache();
        cache.Set("A", "K", (IReadOnlyList<string>)["x"]);
        Assert.False(cache.TryGet<int>("A", "K", out _));
    }
}
