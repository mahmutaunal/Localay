using System.Net;
using System.Text.Json;

namespace AlpWareStudio.Localay;

internal static class GoogleCloudNmtChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            check(request.Method == HttpMethod.Post && request.RequestUri?.AbsoluteUri ==
                "https://translation.googleapis.com/language/translate/v2" &&
                request.Headers.GetValues("X-Goog-Api-Key").Single() == "fixture-key",
                "Cloud NMT authenticates through a header without putting credentials in the URL");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = document.RootElement;
            check(root.GetProperty("model").GetString() == "nmt" && root.GetProperty("format").GetString() == "text" &&
                root.GetProperty("source").GetString() == "en" && root.GetProperty("target").GetString() == "tr",
                "the real transport explicitly requests the NMT model and English-to-Turkish plain text");
            check(root.GetProperty("q").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "Play", "A\nB ##|||## {player}" }),
                "Cloud request arrays keep source blocks, line breaks, placeholders and literal separators intact");
            return Reply("""{"data":{"translations":[{"translatedText":"Oyna"},{"translatedText":"A &amp; B"}]}}""");
        }));
        var client = new GoogleCloudNmtClient(http);
        var translated = await client.TranslateAsync(["Play", "A\nB ##|||## {player}"], "en", "tr", "fixture-key", default);
        check(translated.SequenceEqual(new[] { "Oyna", "A & B" }) && calls == 1,
            "one Cloud request returns one decoded translation slot for every requested block");

        using var multilingualHttp = new HttpClient(new Handler(async (request, token) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = document.RootElement;
            check(root.GetProperty("source").GetString() == "ja" && root.GetProperty("target").GetString() == "ar" &&
                root.GetProperty("q")[0].GetString() == "こんにちは",
                "the fixed NMT transport preserves non-English source text and arbitrary selected language codes");
            return Reply("""{"data":{"translations":[{"translatedText":"مرحبًا"}]}}""");
        }));
        var multilingual = await new GoogleCloudNmtClient(multilingualHttp).TranslateAsync(["こんにちは"], "ja", "ar", "fixture-key", default);
        check(multilingual[0] == "مرحبًا", "multilingual response Unicode survives the transport without an English-to-Turkish restriction");

        string[] invalidReplies = ["{}", "[]", "null", "not json",
            """{"data":{"translations":[{"translatedText":"wrong count"}]}}""",
            """{"data":{"translations":[{"translatedText":"first"},{"translatedText":42}]}}"""];
        foreach (string reply in invalidReplies)
        {
            using var malformedHttp = new HttpClient(new Handler((_, _) => Task.FromResult(Reply(reply))));
            var result = await new GoogleCloudNmtClient(malformedHttp).TranslateAsync(["one", "two"], "en", "tr", "fixture-key", default);
            check(result.Count == 2 && result.All(x => x == null),
                "malformed or ambiguous Cloud replies cannot shift a translation to another block: " + reply);
        }

        foreach (int status in new[] { 401, 403, 429, 500 })
        {
            int failedCalls = 0;
            TranslationFailure? failure = null;
            using var failedHttp = new HttpClient(new Handler((_, _) =>
            {
                failedCalls++;
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
            }));
            var result = await TranslationRequestContext.RunAsync(new WorkService(async token =>
            {
                var values = await new GoogleCloudNmtClient(failedHttp).TranslateAsync(["one"], "ja", "ar", "fixture-key", token);
                return values[0];
            }), "", "", default, f => failure = f);
            check(result == null && failedCalls == 1 && failure?.Kind == (status switch
            {
                401 => TranslationFailureKind.Unauthorized, 403 => TranslationFailureKind.AccessDenied,
                429 => TranslationFailureKind.RateLimited, _ => TranslationFailureKind.Server
            }), "Cloud HTTP " + status + " reports its actual failure without another engine or retry request");
        }

        int unexpectedCalls = 0;
        using var guardHttp = new HttpClient(new Handler((_, _) =>
        {
            unexpectedCalls++;
            return Task.FromResult(Reply("{}"));
        }));
        var guarded = new GoogleCloudNmtClient(guardHttp);
        await guarded.TranslateAsync(["one"], "en", "tr", " ", default);
        await guarded.TranslateAsync(Enumerable.Repeat("one", 129).ToArray(), "en", "tr", "fixture-key", default);
        await guarded.TranslateAsync([], "en", "tr", "fixture-key", default);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await guarded.TranslateAsync(["one"], "en", "tr", "fixture-key", canceled.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { }
        check(unexpectedCalls == 0, "empty, unconfigured, oversized and already canceled Cloud requests never dispatch");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        using var waitingHttp = new HttpClient(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("canceled provider continued");
        }));
        var waiting = new GoogleCloudNmtClient(waitingHttp).TranslateAsync(["old subtitle"], "en", "tr", "fixture-key", stop.Token);
        await entered.Task;
        stop.Cancel();
        try { await waiting; throw new Exception("in-flight cancellation ignored"); }
        catch (OperationCanceledException) { }
        check(waiting.IsCanceled, "scene cancellation reaches the actual Cloud HTTP request");

        var bodyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopBody = new CancellationTokenSource();
        using var bodyHttp = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new WaitingContent(bodyEntered) })));
        var waitingBody = new GoogleCloudNmtClient(bodyHttp).TranslateAsync(["old subtitle"], "en", "tr", "fixture-key", stopBody.Token);
        await bodyEntered.Task;
        stopBody.Cancel();
        try { await waitingBody; throw new Exception("response-body cancellation ignored"); }
        catch (OperationCanceledException) { }
        check(waitingBody.IsCanceled, "cancellation remains effective after HTTP headers while the Cloud response body is pending");

        check(GameTranslationProfile.CacheIdentity(true, false) != GameTranslationProfile.CacheIdentity(false, false),
            "Cloud NMT never reuses a translation produced by the anonymous web engine");
        var before = TranslationBlockProtocol.CacheContext("nmt", GameTranslationProfile.MeaningContext("Google Translate", new[] { "old dialogue" }));
        var after = TranslationBlockProtocol.CacheContext("nmt", GameTranslationProfile.MeaningContext("Google Translate", new[] { "new dialogue" }));
        check(before == after, "conversation history cannot flush the NMT menu and HUD cache because NMT does not consume that history");
        var history = new[] { "context" };
        check(ReferenceEquals(GameTranslationProfile.MeaningContext("Ollama", history), history),
            "providers that consume conversation history keep their existing meaning context");
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class WorkService(Func<CancellationToken, Task<string?>> work) : ITranslationService
    {
        public Task<string?> TranslateAsync(string json, string prompt) => work(TranslationRequestContext.Cancellation);
    }
    private sealed class WaitingContent(TaskCompletionSource entered) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The body reader must propagate cancellation.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
