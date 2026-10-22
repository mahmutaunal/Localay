using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlpWareStudio.Localay;

internal static class LocalModelChecks
{
    internal static Task RunAsync(Action<bool, string> check) => RunCoreAsync(check).WaitAsync(TimeSpan.FromSeconds(10));
    private static async Task RunCoreAsync(Action<bool, string> check)
    {
        const string input = """{"block_protocol":"id-v1","source_language":"english","target_language":"Turkish","text_blocks":[{"id":"private-id-a","text":"Do not delete {name}. 999 ##|||##"},{"id":"private-id-b","text":"Settings"}]}""";
        var handler = new LocalHandler(async (request, index, cancellation) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var root = payload.RootElement;
            string content = root.GetProperty("messages")[0].GetProperty("content").GetString()!;
            check(request.RequestUri!.AbsolutePath == "/api/chat", "TranslateGemma uses Ollama chat, not generic JSON generation");
            check(!root.TryGetProperty("format", out _) && !content.Contains("private-id-"), "model never generates wire IDs or JSON wrappers");
            check(root.GetProperty("options").GetProperty("temperature").GetInt32() == 0, "candidate inference is deterministic");
            check(content.EndsWith(index == 0 ? "\n\n\nDo not delete {name}. 999 ##|||##" : "\n\n\nSettings", StringComparison.Ordinal),
                "one intact source block follows the specialized translation template");
            return Reply(index == 0 ? "{name} öğesini silmeyin. 999 ##|||##" : "Ayarlar");
        });
        using var http = new HttpClient(handler);
        var client = new TranslateGemmaClient(http, new Uri("http://127.0.0.1:11434/api/generate"), "translategemma:4b");
        string? response = await client.TranslateAsync(input, default);
        using var source = JsonDocument.Parse(input);
        var mapped = TranslationBlockProtocol.Parse(response, true, TranslationBlockProtocol.ReadBlocks(source.RootElement));
        check(mapped.Count == 2 && mapped["private-id-b"] == "Ayarlar", "application restores IDs independently of generated text");

        bool unsupported = false;
        try { await client.TranslateAsync(input.Replace("english", "japanese"), default); }
        catch (ArgumentException) { unsupported = true; }
        check(unsupported && handler.Calls == 2, "unsupported pair rejected before contacting a model");
        bool tooLong = false;
        try { await client.TranslateAsync(input.Replace("Settings", new string('x', 4097)), default); }
        catch (ArgumentException) { tooLong = true; }
        check(tooLong && handler.Calls == 2, "entire batch preflight prevents partial inference on oversized input");

