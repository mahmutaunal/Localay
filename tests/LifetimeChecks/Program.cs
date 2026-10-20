using System.Net;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
async Task Canceled(Task task, string message)
{
    try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (OperationCanceledException) { Check(true, message); return; }
    throw new InvalidOperationException("FAILED: " + message);
}

var lifetime = new PipelineLifetime();
using var ocr = lifetime.Capture();
using var translation = lifetime.Capture();
Check(ocr.IsCurrent && translation.IsCurrent, "OCR and translation initially belong to the current session");
int canceled = 0;
using var registration = translation.Token.Register(() => Interlocked.Increment(ref canceled));
lifetime.Invalidate();
Check(!ocr.IsCurrent && !translation.IsCurrent && ocr.Token.IsCancellationRequested && canceled == 1,
    "a session change cancels every outstanding lease");
using var next = lifetime.Capture();
Check(next.IsCurrent && !next.Token.IsCancellationRequested, "the next session can start immediately");
using var lateRegistration = CancellationTokenSource.CreateLinkedTokenSource(ocr.Token);
Check(lateRegistration.IsCancellationRequested, "late consumers can safely use canceled tokens while their lease is alive");
string screenText = "new text_0";
Check(!translation.TryApply(() => screenText = "old text_0") && screenText == "new text_0",
    "an old response cannot overwrite a reused text ID");
Check(next.TryApply(() => screenText = "current translation") && screenText == "current translation",
    "a current response can commit");
ocr.Dispose();
translation.Dispose();
Check(next.IsCurrent, "disposing an old operation does not cancel the new session");
using var afterDispose = CancellationTokenSource.CreateLinkedTokenSource(translation.Token);
Check(afterDispose.IsCancellationRequested, "a late provider can still recognize cancellation after its caller releases the lease");
using var disposed = lifetime.Capture();
disposed.Dispose();
Check(!disposed.IsCurrent && !disposed.TryApply(() => throw new Exception()), "a disposed lease cannot commit");

// Exercise the production wrapper with an actual HttpClient and an in-memory handler.
var nativeLifetime = new PipelineLifetime();
using var nativeLease = nativeLifetime.Capture();
using var handler = new BlockingHandler();
using var client = new HttpClient(handler);
var nativeService = new HttpService(client);
Task<string?> nativeRequest = TranslationRequestContext.RunAsync(nativeService, "{}", "", nativeLease.Token);
await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
nativeLifetime.Invalidate();
await Canceled(nativeRequest, "invalidating a session cancels its HTTP request");
await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(handler.Token.CanBeCanceled, "the provider receives a cancellation token without changing its public interface");
Check(!TranslationRequestContext.Cancellation.CanBeCanceled, "request cancellation does not leak into its caller's async context");

// Even a provider that ignores cancellation cannot hold the pipeline or apply a late result.
var stubbornLifetime = new PipelineLifetime();
using var stubbornLease = stubbornLifetime.Capture();
var stubborn = new DeferredService();
Task<string?> stubbornRequest = TranslationRequestContext.RunAsync(stubborn, "{}", "", stubbornLease.Token);
stubbornLifetime.Invalidate();
await Canceled(stubbornRequest, "an uncooperative provider cannot keep the old caller waiting");
stubborn.Completion.SetResult("late result");
await stubborn.Completion.Task;
Check(!stubbornLease.TryApply(() => screenText = "late result"), "a provider completing after cancellation cannot commit");

// Cancellation is per request; it never calls CancelPendingRequests on a shared client.
var firstLifetime = new PipelineLifetime();
var secondLifetime = new PipelineLifetime();
using var firstLease = firstLifetime.Capture();
using var secondLease = secondLifetime.Capture();
var firstService = new DeferredService();
var secondService = new DeferredService();
var firstRequest = TranslationRequestContext.RunAsync(firstService, "{}", "", firstLease.Token);
var secondRequest = TranslationRequestContext.RunAsync(secondService, "{}", "", secondLease.Token);
firstLifetime.Invalidate();
await Canceled(firstRequest, "one request can be canceled independently");
Check(firstService.Token.IsCancellationRequested && !secondService.Token.IsCancellationRequested,
    "parallel async request contexts are isolated");
secondService.Completion.SetResult("second result");
Check(await secondRequest == "second result", "canceling one request leaves another request usable");
firstService.Completion.SetResult("ignored");

var cache = TranslationCache.Instance;
cache.UseContext("English to Turkish");
var cacheLifetime = new PipelineLifetime();
using var oldPrefetch = cacheLifetime.Capture();
cacheLifetime.Invalidate(); // stop and restart with the same settings
cache.UseContext("English to Turkish");
Check(!oldPrefetch.TryApply(() => cache.SetIfContext("English to Turkish", "Play", "stale")),
    "identical settings after a restart do not revive an old prefetch");
Check(!cache.TryGet("Play", out _), "stale prefetch does not enter the cache");
cache.UseContext("English to French");
cache.SetIfContext("English to Turkish", "Play", "wrong language");
Check(!cache.TryGet("Play", out _), "a response cannot write into a different cache context");
using var currentPrefetch = cacheLifetime.Capture();
currentPrefetch.TryApply(() => cache.SetIfContext("English to French", "Play", "Jouer"));
Check(cache.TryGet("Play", out var result) && result == "Jouer", "the current prefetch can populate the correct cache");
cache.Clear();

// Invalidation and cache commits share a gate: a competing old write cannot run after invalidation returns.
var racedLifetime = new PipelineLifetime();
using var raced = racedLifetime.Capture();
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
var write = Task.Run(() => raced.TryApply(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }));
Check(entered.Wait(TimeSpan.FromSeconds(5)), "cache commit starts under the session gate");
var invalidate = Task.Run(racedLifetime.Invalidate);
release.Set();
await Task.WhenAll(write, invalidate).WaitAsync(TimeSpan.FromSeconds(5));
Check(!raced.TryApply(() => throw new Exception()), "invalidation prevents every subsequent old-session commit");

Console.WriteLine($"{passed} lifetime checks passed. No network, Windows capture or user settings were accessed.");

sealed class DeferredService : ITranslationService
{
    internal readonly TaskCompletionSource<string?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token;
    public Task<string?> TranslateAsync(string jsonData, string prompt)
    {
        Token = TranslationRequestContext.Cancellation;
        return Completion.Task;
    }
}

sealed class HttpService(HttpClient client) : ITranslationService
{
    public async Task<string?> TranslateAsync(string jsonData, string prompt)
    {
        using var response = await client.GetAsync("https://localay.invalid/test", TranslationRequestContext.Cancellation);
        return await response.Content.ReadAsStringAsync(TranslationRequestContext.Cancellation);
    }
}

sealed class BlockingHandler : HttpMessageHandler
{
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Token = cancellationToken;
        Started.SetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        finally { Canceled.TrySetResult(); }
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
