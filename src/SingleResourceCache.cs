using System;
using System.Collections.Generic;

namespace AlpWareStudio.Localay;

// Owner-serialized: callers hold their OCR/frame gate. Keep one expensive native
// resource, replacing it only after successful creation of its successor.
internal sealed class SingleResourceCache<TKey, TResource>(Action<TResource> release) : IDisposable
    where TKey : notnull where TResource : class
{
    private TKey? _key;
    private TResource? _resource;
    private bool _disposed;

    internal TResource GetOrCreate(TKey key, Func<TResource> create)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_resource != null && EqualityComparer<TKey>.Default.Equals(_key!, key)) return _resource;
        var next = create() ?? throw new InvalidOperationException("Resource creation returned null.");
        var previous = _resource;
        _resource = next;
        _key = key;
        if (previous != null && !ReferenceEquals(previous, next)) release(previous);
        return next;
    }

    internal void Clear()
    {
        var previous = _resource;
        _resource = null;
        _key = default;
        if (previous != null) release(previous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
