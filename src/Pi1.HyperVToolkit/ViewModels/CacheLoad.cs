using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.ViewModels;

// Shared session-cache loading primitives. Cache HIT restores rows AND the
// warnings recorded with them (no collector call). MISS or force runs the
// loader; only SUCCESSFUL snapshots are stored — a failed collection never
// overwrites a valid entry and never touches unrelated datasets.
internal static class CacheLoad
{
    public static async Task<(List<T> Rows, List<string> Warnings)> NodeListAsync<T>(
        ISessionCache cache,
        string dataset,
        string scopeKey,
        bool force,
        Func<CancellationToken, Task<IReadOnlyList<NodeResult<IReadOnlyList<T>>>>> loader,
        CancellationToken ct)
    {
        if (!force && cache.TryGet<T>(dataset, scopeKey, out var cached))
        {
            cache.TryGetWarnings(dataset, scopeKey, out var cachedWarnings);
            return (cached.ToList(), cachedWarnings.ToList());
        }

        var results = await loader(ct).ConfigureAwait(false);
        var rows = new List<T>();
        var warnings = new List<string>();
        foreach (var result in results)
        {
            if (result.IsSuccess && result.Data is not null)
            {
                rows.AddRange(result.Data);
            }
            else if (result.Error is not null)
            {
                var warning = $"{Core.Localization.NodeErrorText.For(result.Error.Kind, result.Node)} {result.Error.Detail}".Trim();
                if (!warnings.Contains(warning))
                {
                    warnings.Add(warning);
                }
            }
        }

        cache.Set(dataset, scopeKey, (IReadOnlyList<T>)rows, warnings);
        return (rows, warnings);
    }

    public static async Task<(List<T> Rows, List<string> Warnings)> SinglesAsync<T>(
        ISessionCache cache,
        string dataset,
        string scopeKey,
        bool force,
        Func<CancellationToken, Task<IReadOnlyList<NodeResult<T>>>> loader,
        CancellationToken ct)
    {
        if (!force && cache.TryGet<T>(dataset, scopeKey, out var cached))
        {
            cache.TryGetWarnings(dataset, scopeKey, out var cachedWarnings);
            return (cached.ToList(), cachedWarnings.ToList());
        }

        var results = await loader(ct).ConfigureAwait(false);
        var rows = new List<T>();
        var warnings = new List<string>();
        foreach (var result in results)
        {
            if (result.IsSuccess && result.Data is not null)
            {
                rows.Add(result.Data);
            }
            else if (result.Error is not null)
            {
                var warning = $"{Core.Localization.NodeErrorText.For(result.Error.Kind, result.Node)} {result.Error.Detail}".Trim();
                if (!warnings.Contains(warning))
                {
                    warnings.Add(warning);
                }
            }
        }

        cache.Set(dataset, scopeKey, (IReadOnlyList<T>)rows, warnings);
        return (rows, warnings);
    }

    public static async Task<(List<T> Rows, List<string> Warnings)> LocalAsync<T>(
        ISessionCache cache,
        string dataset,
        string scopeKey,
        bool force,
        Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
        Func<Exception, string> warnText,
        CancellationToken ct)
    {
        if (!force && cache.TryGet<T>(dataset, scopeKey, out var cached))
        {
            cache.TryGetWarnings(dataset, scopeKey, out var cachedWarnings);
            return (cached.ToList(), cachedWarnings.ToList());
        }

        try
        {
            var rows = (await loader(ct).ConfigureAwait(false)).ToList();
            cache.Set(dataset, scopeKey, (IReadOnlyList<T>)rows, []);
            return (rows, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ([], [warnText(ex)]);
        }
    }
}
