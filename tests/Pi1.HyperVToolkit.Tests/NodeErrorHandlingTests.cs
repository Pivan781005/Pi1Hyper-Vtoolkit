using Microsoft.Extensions.Logging.Abstractions;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Common;

namespace Pi1.HyperVToolkit.Tests;

public sealed class NodeErrorMapperTests
{
    [Fact]
    public void Map_AccessDenied()
    {
        var error = NodeErrorMapper.Map("N1", new UnauthorizedAccessException("Access is denied."));
        Assert.Equal(NodeErrorKind.AccessDenied, error.Kind);
    }

    [Fact]
    public void Map_Unreachable_RpcServer()
    {
        var error = NodeErrorMapper.Map("N1", new InvalidOperationException("The RPC server is unavailable. (0x800706BA)"));
        Assert.Equal(NodeErrorKind.HostUnreachable, error.Kind);
    }

    [Fact]
    public void Map_AccessDenied_ByHResultText()
    {
        var error = NodeErrorMapper.Map("N1", new InvalidOperationException("General access denied error (0x80070005)"));
        Assert.Equal(NodeErrorKind.AccessDenied, error.Kind);
    }

    [Fact]
    public void Map_HyperVUnavailable_InvalidNamespace()
    {
        var error = NodeErrorMapper.Map("N1", new InvalidOperationException("Invalid namespace (0x8004100E)"), hyperVNamespace: true);
        Assert.Equal(NodeErrorKind.HyperVUnavailable, error.Kind);
    }

    [Fact]
    public void Map_Timeout_ByText()
    {
        var error = NodeErrorMapper.Map("N1", new InvalidOperationException("The operation timed out."));
        Assert.Equal(NodeErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public void Map_TimeoutException()
    {
        var error = NodeErrorMapper.Map("N1", new TimeoutException("WSMan timeout."));
        Assert.Equal(NodeErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public void Map_Unknown_ByDefault()
    {
        var error = NodeErrorMapper.Map("N1", new InvalidOperationException("Something odd."));
        Assert.Equal(NodeErrorKind.Unknown, error.Kind);
    }
}

public sealed class NodeFanOutTests
{
    [Fact]
    public async Task RunEach_PartialFailure_KeepsSuccessfulNodes()
    {
        var results = await NodeFanOut.RunEachAsync(
            ["N1", "N2", "N3"],
            (node, ct) => node == "N2"
                ? Task.FromException<string>(new UnauthorizedAccessException("denied"))
                : Task.FromResult(node + "-data"),
            NullLogger.Instance);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsSuccess);
        Assert.False(results[1].IsSuccess);
        Assert.Equal(NodeErrorKind.AccessDenied, results[1].Error?.Kind);
        Assert.True(results[2].IsSuccess);
    }

    [Fact]
    public async Task RunEach_SlowNode_BecomesTimeout()
    {
        var results = await NodeFanOut.RunEachAsync(
            ["SLOW"],
            async (node, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return "late";
            },
            NullLogger.Instance,
            perNodeTimeout: TimeSpan.FromMilliseconds(100));

        var single = Assert.Single(results);
        Assert.False(single.IsSuccess);
        Assert.Equal(NodeErrorKind.Timeout, single.Error?.Kind);
    }

    [Fact]
    public async Task RunEach_OuterCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NodeFanOut.RunEachAsync<string>(
                ["N1"],
                async (node, ct) =>
                {
                    await Task.Delay(10, ct);
                    return "x";
                },
                NullLogger.Instance,
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunEach_BoundedConcurrency_Respected()
    {
        var current = 0;
        var peak = 0;
        var gate = new object();
        async Task<string> Query(string node, CancellationToken ct)
        {
            lock (gate)
            {
                current++;
                peak = Math.Max(peak, current);
            }

            try
            {
                await Task.Delay(50, ct);
                return node;
            }
            finally
            {
                lock (gate)
                {
                    current--;
                }
            }
        }

        await NodeFanOut.RunEachAsync(
            ["A", "B", "C", "D", "E", "F"], Query, NullLogger.Instance, maxDegreeOfParallelism: 2);
        Assert.True(peak <= 2, $"Peak concurrency was {peak}, expected <= 2.");
    }
}
