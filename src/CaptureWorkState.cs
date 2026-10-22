namespace AlpWareStudio.Localay;

// The slot owns published frames. Taking a frame transfers ownership to the caller.
// No bitmap conversion or OCR is performed by the capture event producer.
internal sealed class LatestCaptureFrame<T> : IDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private T? _latest;
    private bool _disposed;

    internal void Publish(T frame)
    {
        T? previous;
        lock (_gate)
        {
            previous = _disposed ? frame : _latest;
            if (!_disposed) _latest = frame;
        }
        previous?.Dispose();
    }

    // Bound the drain by the native pool capacity, even if a producer never stops.
    internal void Drain(Func<T?> nextFrame, int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        for (int i = 0; i < capacity; i++)
        {
            var frame = nextFrame();
            if (frame == null) break;
            Publish(frame);
        }
    }

    internal T? Take()
    {
        lock (_gate)
        {
            var frame = _latest;
            _latest = null;
            return frame;
        }
    }

    internal void Clear() => Take()?.Dispose();

    public void Dispose()
    {
        T? frame;
        lock (_gate)
        {
            _disposed = true;
            frame = _latest;
            _latest = null;
        }
        frame?.Dispose();
    }
}

// A timer tick cannot start a second capture/OCR while the first still owns an image.
// Stop/restart does not forcibly unlock an uncooperative native OCR operation.
internal sealed class CaptureWorkGate
{
    private int _busy;
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;
    internal IDisposable? TryEnter() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0
        ? new Lease(this) : null;

    private sealed class Lease(CaptureWorkGate owner) : IDisposable
    {
        private CaptureWorkGate? _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null) Volatile.Write(ref owner._busy, 0);
        }
    }
}
