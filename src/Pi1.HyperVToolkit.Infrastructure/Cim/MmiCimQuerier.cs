using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace Pi1.HyperVToolkit.Infrastructure.Cim;

/// <summary>
/// Native MMI querier. One cached CimSession per node (WSMan, reused across
/// queries and refreshes); a failed session is evicted so the next query
/// reconnects. Queries use the true-async IObservable surface.
/// </summary>
public sealed class MmiCimQuerier : ICimQuerier, IDisposable
{
    private readonly ConcurrentDictionary<string, CimSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MmiCimQuerier> _logger;
    private bool _disposed;

    public MmiCimQuerier(ILogger<MmiCimQuerier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string node,
        string @namespace,
        string wql,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        cancellationToken.ThrowIfCancellationRequested();

        // A single immediate retry on transient CIM faults: parallel dashboard
        // fan-outs share cached sessions, and one evicted/busy session must
        // not turn a healthy-empty result (e.g. zero Storage Jobs) into a
        // failure banner. Persistent faults still propagate to the caller.
        var attempt = 0;
        while (true)
        {
            attempt++;
            var session = GetOrCreateSession(node);
            var options = new CimOperationOptions { Timeout = timeout };

            try
            {
                var observable = session.QueryInstancesAsync(@namespace, "WQL", wql, options);
                var instances = await ObserveAsync(observable, cancellationToken).ConfigureAwait(false);
                return instances.Select(ToDictionary).ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsSessionFault(ex) || ex is ObjectDisposedException)
            {
                _logger.LogDebug(ex, "CIM session k {Node} je chybná, odstraňujem z cache.", node);
                EvictSession(node);
                if (CimRetryPolicy.ShouldRetry(ex, attempt) && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Opakujem CIM dotaz na {Node} po prechodnej chybe.", node);
                    continue;
                }

                throw;
            }
        }
    }

    private CimSession GetOrCreateSession(string node)
    {
        return _sessions.GetOrAdd(node, static name =>
        {
            return IsLocalName(name)
                ? CimSession.Create(null)
                : CimSession.Create(name);
        });
    }

    private static bool IsLocalName(string node) =>
        node is "." or "localhost" ||
        string.Equals(node, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static async Task<List<CimInstance>> ObserveAsync(
        IObservable<CimInstance> observable, CancellationToken cancellationToken)
    {
        var observer = new CimCollectObserver();
        var subscription = observable.Subscribe(observer);
        observer.Attach(subscription);
        using (cancellationToken.Register(subscription.Dispose))
        {
            return await observer.Task.ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(CimInstance instance)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // Class identity for result filtering (e.g. Ethernet setting subclasses).
            ["__Class"] = instance.CimSystemProperties.ClassName,
        };
        foreach (var property in instance.CimInstanceProperties)
        {
            dict[property.Name] = property.Value;
        }

        return dict;
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeSingletonMethodAsync(
        string node,
        string @namespace,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?> inParameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        cancellationToken.ThrowIfCancellationRequested();

        var session = GetOrCreateSession(node);
        try
        {
            // MMI method invocation is synchronous; offload so collectors stay async.
            return await Task.Run(() =>
            {
                var instances = session.QueryInstances(@namespace, "WQL", $"SELECT * FROM {className}");
                var target = instances.FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"Trieda {className} nemá na uzle {node} žiadnu inštanciu.");
                var inParams = new CimMethodParametersCollection();
                foreach (var (name, value) in inParameters)
                {
                    inParams.Add(CimMethodParameter.Create(
                        name, value, InferCimType(value), CimFlags.In));
                }

                var options = new CimOperationOptions { Timeout = timeout };
                var result = session.InvokeMethod(@namespace, target, methodName, inParams, options);
                var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ReturnValue"] = result.ReturnValue?.Value,
                };
                foreach (var outParam in result.OutParameters)
                {
                    outputs[outParam.Name] = outParam.Value;
                }

                return (IReadOnlyDictionary<string, object?>)outputs;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSessionFault(ex))
        {
            _logger.LogDebug(ex, "CIM session k {Node} je chybná, odstraňujem z cache.", node);
            EvictSession(node);
            throw;
        }
    }

    private static CimType InferCimType(object? value) => value switch
    {
        string => CimType.String,
        bool => CimType.Boolean,
        byte or sbyte or short or ushort or int or uint => CimType.UInt32,
        long or ulong => CimType.UInt64,
        _ => CimType.String,
    };

    private static bool IsSessionFault(Exception ex) => ex is CimException;

    private void EvictSession(string node)
    {
        if (_sessions.TryRemove(node, out var session))
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
                // Best effort cleanup of a broken session.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var node in _sessions.Keys.ToList())
        {
            EvictSession(node);
        }
    }
}
