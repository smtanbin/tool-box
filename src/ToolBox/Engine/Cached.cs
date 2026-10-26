namespace ToolBox.Engine;

/// <summary>
/// A value that is recomputed at most once per <c>maxAge</c>, by one background task at a time.
/// Callers never wait on a lock while a slow or hung system call runs: they wait for a bounded time and then
/// get the previous value (or, for <see cref="GetRequired"/>, a timeout error). This keeps one stuck driver
/// (a sleeping disk, a hung WMI provider) from freezing every page that polls the engine.
/// </summary>
public sealed class Cached<T> where T : class
{
    readonly Func<T> _load;
    readonly TimeSpan _maxAge;
    readonly object _gate = new();
    T? _value;
    DateTime _at;
    Task<T>? _running;

    public Cached(Func<T> load, TimeSpan maxAge) { _load = load; _maxAge = maxAge; }

    /// <summary>Forgets the stored value so the next call reloads it.</summary>
    public void Invalidate() { lock (_gate) _at = DateTime.MinValue; }

    /// <summary>The fresh value, or the stale one if the reload is not done within <paramref name="wait"/>; null if there is neither.</summary>
    public T? Get(TimeSpan wait)
    {
        var t = Start();
        if (t == null) { lock (_gate) return _value; }
        try { if (t.Wait(wait)) return t.Result; }
        catch (AggregateException) { }
        lock (_gate) return _value;
    }

    /// <summary>Like <see cref="Get"/> but never returns null: a failed or timed-out first load throws.</summary>
    public T GetRequired(TimeSpan wait)
    {
        var t = Start();
        if (t == null) { lock (_gate) return _value!; }
        try { if (t.Wait(wait)) return t.Result; }
        catch (AggregateException e) { throw e.InnerException ?? e; }
        lock (_gate) return _value ?? throw new TimeoutException("Windows took too long to answer.");
    }

    // null = the stored value is still fresh
    Task<T>? Start()
    {
        lock (_gate)
        {
            if (_value != null && DateTime.UtcNow - _at < _maxAge) return null;
            return _running ??= Task.Factory.StartNew(() =>
            {
                try
                {
                    var v = _load();
                    lock (_gate) { _value = v; _at = DateTime.UtcNow; }
                    return v;
                }
                finally { lock (_gate) _running = null; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
