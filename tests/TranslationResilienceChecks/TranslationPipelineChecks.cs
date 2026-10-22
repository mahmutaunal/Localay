using System.Net;
using System.Text.Json;

namespace AlpWareStudio.Localay;

// Shared portable regressions: the existing LifetimeChecks workflow step runs these
// too, without adding CI steps, network access, packages or platform dependencies.
internal static class TranslationPipelineChecks
{
    internal static Task RunAsync(Action<bool, string> Check) => RunCoreAsync(Check).WaitAsync(TimeSpan.FromSeconds(15));

    private static async Task RunCoreAsync(Action<bool, string> Check)
    {
        var cache = TranslationCache.Instance;
        var requested = new[] { new TranslationBlock("a", "Play"), new TranslationBlock("b", "Settings") };
        Dictionary<string, string> ParseBlocks(string json) => TranslationBlockProtocol.Parse(json, false, requested);
        var idReply = ParseBlocks("""{"text_blocks":[{"id":"b","text":"Ayarlar"},{"id":"a","text":"Oyna"}]}""");
        Check(idReply.Count == 2 && idReply["a"] == "Oyna" && idReply["b"] == "Ayarlar", "response reordering preserves explicit block identity");
        Check(ParseBlocks("""{"text_blocks":[{"id":"a","text":"Oyna"}]}""").Count == 1,
            "missing IDs stay missing without shifting a neighboring translation");
        Check(ParseBlocks("""{"text_blocks":[{"id":"a","text":"Oyna"},{"id":"a","text":"Yanlış"}]}""").Count == 0,
            "duplicate IDs reject an ambiguous response atomically");
        Check(ParseBlocks("""{"text_blocks":[{"id":"a","text":"Oyna"},{"id":"999","text":"Yanlış"}]}""").Count == 0,
            "an unrequested combined ID cannot target the active scene");
        Check(ParseBlocks("""{"text_blocks":[{"id":0,"text":"Yanlış"}]}""").Count == 0, "numeric IDs cannot become list indexes");
        Check(TranslationBlockProtocol.Parse("""{"translations":[{"id":"a","original_text":"Stop","translated_text":"Oyna"}]}""", true, requested).Count == 0,
            "a direct adapter cannot change the source ownership of a requested ID");
        Check(ParseBlocks("""{"text_blocks":[{"id":"a","text":" "},{"id":"b","text":"Ayarlar"}]}""").Count == 1,
            "blank translations remain failures while other explicit IDs succeed");
        Check(ParseBlocks("""{"text_blocks":[{"id":"a","text":"Play"}]}""")["a"] == "Play", "an unchanged proper name is still a valid ID response");
        Check(ParseBlocks("{broken").Count == 0 && ParseBlocks("[]").Count == 0,
            "malformed response structures cannot assign translations");
        var literal = new[] { new TranslationBlock("literal", "a##|||##b\nsecond line") };
        using var literalRequest = JsonDocument.Parse(JsonSerializer.Serialize(new { block_protocol = TranslationBlockProtocol.Version,
            text_blocks = literal.Select(b => new { id = b.Id, text = b.Text }) }));
        Check(TranslationBlockProtocol.IsEnabled(literalRequest.RootElement) &&
            TranslationBlockProtocol.ReadBlocks(literalRequest.RootElement)[0].Text == literal[0].Text,
            "ID protocol preserves literal legacy separators and multiline source text as one block");
        Check(TranslationBlockProtocol.DirectResponse(requested, new[] { "Oyna" }) == null &&
            TranslationBlockProtocol.DirectResponse(requested, new[] { "Oyna", "Ayarlar", "extra" }) == null,
            "direct adapters reject both too few and too many API results");
        var directReply = TranslationBlockProtocol.DirectResponse(requested, new[] { "Oyna", "" });
        Check(TranslationBlockProtocol.Parse(directReply, true, requested).Count == 1,
            "a direct provider retains empty positions internally but exports only valid explicit assignments");
        cache.UseContext("new-context"); cache.Set("Play", "Yeni");
        Check(!cache.TryGet("old-context", "Play", out _) && cache.TryGet("new-context", "Play", out _),
            "context comparison and cache lookup are atomic");

        var coordinator = new TranslationBatchCoordinator();
        var enteredBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new List<string[]>();
        async Task<TranslationBatchResult> TranslateBatch(IReadOnlyList<TranslationBlock> blocks, CancellationToken token)
        {
            batches.Add(blocks.Select(b => b.Text).ToArray());
            enteredBatch.TrySetResult();
            await releaseBatch.Task.WaitAsync(token);
            return new(blocks.ToDictionary(b => b.Id, b => "tr:" + b.Text));
        }
        var prefetch = coordinator.TranslateAsync("ctx", new[] { "Play", "Settings" }, TranslateBatch, default);
        await enteredBatch.Task;
        var foreground = coordinator.TranslateAsync("ctx", new[] { "Settings", "New", "Play", "Play" }, TranslateBatch, default);
        releaseBatch.SetResult();
        var prefetched = await prefetch;
        var displayed = await foreground;
        Check(batches.Count == 2 && batches[0].SequenceEqual(new[] { "Play", "Settings" }) && batches[1].SequenceEqual(new[] { "New" }),
            "partial overlapping foreground joins prefetch and sends only the new block");
        Check(displayed.Count == 3 && displayed["Play"].Text == "tr:Play" && displayed["New"].Text == "tr:New",
            "duplicates and OCR reordering preserve source ownership through a shared flight");

        cache.UseContext("twenty");
        for (int i = 0; i < 19; i++) cache.SetIfContext("twenty", "block" + i, "tr" + i);
        var twenty = Enumerable.Range(0, 20).Select(i => "block" + i).ToArray();
        var missingTwenty = twenty.Where(t => !cache.TryGet("twenty", t, out _)).ToArray();
        int sentTwenty = 0;
        await coordinator.TranslateAsync("twenty", missingTwenty, (blocks, _) =>
        {
            sentTwenty += blocks.Count;
            return Task.FromResult(new TranslationBatchResult(blocks.ToDictionary(b => b.Id, b => "tr:" + b.Text)));
        }, default);
        Check(sentTwenty == 1 && missingTwenty[0] == "block19", "nineteen retained blocks require only one new translation unit");
        int maximumBatch = 0, batchCount = 0;
        await coordinator.TranslateAsync("large", Enumerable.Range(0, 41).Select(i => i.ToString()).ToArray(), (blocks, _) =>
        {
            maximumBatch = Math.Max(maximumBatch, blocks.Count); batchCount++;
            return Task.FromResult(new TranslationBatchResult(blocks.ToDictionary(b => b.Id, b => "ok")));
        }, default);
        Check(maximumBatch == 15 && batchCount == 3, "large pages use bounded sequential batches rather than unlimited provider parallelism");

        var canceledOwner = new CancellationTokenSource();
        var joinedOwner = new CancellationTokenSource();
        var sharedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sharedCalls = 0;
        async Task<TranslationBatchResult> Shared(IReadOnlyList<TranslationBlock> blocks, CancellationToken token)
        {
            sharedCalls++; sharedStarted.TrySetResult();
            await sharedRelease.Task.WaitAsync(token);
            return new(blocks.ToDictionary(b => b.Id, b => "shared"));
        }
        var ownerA = coordinator.TranslateAsync("shared", new[] { "same" }, Shared, canceledOwner.Token);
        await sharedStarted.Task;
        var ownerB = coordinator.TranslateAsync("shared", new[] { "same" }, Shared, joinedOwner.Token);
        canceledOwner.Cancel();
        try { await ownerA; throw new Exception("caller cancellation ignored"); } catch (OperationCanceledException) { }
        sharedRelease.SetResult();
        Check((await ownerB)["same"].Text == "shared" && sharedCalls == 1,
            "canceling prefetch cannot cancel the same unit still needed by foreground");
        canceledOwner.Dispose(); joinedOwner.Dispose();

        var stubbornStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stubbornRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopStubborn = new CancellationTokenSource();
        var stubborn = coordinator.TranslateAsync("old-session", new[] { "old" }, async (blocks, _) =>
        {
            stubbornStarted.SetResult(); await stubbornRelease.Task; // Deliberately ignores cancellation.
            return new(blocks.ToDictionary(b => b.Id, b => "old reply"));
        }, stopStubborn.Token);
        await stubbornStarted.Task; stopStubborn.Cancel();
        try { await stubborn; throw new Exception("stubborn caller cancellation ignored"); } catch (OperationCanceledException) { }
        var newSession = coordinator.TranslateAsync("new-session", new[] { "new" }, (blocks, _) =>
        {
            nextStarted.SetResult(); return Task.FromResult(new TranslationBatchResult(blocks.ToDictionary(b => b.Id, b => "new reply")));
        }, default);
        Check(!nextStarted.Task.IsCompleted, "an uncooperative canceled provider retains the actual provider slot");
        stubbornRelease.SetResult();
        Check((await newSession)["new"].Text == "new reply", "the newest work starts after actual provider exit without accepting the old result");

        int failuresSent = 0;
        async Task<TranslationBatchResult> Failing(IReadOnlyList<TranslationBlock> blocks, CancellationToken _)
        {
            failuresSent++; await Task.Yield(); return new(new Dictionary<string, string>(), new(TranslationFailureKind.RateLimited));
        }
        var failedBlock = await coordinator.TranslateAsync("failure", new[] { "retry" }, Failing, default);
        var retriedBlock = await coordinator.TranslateAsync("failure", new[] { "retry" }, Failing, default);
        Check(failedBlock["retry"].Failure?.Kind == TranslationFailureKind.RateLimited && failuresSent == 2,
            "failure promises are released so the existing retry policy can make a later attempt");

        // Fake time exercises real Google dispatch without network or real one-second sleeps.
        var clock = new ManualTime();
        int googleDispatches = 0;
        using (var client = new HttpClient(new ReplyHandler(() =>
        {
            googleDispatches++; return new(HttpStatusCode.OK) { Content = new StringContent("[[[\"Oyna\"]]]") };
        })))
        {
            var free = new GoogleTranslateFreeClient(client, clock);
            await free.TranslateAsync("first", "en", "tr", default);
            using var cancelQueued = new CancellationTokenSource();
            var canceledQueued = Enumerable.Range(0, 30).Select(i => free.TranslateAsync("old" + i, "en", "tr", cancelQueued.Token)).ToArray();
            cancelQueued.Cancel(); await Task.WhenAll(canceledQueued);
            var latestRequest = free.TranslateAsync("latest", "en", "tr", default);
            Check(googleDispatches == 1 && clock.NextDelay == TimeSpan.FromSeconds(1),
                "thirty canceled Google waiters reserve no future dispatch slots");
            clock.Advance(TimeSpan.FromSeconds(1));
            Check(await latestRequest == "Oyna" && googleDispatches == 2,
                "the latest Google block retains the original one-second rate limit without canceled queue debt");
        }

        Check(TranslationBlockProtocol.CacheContext("settings", new[] { "context A" }) !=
            TranslationBlockProtocol.CacheContext("settings", new[] { "context B" }),
            "meaning context changes cannot reuse an unrelated translation flight or cache entry");
        Check(TranslationBlockProtocol.CacheContext("a\u001fb", new[] { "c" }) !=
            TranslationBlockProtocol.CacheContext("a", new[] { "b\u001fc" }),
            "serialized context keys do not collide on delimiter characters inside user text");

        using var physicalCancellation = new CancellationTokenSource();
        var physicalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalCall = TranslationRequestContext.RunAsync(new WorkService(async _ =>
        {
            physicalEntered.SetResult(); await physicalRelease.Task; return "late";
        }), "", "", physicalCancellation.Token, awaitProviderCompletion: true);
        await physicalEntered.Task; physicalCancellation.Cancel();
        Check(!physicalCall.IsCompleted, "managed batch mode retains the actual request lifetime after caller cancellation");
        physicalRelease.SetResult();
        try { await physicalCall; throw new Exception("physical cancellation ignored"); } catch (OperationCanceledException) { }

        var capacityResult = await coordinator.TranslateAsync("capacity", Enumerable.Range(0, 300).Select(i => "unit" + i).ToArray(),
            (blocks, _) => Task.FromResult(new TranslationBatchResult(blocks.ToDictionary(b => b.Id, b => "ok"))), default);
        Check(capacityResult.Count == 300 && capacityResult.Count(p => p.Value.Text == "ok") == 256 &&
            capacityResult.Count(p => p.Value.Text == null) == 44,
            "pending translation units are bounded and overflow is reported instead of silently dropped");
    }

    sealed class ReplyHandler(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply());
    }

    sealed class WorkService(Func<CancellationToken, Task<string?>> work) : ITranslationService
    {
        public Task<string?> TranslateAsync(string json, string prompt) => work(TranslationRequestContext.Cancellation);
    }

    sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _now.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public TimeSpan NextDelay => _timers.Where(t => !t.Removed).Min(t => t.Due - _now);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, _now + dueTime); _timers.Add(timer); return timer;
        }
        public void Advance(TimeSpan amount)
        {
            _now += amount;
            foreach (var timer in _timers.Where(t => !t.Removed && t.Due <= _now).ToArray())
            {
                timer.Removed = true; timer.Callback(timer.State);
            }
        }
        private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal DateTimeOffset Due = due;
            internal bool Removed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = owner._now + dueTime; return !Removed; }
            public void Dispose() => Removed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
