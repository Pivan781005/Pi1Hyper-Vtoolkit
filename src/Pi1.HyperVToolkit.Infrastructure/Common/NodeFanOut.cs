using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Cim;

namespace Pi1.HyperVToolkit.Infrastructure.Common;

/// <summary>
/// Bounded parallel fan-out over target nodes. Successful nodes keep their rows;
/// failing nodes become classified NodeResults (Invoke-PiSafe parity).
/// Outer cancellation propagates as OperationCanceledException; per-node
/// timeouts become Timeout results.
/// </summary>
public static class NodeFanOut
{
    public static TimeSpan DefaultPerNodeTimeout { get; } = TimeSpan.FromSeconds(60);
    public static int DefaultMaxDegreeOfParallelism { get; } = 4;

    public static async Task<IReadOnlyList<NodeResult<T>>> RunEachAsync<T>(
        IReadOnlyList<string> nodes,
        Func<string, CancellationToken, Task<T>> query,
        ILogger logger,
        TimeSpan? perNodeTimeout = null,
        int? maxDegreeOfParallelism = null,
        bool hyperVNamespace = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(logger);

        using var gate = new SemaphoreSlim(maxDegreeOfParallelism ?? DefaultMaxDegreeOfParallelism);
        var tasks = nodes.Select(node => QueryOneAsync(node, query, logger, perNodeTimeout ?? DefaultPerNodeTimeout, gate, hyperVNamespace, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static async Task<NodeResult<T>> QueryOneAsync<T>(
        string node,
        Func<string, CancellationToken, Task<T>> query,
        ILogger logger,
        TimeSpan perNodeTimeout,
        SemaphoreSlim gate,
        bool hyperVNamespace,
        CancellationToken outerToken)
    {
        await gate.WaitAsync(outerToken).ConfigureAwait(false);
        try
        {
            using var nodeCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            nodeCts.CancelAfter(perNodeTimeout);
            try
            {
                var data = await query(node, nodeCts.Token).ConfigureAwait(false);
                return NodeResult<T>.Ok(node, data);
            }
            catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
            {
                logger.LogWarning("Dotaz na uzol {Node} prekročil časový limit {Timeout}.", node, perNodeTimeout);
                return NodeResult<T>.Fail(node, new NodeError(
                    NodeErrorKind.Timeout, $"Časový limit vypršal ({node}).", $"Limit: {perNodeTimeout}."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dotaz na uzol {Node} zlyhal.", node);
            return NodeResult<T>.Fail(node, NodeErrorMapper.Map(node, ex, hyperVNamespace));
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Shared WMI timeout for single CIM operations.</summary>
public static class CimTimeouts
{
    public static TimeSpan Query { get; } = TimeSpan.FromSeconds(30);
}
