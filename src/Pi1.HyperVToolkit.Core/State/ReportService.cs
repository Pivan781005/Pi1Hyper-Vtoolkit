using System.Collections;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.State;

/// <summary>Single in-memory owner of the current result set (LastResult parity, bug fixed).</summary>
public sealed class ReportService : IReportService
{
    private readonly object _sync = new();
    private IEnumerable? _rows;
    private string _name = string.Empty;
    private Type? _rowType;
    private int _count;

    public string CurrentName
    {
        get { lock (_sync) { return _name; } }
    }

    public int Count
    {
        get { lock (_sync) { return _count; } }
    }

    public Type? RowType
    {
        get { lock (_sync) { return _rowType; } }
    }

    public IEnumerable? CurrentRows
    {
        get { lock (_sync) { return _rows; } }
    }

    public void SetCurrent<T>(IReadOnlyList<T> rows, string name)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            _rows = rows;
            _name = name;
            _rowType = typeof(T);
            _count = rows.Count;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<T> GetRows<T>()
    {
        lock (_sync)
        {
            if (_rows is IReadOnlyList<T> typed)
            {
                return typed;
            }

            return [];
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _rows = null;
            _name = string.Empty;
            _rowType = null;
            _count = 0;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CurrentChanged;
}
