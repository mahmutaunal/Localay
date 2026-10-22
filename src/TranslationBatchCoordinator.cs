using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay;

// Shared by foreground and prefetch. At most one actual provider invocation runs;
// cancellation releases callers immediately but retains that slot until native/
// provider work really exits. Pending units and batch sizes are bounded.
internal sealed class TranslationBatchCoordinator
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _provider = new(1, 1);
    private readonly Dictionary<(string Context, string Text), Entry> _entries = new();
    private const int MaximumPendingUnits = 256;
    private const int BatchSize = 15;
    private long _nextId;

    private sealed class Job
    {
        internal readonly CancellationTokenSource Source = new();
        internal readonly List<Entry> Entries = new();
        internal int Waiters;
        internal bool Finished;
    }
    private sealed class Entry(Job job, string context, TranslationBlock block)
    {
        internal readonly Job Job = job;
        internal readonly string Context = context;
        internal readonly TranslationBlock Block = block;
        internal readonly TaskCompletionSource<TranslationBlockResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal async Task<IReadOnlyDictionary<string, TranslationBlockResult>> TranslateAsync(string context,
        IReadOnlyList<string> texts,
        Func<IReadOnlyList<TranslationBlock>, CancellationToken, Task<TranslationBatchResult>> execute,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var subscribed = new List<Entry>();
        Job? created = null;
        var rejected = new Dictionary<string, TranslationBlockResult>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (string text in texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal))
            {
                var key = (context, text);
                if (!_entries.TryGetValue(key, out var entry) || entry.Job.Source.IsCancellationRequested ||
                    (entry.Completion.Task.IsCompletedSuccessfully && string.IsNullOrWhiteSpace(entry.Completion.Task.Result.Text)))
                {
                    if (_entries.Count >= MaximumPendingUnits)
                    {
                        rejected[text] = new(null, new(TranslationFailureKind.InvalidResponse));
                        continue;
                    }
                    created ??= new Job();
                    entry = new(created, context, new("b" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture), text));
                    created.Entries.Add(entry);
                    _entries[key] = entry;
                }
                entry.Job.Waiters++;
                subscribed.Add(entry);
            }
        }
        if (created != null) _ = RunAsync(created, execute);
        try
        {
            // Await all promises together: a later completed unit is not hidden behind
            // sequential awaits, and cancellation cannot leave unobserved faulted tasks.
            var results = await Task.WhenAll(subscribed.Select(e => e.Completion.Task)).WaitAsync(cancellation).ConfigureAwait(false);
            for (int i = 0; i < subscribed.Count; i++) rejected[subscribed[i].Block.Text] = results[i];
            return rejected;
        }
        finally
        {
            lock (_gate)
            {
                foreach (var entry in subscribed)
                {
                    var job = entry.Job;
                    if (--job.Waiters == 0 && !job.Finished) job.Source.Cancel();
                }
            }
        }
    }

    private async Task RunAsync(Job job,
        Func<IReadOnlyList<TranslationBlock>, CancellationToken, Task<TranslationBatchResult>> execute)
    {
        bool entered = false;
        try
        {
            await _provider.WaitAsync(job.Source.Token).ConfigureAwait(false);
            entered = true;
            foreach (var chunk in job.Entries.Chunk(BatchSize))
            {
                job.Source.Token.ThrowIfCancellationRequested();
                // execute must await actual provider completion, even if it ignores cancellation.
                var reply = await execute(chunk.Select(e => e.Block).ToArray(), job.Source.Token).ConfigureAwait(false);
                job.Source.Token.ThrowIfCancellationRequested();
                foreach (var entry in chunk)
                {
                    string? value = reply.Translations.GetValueOrDefault(entry.Block.Id);
                    entry.Completion.TrySetResult(string.IsNullOrWhiteSpace(value)
                        ? new(null, reply.Failure ?? new(TranslationFailureKind.InvalidResponse)) : new(value));
                }
            }
        }
        catch (OperationCanceledException) when (job.Source.IsCancellationRequested)
        {
            foreach (var entry in job.Entries) entry.Completion.TrySetResult(new(null));
        }
        catch (Exception ex)
        {
            foreach (var entry in job.Entries) entry.Completion.TrySetResult(new(null, TranslationFailure.FromException(ex)));
        }
        finally
        {
            lock (_gate)
            {
                job.Finished = true;
                foreach (var entry in job.Entries)
                {
                    var key = (entry.Context, entry.Block.Text);
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
                }
                job.Source.Dispose();
            }
            if (entered) _provider.Release();
        }
    }
}
