using System.Net;
using System.Net.Http.Headers;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    passed++;
    Console.WriteLine("PASS: " + description);
}

var partial = TranslationParts.Reassemble([0, 1, 2], ["Oyna", null, "Ayarlar"]);
Check(partial.Success && partial.Text == "Oyna##|||####|||##Ayarlar", "partial translations keep the failed slot empty without copying source text");
var parts = TranslationParts.SplitExact(partial.Text, 3)!;
Check(parts.Length == 3 && parts[1] == "", "a missing middle translation does not shift later blocks");
Check(TranslationParts.SplitExact("one##|||##two", 3) == null, "changed separator count is rejected instead of pairing the wrong blocks");
Check(TranslationParts.SplitExact("one##|||##two##|||##three", 2) == null, "extra separator also rejects the combined response");
Check(TranslationParts.Reassemble([0], ["Ubisoft"]) == ("Ubisoft", true), "unchanged names are valid translations");
Check(!TranslationParts.Reassemble([0, 1], [null, " "]).Success, "all missing translations are a failure");
Check(TranslationParts.Reassemble([-1, 0], ["Oyna"]).Text == "##|||##Oyna", "an empty input slot keeps its position");
var cache = TranslationCache.Instance;
cache.UseContext("resilience");
for (int i = 0; i < parts.Length; i++) cache.SetIfContext("resilience", new[] { "Play", "Store", "Settings" }[i], parts[i]);
Check(!cache.TryGet("Store", out _) && cache.TryGet("Settings", out var saved) && saved == "Ayarlar", "only successful parts enter the production cache");

var now = DateTimeOffset.UtcNow;
var retry = new TranslationRetryState();
var transient = new TranslationFailure(TranslationFailureKind.Network);
Check(!retry.IsDue(now), "a new scene has no spurious retry");
retry.Failed(transient, now);
Check(!retry.IsDue(now.AddMilliseconds(1999)) && retry.IsDue(now.AddSeconds(2)), "first retry waits two seconds");
retry.Failed(transient, now.AddSeconds(2));
Check(!retry.IsDue(now.AddSeconds(6)) && retry.IsDue(now.AddSeconds(7)), "second retry waits five seconds");
retry.Failed(transient, now.AddSeconds(7));
Check(!retry.IsDue(now.AddDays(1)), "three failed attempts stop automatic retries");
retry.Reset();
Check(!retry.IsDue(now.AddDays(1)), "new scene/session or success clears retry state");
foreach (var kind in new[] { TranslationFailureKind.Unauthorized, TranslationFailureKind.AccessDenied, TranslationFailureKind.InvalidRequest })
{
    retry.Reset(); retry.Failed(new(kind), now);
    Check(!retry.IsDue(now.AddDays(1)), kind + " does not automatically retry");
}
retry.Reset(); retry.Failed(new(TranslationFailureKind.RateLimited), now);
Check(!retry.IsDue(now.AddSeconds(29)) && retry.IsDue(now.AddSeconds(30)), "rate limits wait at least thirty seconds");
retry.Reset(); retry.Failed(new(TranslationFailureKind.RateLimited, TimeSpan.FromSeconds(120)), now);
Check(!retry.IsDue(now.AddSeconds(119)) && retry.IsDue(now.AddSeconds(120)), "server Retry-After takes precedence over local retry delay");

