using System;
using System.Threading;

namespace AlpWareStudio.Localay;

// A session owns outstanding OCR, translation and prefetch work. Leases keep its
// cancellation source alive until every asynchronous consumer has finished.
internal sealed class PipelineLifetime
{
    private readonly object _gate = new();
    private Session _current = new();

    internal sealed class Session
    {
        internal readonly CancellationTokenSource Source = new();
        internal int References = 1;
    }

    internal Lease Capture()
    {
        lock (_gate)
        {
            _current.References++;
            return new Lease(this, _current);
        }
    }

    internal void Invalidate()
    {
        Session previous;
        lock (_gate)
        {
            previous = _current;
            _current = new Session();
        }
        try { previous.Source.Cancel(); }
        finally { Release(previous); }
    }

    private void Release(Session session)
    {
        bool dispose;
        lock (_gate) { dispose = --session.References == 0; }
        if (dispose) session.Source.Dispose();
    }

    internal sealed class Lease : IDisposable
    {
        private PipelineLifetime? _owner;
        private readonly Session _session;
        internal CancellationToken Token { get; }
        internal Lease(PipelineLifetime owner, Session session)
        {
            _owner = owner;
            _session = session;
            Token = session.Source.Token;
        }
        internal bool IsCurrent
        {
            get
            {
                var owner = _owner;
                if (owner == null) return false;
                lock (owner._gate) return ReferenceEquals(owner._current, _session);
            }
        }
        internal void ThrowIfStale()
        {
            Token.ThrowIfCancellationRequested();
            if (!IsCurrent) throw new OperationCanceledException(Token);
        }
        internal bool TryApply(Action action)
        {
            var owner = _owner;
            if (owner == null) return false;
            lock (owner._gate)
            {
                if (!ReferenceEquals(owner._current, _session)) return false;
                action();
                return true;
            }
        }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_session);
    }
}
