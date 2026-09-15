using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

// Thread-safe in-memory snapshot store. Entries are immutable row lists;
// readers and writers can never disagree mid-refresh.
public sealed class SessionCache : ISessionCache
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Dataset, string ScopeKey), Entry> _entries = new();

    private sealed record Entry(object Rows, List<string> Warnings);

    public bool TryGet<T>(string dataset, string scopeKey, out IReadOnlyList<T> rows)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue((dataset, scopeKey), out var entry) &&
                entry.Rows is IReadOnlyList<T> typed)
            {
                rows = typed;
                return true;
            }
        }

        rows = [];
        return false;
    }

    public bool TryGetWarnings(string dataset, string scopeKey, out IReadOnlyList<string> warnings)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue((dataset, scopeKey), out var entry))
            {
                warnings = entry.Warnings;
                return true;
            }
        }

        warnings = [];
        return false;
    }

    public void Set<T>(string dataset, string scopeKey, IReadOnlyList<T> rows, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        lock (_sync)
        {
            _entries[(dataset, scopeKey)] = new Entry(rows, warnings?.ToList() ?? []);
        }
    }

    public void Invalidate(string? dataset = null, string? scopeKey = null)
    {
        lock (_sync)
        {
            var doomed = _entries.Keys
                .Where(k =>
                    (dataset is null || string.Equals(k.Dataset, dataset, StringComparison.Ordinal)) &&
                    (scopeKey is null || string.Equals(k.ScopeKey, scopeKey, StringComparison.Ordinal)))
                .ToList();
            foreach (var key in doomed)
            {
                _entries.Remove(key);
            }
        }
    }
}