        using var truncatedHttp = new HttpClient(new LocalHandler((_, _, _) => Task.FromResult(Reply("eksik", "length"))));
        check(await new TranslateGemmaClient(truncatedHttp, new Uri("http://localhost/api/generate"), "translategemma:4b")
            .TranslateAsync(input, default) == null, "token-limited output cannot be displayed as complete translation");
        foreach (string broken in new[] { "{", "[]", "{\"done\":true,\"done_reason\":5}", "{\"done\":true,\"done_reason\":\"stop\",\"message\":[]}" })
        {
            using var partialHttp = new HttpClient(new LocalHandler((_, index, _) => Task.FromResult(index == 0
                ? Reply("Doğru ilk blok") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(broken) })));
            string? partial = await new TranslateGemmaClient(partialHttp, new Uri("http://localhost/api/generate"), "translategemma:4b")
                .TranslateAsync(input, default);
            var valid = TranslationBlockProtocol.Parse(partial, true, TranslationBlockProtocol.ReadBlocks(source.RootElement));
            check(valid.Count == 1 && valid["private-id-a"] == "Doğru ilk blok" && !valid.ContainsKey("private-id-b"),
                "a malformed later response retains only the completed block under its own ID");
        }
        using var failedHttp = new HttpClient(new LocalHandler((_, index, _) => Task.FromResult(index == 0
            ? Reply("Doğru ilk blok") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        string? partialFailure = await new TranslateGemmaClient(failedHttp, new Uri("http://localhost/api/generate"), "translategemma:4b")
            .TranslateAsync(input, default);
        check(TranslationBlockProtocol.Parse(partialFailure, true, TranslationBlockProtocol.ReadBlocks(source.RootElement)).Count == 1,
            "later HTTP failure does not force retranslating an earlier completed block");
        using var cancelled = new CancellationTokenSource();
        var cancelHandler = new LocalHandler((_, _, _) => { cancelled.Cancel(); return Task.FromResult(Reply("ilk")); });
        using var cancelHttp = new HttpClient(cancelHandler);
        bool stopped = false;
        try { await new TranslateGemmaClient(cancelHttp, new Uri("http://localhost/api/generate"), "translategemma:4b").TranslateAsync(input, cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped && cancelHandler.Calls == 1, "cancelled batch starts no subsequent native-model requests");

        string marianIdentity = "marian-en-tr-v1:" + new string('a', 64);
        string gemmaIdentity = "translategemma-en-tr-v1:" + new string('b', 64);
        var fast = new LocalModelEvidence("marian-en-tr", marianIdentity, marianIdentity, "same-inputs-v1", "observed-end-to-end",
            true, 0, true, 70, 1, 4, 0, 1000);
        var other = fast with { Candidate = "translategemma-4b-en-tr", ProfileIdentity = gemmaIdentity, ReviewedProfileIdentity = gemmaIdentity,
            WarmEndToEndP95Ms = 75, CpuMsPerFrame = 3, PeakWorkingSetBytes = 800 };
        check(LocalModelDecision.Choose([]).Candidate == null, "missing measurements cannot select a default");
        check(LocalModelDecision.Rejection(fast with { Origin = "synthetic-stage-replay" }) != null, "synthetic timings cannot qualify a model");
        check(LocalModelDecision.Rejection(fast with { QualityReviewed = false }) == "quality-gate", "speed alone cannot pass the quality gate");
        check(LocalModelDecision.Rejection(fast with { CriticalErrors = 1 }) == "quality-gate", "one critical semantic error rejects a fast candidate");
        check(LocalModelDecision.Rejection(fast with { ReviewedProfileIdentity = "different-quantization" }) != null, "another model revision's review cannot qualify current weights");
        check(LocalModelDecision.Rejection(fast with { WarmEndToEndP95Ms = 100 }) != null, "100ms is not under 100ms");
        check(LocalModelDecision.Rejection(fast with { Coverage = 0.99 }) != null, "hiding failed blocks cannot satisfy latency acceptance");
        check(LocalModelDecision.Rejection(fast with { CpuMsPerFrame = double.NaN }) != null, "missing resource measurements reject acceptance");
        check(LocalModelDecision.Choose([fast, other]).Candidate == other.Candidate, "within ten percent, lower CPU and memory takes precedence");
        check(LocalModelDecision.Choose([fast, other with { WarmEndToEndP95Ms = 90 }]).Candidate == fast.Candidate, "clearly lower p95 wins after both quality gates pass");
        check(LocalModelDecision.Choose([fast, other with { ComparisonSet = "different-inputs" }]).Candidate == null, "incomparable workloads cannot rank providers");
        check(LocalModelDecision.Choose([fast, other with { GpuMsPerFrame = 1 }]).Candidate == null, "CPU versus GPU resource tradeoffs require explicit review");
        CheckPackage(check);
    }

    private static void CheckPackage(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "localay-package-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "model"));
        try
        {
            var hashes = new Dictionary<string, string>();
            foreach (string file in new[] { "localay_marian.dll", "source.spm", "target.spm", "model/model.bin", "model/config.json", "model/shared_vocabulary.json" })
            {
                // Schema/hash fixtures only. No native binary or model is loaded by this test.
                byte[] bytes = Encoding.UTF8.GetBytes("fixture: " + file);
                File.WriteAllBytes(Path.Combine(directory, file), bytes);
                hashes[file] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            File.WriteAllText(Path.Combine(directory, "profile.json"), JsonSerializer.Serialize(new
            {
                schema = 1, model = "Helsinki-NLP/opus-mt-tc-big-en-tr", revision = new string('a', 40),
                runtime = "CTranslate2-4.8.3", tokenizer = "SentencePiece-0.2.1", compute = "cpu-int8", source = "en", target = "tr", files = hashes
            }));
            check(MarianPackage.Read(directory).Identity.StartsWith("marian-en-tr-v1:", StringComparison.Ordinal), "package fingerprint binds the complete asset manifest");
            File.AppendAllText(Path.Combine(directory, "source.spm"), "changed tokenizer");
            bool rejected = false;
            try { MarianPackage.Read(directory); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "tokenizer changes fail readiness before native loading");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static HttpResponseMessage Reply(string text, string reason = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { done = true, done_reason = reason, message = new { role = "assistant", content = text } }))
    };
    private sealed class LocalHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, Calls++, cancellationToken);
    }
}
