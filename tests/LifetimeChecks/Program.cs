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

// Model the production stop/restart order with the real lifetime, scene owner and
// request wrapper. The provider deliberately ignores cancellation and stays pending.
var restartLifetime = new PipelineLifetime();
var restartScene = new OcrSceneState();
for (int cycle = 0; cycle < 50; cycle++)
{
    using var oldLease = restartLifetime.Capture();
    if (!restartScene.TryBeginTranslation(out long oldOwner)) throw new Exception("Old request could not start.");
    var oldProvider = new DeferredService();
    int failuresReported = 0;
    Task<string?> oldRequest = TranslationRequestContext.RunAsync(oldProvider, "same settings", "", oldLease.Token,
        _ => failuresReported++);
    restartScene.Defer("old OCR");
    long oldGeneration = restartScene.Generation;

    restartScene.CancelTranslation();
    restartLifetime.Invalidate();
    using var newLease = restartLifetime.Capture();
    if (!restartScene.TryBeginTranslation(out long newOwner) || oldProvider.Completion.Task.IsCompleted)
        throw new Exception("Restart waited for the canceled provider.");
    var newProvider = new DeferredService();
    Task<string?> newRequest = TranslationRequestContext.RunAsync(newProvider, "same settings", "", newLease.Token);
    restartScene.Defer("new OCR");

    try
    {
        await oldRequest.WaitAsync(TimeSpan.FromSeconds(5));
        throw new Exception("Old request did not observe cancellation.");
    }
    catch (OperationCanceledException) { }
    // This is the old top-level request's delayed finally and deferred callback.
    if (restartScene.EndTranslation(oldOwner) || !restartScene.IsTranslating)
        throw new Exception("Old cleanup released the new request's ownership.");
    if (oldGeneration == restartScene.Generation) restartScene.TakeDeferred();
    if (!restartScene.HasDeferred || failuresReported != 0)
        throw new Exception("Old cleanup consumed new OCR or reported cancellation as a failure.");
    string displayed = "new session";
    oldProvider.Completion.SetResult("old response");
    if (oldLease.TryApply(() => displayed = "old response") || displayed != "new session")
        throw new Exception("Old response committed after restart.");

    newProvider.Completion.SetResult("current response");
    string? response = await newRequest.WaitAsync(TimeSpan.FromSeconds(5));
    if (!newLease.TryApply(() => displayed = response!) || displayed != "current response" ||
        !restartScene.EndTranslation(newOwner) || restartScene.IsTranslating || restartScene.TakeDeferred() != "new OCR")
        throw new Exception("New request could not complete normally after restart.");
}
Check(true, "50 stop/restart cycles: uncooperative old providers cannot block, unlock, overwrite or drain new work");

using (var frames = new LatestCaptureFrame<TestFrame>())
{
    var first = new TestFrame(1);
    var second = new TestFrame(2);
    var newest = new TestFrame(3);
    frames.Publish(first); frames.Publish(second); frames.Publish(newest);
    Check(first.Disposals == 1 && second.Disposals == 1 && newest.Disposals == 0,
        "frame arrival releases superseded surfaces without converting them");
    var owned = frames.Take();
    frames.Clear();
    Check(ReferenceEquals(owned, newest) && frames.Take() == null && newest.Disposals == 0,
        "taking the latest surface transfers its lifetime to the consumer");
    owned!.Dispose();
    Check(newest.Disposals == 1, "consumer releases the taken surface exactly once");
    var pending = new TestFrame(4);
    frames.Publish(pending); frames.Clear();
    var resumed = new TestFrame(5);
    frames.Publish(resumed);
    Check(pending.Disposals == 1 && ReferenceEquals(frames.Take(), resumed),
        "capture reset clears the old surface and permits a fresh one");
    resumed.Dispose();
}
var closedFrames = new LatestCaptureFrame<TestFrame>();
var closedPending = new TestFrame(1);
closedFrames.Publish(closedPending); closedFrames.Dispose(); closedFrames.Dispose();
var lateFrame = new TestFrame(2);
closedFrames.Publish(lateFrame);
Check(closedPending.Disposals == 1 && lateFrame.Disposals == 1 && closedFrames.Take() == null,
    "shutdown rejects late surfaces and repeated cleanup cannot double-dispose them");
using (var bounded = new LatestCaptureFrame<TestFrame>())
{
    var produced = new List<TestFrame>();
    bounded.Drain(() => { var frame = new TestFrame(produced.Count + 1); produced.Add(frame); return frame; }, 2);
    Check(produced.Count == 2 && produced[0].Disposals == 1 && bounded.Take() is { Id: 2 },
        "native drain stops at pool capacity even when the producer never returns null");
    produced[1].Dispose();
}
using (var concurrent = new LatestCaptureFrame<TestFrame>())
{
    var produced = Enumerable.Range(0, 128).Select(i => new TestFrame(i)).ToArray();
    await Task.WhenAll(produced.Select(frame => Task.Run(() => concurrent.Publish(frame))));
    var owned = concurrent.Take();
    Check(owned != null && produced.Sum(frame => frame.Disposals) == 127 && produced.All(frame => frame.Disposals <= 1),
        "concurrent arrivals retain one owner and release every superseded frame once");
    owned!.Dispose();
}
var captureGate = new CaptureWorkGate();
var oldCapture = captureGate.TryEnter();
Check(oldCapture != null && captureGate.TryEnter() == null, "slow OCR cannot accumulate capture timer work");
oldCapture!.Dispose();
var nextCapture = captureGate.TryEnter();
oldCapture.Dispose();
Check(nextCapture != null && captureGate.TryEnter() == null,
    "an old capture cleanup cannot release a newer capture's gate");
nextCapture!.Dispose();
using (var recovered = captureGate.TryEnter())
    Check(recovered != null, "capture ownership is available after completion or an exception cleanup");

OcrSettlementChecks.Run(Check);
OcrCapturePolicyChecks.Run(Check);
await TranslationPipelineChecks.RunAsync(Check);
await GameTranslationFreshnessChecks.RunAsync(Check);
await LocalModelChecks.RunAsync(Check);
await HyMt2Checks.RunAsync(Check);
RenderingResourceChecks.Run(Check);
await NativeAbiChecks.RunIfConfiguredAsync(Check);
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

sealed class TestFrame(int id) : IDisposable
{
    private int _disposals;
    internal int Id => id;
    internal int Disposals => Volatile.Read(ref _disposals);
    public void Dispose() => Interlocked.Increment(ref _disposals);
}