foreach (var (status, expected) in new[] { (400, TranslationFailureKind.InvalidRequest), (401, TranslationFailureKind.Unauthorized), (403, TranslationFailureKind.AccessDenied), (429, TranslationFailureKind.RateLimited), (503, TranslationFailureKind.Server), (504, TranslationFailureKind.Timeout) })
{
    using var client = new HttpClient(new TranslationHttpHandler(new ReplyHandler(() => new((HttpStatusCode)status))));
    TranslationFailure? failure = null;
    await TranslationRequestContext.RunAsync(new HttpService(client), "", "", default, f => failure = f);
    Check(failure?.Kind == expected, "HTTP " + status + " survives a provider returning null");
}
using (var client = new HttpClient(new TranslationHttpHandler(new ReplyHandler(() => throw new HttpRequestException("offline")))))
{
    TranslationFailure? failure = null;
    await TranslationRequestContext.RunAsync(new HttpService(client), "", "", default, f => failure = f);
    Check(failure?.Kind == TranslationFailureKind.Network, "network exceptions are distinct from authentication errors");
}
TranslationFailure? emptyFailure = null;
await TranslationRequestContext.RunAsync(new WorkService(_ => Task.FromResult<string?>(null)), "", "", default, f => emptyFailure = f);
Check(emptyFailure?.Kind == TranslationFailureKind.InvalidResponse, "empty responses get a neutral diagnosis without inventing a key error");
TranslationFailure? timeoutFailure = null;
await TranslationRequestContext.RunAsync(new WorkService(_ => throw new TaskCanceledException()), "", "", default, f => timeoutFailure = f);
Check(timeoutFailure?.Kind == TranslationFailureKind.Timeout, "provider timeout is distinct from session cancellation");
using (var canceled = new CancellationTokenSource())
{
    bool notified = false;
    var pending = TranslationRequestContext.RunAsync(new WorkService(async token => { canceled.Cancel(); await Task.Delay(1000, token); return "late"; }), "", "", canceled.Token, _ => notified = true);
    try { await pending; throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { }
    Check(!notified, "session cancellation does not trigger failure tips or retries");
}

// Exercise the real Google free client, including the no-network cooldown path.
int requests = 0;
using (var client = new HttpClient(new TranslationHttpHandler(new ReplyHandler(() =>
{
    requests++;
    var response = new HttpResponseMessage((HttpStatusCode)429);
    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
    return response;
}))))
{
    var free = new GoogleTranslateFreeClient(client);
    var service = new WorkService(token => free.TranslateAsync("Play", "en", "tr", token));
    TranslationFailure? failure = null;
    await TranslationRequestContext.RunAsync(service, "", "", default, f => failure = f);
    Check(requests == 1 && failure?.Kind == TranslationFailureKind.RateLimited && failure.RetryAfter == TimeSpan.FromSeconds(120), "Google 429 stops endpoint hopping and retains Retry-After");
    failure = null;
    await TranslationRequestContext.RunAsync(service, "", "", default, f => failure = f);
    Check(requests == 1 && failure?.Kind == TranslationFailureKind.RateLimited && failure.RetryAfter > TimeSpan.FromSeconds(100), "Google cooldown remains correctly classified without another HTTP request");
}
// Parallel requests must not inherit another request's diagnosis.
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
TranslationFailure? firstFailure = null, secondFailure = null;
var first = TranslationRequestContext.RunAsync(new WorkService(async _ => { TranslationRequestContext.ReportFailure(new(TranslationFailureKind.Unauthorized)); ready.SetResult(); await release.Task; return null; }), "", "", default, f => firstFailure = f);
await ready.Task;
await TranslationRequestContext.RunAsync(new WorkService(_ => Task.FromResult<string?>(null)), "", "", default, f => secondFailure = f);
release.SetResult(); await first;
Check(firstFailure?.Kind == TranslationFailureKind.Unauthorized && secondFailure?.Kind == TranslationFailureKind.InvalidResponse, "parallel provider diagnostics remain isolated");
Check(!TranslationRequestContext.IsManaged, "request diagnostics never leak into the caller");

await TranslationPipelineChecks.RunAsync(Check);
await LocalModelChecks.RunAsync(Check);
Console.WriteLine($"{passed} translation resilience checks passed.");

sealed class ReplyHandler(Func<HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply());
}
sealed class HttpService(HttpClient client) : ITranslationService
{
    public async Task<string?> TranslateAsync(string json, string prompt)
    {
        try { using var response = await client.GetAsync("https://local.invalid/", TranslationRequestContext.Cancellation); return response.IsSuccessStatusCode ? "ok" : null; }
        catch (HttpRequestException) { return null; }
    }
}
sealed class WorkService(Func<CancellationToken, Task<string?>> work) : ITranslationService
{
    public Task<string?> TranslateAsync(string json, string prompt) => work(TranslationRequestContext.Cancellation);
}
