using Microsoft.Management.Infrastructure;

namespace Pi1.HyperVToolkit.Infrastructure.Cim;

/// <summary>
/// Bridges MMI's genuinely-async IObservable query surface to Task without
/// Rx and without Task.Run offload: the WS-Man/DCOM async I/O stays async.
/// </summary>
internal sealed class CimCollectObserver : IObserver<CimInstance>
{
    private readonly List<CimInstance> _items = new();
    private readonly TaskCompletionSource<List<CimInstance>> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();
    private IDisposable? _subscription;

    public void Attach(IDisposable subscription) => _subscription = subscription;

    public void OnNext(CimInstance value)
    {
        lock (_sync)
        {
            _items.Add(value);
        }
    }

    public void OnError(Exception error)
    {
        _tcs.TrySetException(error);
        _subscription?.Dispose();
    }

    public void OnCompleted()
    {
        lock (_sync)
        {
            _tcs.TrySetResult(new List<CimInstance>(_items));
        }

        _subscription?.Dispose();
    }

    public Task<List<CimInstance>> Task => _tcs.Task;
}
