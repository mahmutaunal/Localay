using AlpWareStudio.Localay;

internal static class GameTranslationFreshnessChecks
{
    internal static Task RunAsync(Action<bool, string> check) => RunCoreAsync(check).WaitAsync(TimeSpan.FromSeconds(10));

    private static async Task RunCoreAsync(Action<bool, string> check)
    {
        // A provider that ignores cancellation holds the slot while thirty successive
        // dialogue lines arrive. Only the final line may dispatch once it exits.
        var batches = new TranslationBatchCoordinator();
        var lifetime = new PipelineLifetime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var cache = TranslationCache.Instance;
        const string context = "game-freshness-regression";
        cache.UseContext(context);
        cache.SetIfContext(context, "Health", "Sağlık");

        async Task<TranslationBatchResult> Execute(IReadOnlyList<TranslationBlock> blocks, CancellationToken token)
        {
            calls.AddRange(blocks.Select(b => b.Text));
            if (blocks.Any(b => b.Text == "old dialogue"))
            {
                entered.TrySetResult();
                await release.Task; // Intentionally ignore token until the old work really exits.
            }
            return new(blocks.ToDictionary(b => b.Id, b => "tr:" + b.Text));
        }

        using var oldLease = lifetime.Capture();
        var old = batches.TranslateAsync("generation-0", ["old dialogue"], Execute, oldLease.Token);
        await entered.Task;
        lifetime.Invalidate();
        try { await old; throw new Exception("obsolete caller was not canceled"); }
        catch (OperationCanceledException) { }
        check(calls.SequenceEqual(new[] { "old dialogue" }), "canceling old dialogue releases its caller while retaining the physical provider slot");

        var obsolete = new List<Task<IReadOnlyDictionary<string, TranslationBlockResult>>>();
        for (int i = 1; i <= 30; i++)
        {
            using var lease = lifetime.Capture();
            obsolete.Add(batches.TranslateAsync("generation-" + i, ["intermediate " + i], Execute, lease.Token));
            lifetime.Invalidate();
        }
        foreach (var task in obsolete)
        {
            try { await task; throw new Exception("obsolete queued dialogue was not canceled"); }
            catch (OperationCanceledException) { }
        }
        using var latestLease = lifetime.Capture();
        string[] visible = ["Health", "latest dialogue"];
        var missing = visible.Where(t => !cache.TryGet(context, t, out _)).ToArray();
        var latest = batches.TranslateAsync("latest", missing, Execute, latestLease.Token);
        check(!latest.IsCompleted && calls.Count == 1, "successive scene changes cannot overlap an uncooperative provider");
        release.TrySetResult();
        var result = await latest;
        check(calls.SequenceEqual(new[] { "old dialogue", "latest dialogue" }) && result["latest dialogue"].Text == "tr:latest dialogue",
            "thirty canceled queued subtitles incur no extra provider calls before the newest line");
        check(!oldLease.IsCurrent && latestLease.IsCurrent && cache.TryGet(context, "Health", out var hud) && hud == "Sağlık",
            "scene invalidation rejects old ownership while retaining verified HUD translations in cache");
    }
}
