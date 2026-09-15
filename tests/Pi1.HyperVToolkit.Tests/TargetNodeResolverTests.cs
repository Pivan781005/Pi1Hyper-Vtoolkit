using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Scope;

namespace Pi1.HyperVToolkit.Tests;

public sealed class TargetNodeResolverTests
{
    private sealed class FakeQuerier : ICimQuerier
    {
        public Func<string, string, string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? Handler { get; set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string node, string @namespace, string wql, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(Handler?.Invoke(node, @namespace, wql) ?? []);

        public Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
            string node, string @namespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inParameters, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    private static TargetNodeResolver Create(FakeQuerier querier, CapabilitySnapshot snapshot)
    {
        var scope = new ScopeService("LOCAL");
        return new TargetNodeResolver(
            scope, snapshot, querier, NullLogger<TargetNodeResolver>.Instance);
    }

    private static CapabilitySnapshot ClusterSnapshot(bool available)
    {
        var snapshot = new CapabilitySnapshot();
        snapshot.Update(null, new CapabilityReport(CapabilityKind.FailoverCluster, available, "test"), null);
        return snapshot;
    }

    [Fact]
    public async Task Resolve_Local_ReturnsMachine()
    {
        var resolver = Create(new FakeQuerier(), ClusterSnapshot(false));
        var result = await resolver.ResolveAsync(new ScopeState(ScopeMode.Local, "LOCAL"));
        Assert.Equal(["LOCAL"], result.Nodes);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Resolve_Node_ReturnsSelected()
    {
        var resolver = Create(new FakeQuerier(), ClusterSnapshot(false));
        var result = await resolver.ResolveAsync(new ScopeState(ScopeMode.Node, "NODE7"));
        Assert.Equal(["NODE7"], result.Nodes);
    }

    [Fact]
    public async Task Resolve_NodeEmpty_FailsWithWarning()
    {
        var resolver = Create(new FakeQuerier(), ClusterSnapshot(false));
        var result = await resolver.ResolveAsync(new ScopeState(ScopeMode.Node, ""));
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task Resolve_ClusterUnavailable_FailsWithoutQuery()
    {
        var querier = new FakeQuerier
        {
            Handler = (_, _, _) => throw new InvalidOperationException("must not be called"),
        };
        var resolver = Create(querier, ClusterSnapshot(false));
        var result = await resolver.ResolveAsync(new ScopeState(ScopeMode.Cluster, "LOCAL"));
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task Resolve_ClusterAvailable_EnumeratesNodes()
    {
        var querier = new FakeQuerier
        {
            Handler = (_, ns, _) =>
            {
                Assert.Equal(@"root\MSCluster", ns);
                return new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["Name"] = "NODE2" },
                    new Dictionary<string, object?> { ["Name"] = "NODE1" },
                };
            },
        };
        var resolver = Create(querier, ClusterSnapshot(true));
        var result = await resolver.ResolveAsync(new ScopeState(ScopeMode.Cluster, "LOCAL"));
        Assert.Equal(["NODE1", "NODE2"], result.Nodes);
    }
}
