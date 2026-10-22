using System.Net;
using System.Text;
using System.Text.Json;

namespace AlpWareStudio.Localay;

// All replies are fixtures. This checks transport/ownership, never model quality or speed.
internal static class HyMt2Checks
{
    internal static Task RunAsync(Action<bool, string> check) => RunCoreAsync(check).WaitAsync(TimeSpan.FromSeconds(10));

    private static string Input(string source = "English", string target = "Turkish") => JsonSerializer.Serialize(new {
        block_protocol = TranslationBlockProtocol.Version, source_language = source, target_language = target,
        previous_context = new[] { "unused old dialogue" },
        text_blocks = new[] { new { id = "private-a", text = "Keep {name}.\nUse %1$s <b>now</b>." }, new { id = "private-b", text = "Settings" } }
    });
    private static HttpResponseMessage Reply(string text, string reason = "stop", string? model = null) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(new { model = model ?? HyMt2Profile.Model, done = true, done_reason = reason,
            message = new { role = "assistant", content = text } }), Encoding.UTF8, "application/json")
    };
    private static async Task RunCoreAsync(Action<bool, string> check)
    {
        using var source = JsonDocument.Parse(Input());
        var blocks = TranslationBlockProtocol.ReadBlocks(source.RootElement);
        var handler = new Handler(async (request, index, token) => {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var body = payload.RootElement;
            string prompt = body.GetProperty("messages")[0].GetProperty("content").GetString()!;
            check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/chat" &&
                body.GetProperty("model").GetString() == HyMt2Profile.Model, "Hy-MT2 uses the exact selected model through the existing Ollama chat endpoint");
            check(!body.TryGetProperty("format", out _) && !prompt.Contains("private-") && !prompt.Contains("unused old dialogue") &&
                prompt.EndsWith(blocks[index].Text, StringComparison.Ordinal), "each intact source enters a plain translation prompt without wire IDs, JSON constraints or unused history");
            check(prompt.Contains("into Turkish") && body.GetProperty("options").GetProperty("top_k").GetInt32() == 20 &&
                body.GetProperty("options").GetProperty("num_predict").GetInt32() == 2048,
                "full target name, publisher sampling and bounded generation are sent");
            return Reply(index == 0 ? "{name} kalsın.\n%1$s <b>şimdi</b> kullan." : "Ayarlar");
        });
        using var http = new HttpClient(handler);
        var client = new HyMt2Client(http, new Uri("http://localhost:11434/api/generate"), HyMt2Profile.Model);
        var actual = TranslationBlockProtocol.Parse(await client.TranslateAsync(Input(), default), true, blocks);
        check(actual.Count == 2 && actual["private-a"].Contains("%1$s") && actual["private-b"] == "Ayarlar",
            "the application restores IDs and multiline placeholders without inferring identity from model output order");

        int before = handler.Calls;
        foreach (string invalid in new[] { Input(target: "Danish"), Input(source: "Turkish"), Input().Replace("Settings", new string('x', 4097)) })
        {
            bool rejected = false;
            try { await client.TranslateAsync(invalid, default); } catch (ArgumentException) { rejected = true; }
            check(rejected && handler.Calls == before, "unsupported pairs and oversized later blocks fail before any local inference");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await client.TranslateAsync(Input(), canceled.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            check(handler.Calls == before, "already canceled local requests never dispatch");
        }
        foreach (var bad in new Func<HttpResponseMessage>[] {
            () => Reply("{name} %1$s <b>eksik</b>", "length"),
            () => Reply("{name} %1$s <b>yanlış</b>", model: "another-model"),
            () => Reply("yer tutucu kayıp"),
            () => new(HttpStatusCode.OK) { Content = new StringContent("[]") },
            () => new(HttpStatusCode.OK) { Content = new StringContent("{broken") }
        })
        {
            using var invalidHttp = new HttpClient(new Handler((_, _, _) => Task.FromResult(bad())));
            check(await new HyMt2Client(invalidHttp, new Uri("http://localhost/api/generate"), HyMt2Profile.Model)
                .TranslateAsync(Input(), default) == null, "truncated, wrong-tag, damaged-placeholder and malformed outputs cannot become complete overlays");
        }
        using (var partialHttp = new HttpClient(new Handler((_, index, _) => Task.FromResult(index == 0
            ? Reply("{name} %1$s <b>tamam</b>") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
        {
            var partial = TranslationBlockProtocol.Parse(await new HyMt2Client(partialHttp, new Uri("http://localhost/api/generate"), HyMt2Profile.Model)
                .TranslateAsync(Input(), default), true, blocks);
            check(partial.Count == 1 && partial.ContainsKey("private-a"), "later local failure retains only the valid source-owned result without fallback");
        }
        using (var cancel = new CancellationTokenSource())
        {
            var cancelHandler = new Handler((_, _, _) => { cancel.Cancel(); return Task.FromResult(Reply("{name} %1$s <b>tamam</b>")); });
            using var cancelHttp = new HttpClient(cancelHandler);
            try { await new HyMt2Client(cancelHttp, new Uri("http://localhost/api/generate"), HyMt2Profile.Model).TranslateAsync(Input(), cancel.Token); }
            catch (OperationCanceledException) { }
            check(cancelHandler.Calls == 1, "scene cancellation starts no subsequent local generation");
        }
        check(HyMt2Profile.PreservesTokens("{x} {x} %s <b>ok</b>", "<b>iyi</b> %s {x} {x}") &&
            !HyMt2Profile.PreservesTokens("{x} {x}", "{x}") && !HyMt2Profile.PreservesTokens("hello", "{invented}"),
            "placeholder validation checks exact spelling and multiplicity, including invented tokens");
        check(GameTranslationProfile.MeaningContext("Ollama", new[] { "old" }, HyMt2Profile.Model) is string[] { Length: 0 } &&
            GameTranslationProfile.MeaningContext("Ollama", new[] { "old" }, "other") is string[] { Length: 1 },
            "unused history cannot flush Hy-MT2 HUD cache while generic Ollama keeps its history context");

        // Real coordinator + real adapter + fake HTTP: ready captions must reach the
        // consumer before a later menu generation finishes, with one provider active.
        var enteredMenu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMenu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shownCaption = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();
        var sequential = new Handler(async (_, index, token) => {
            if (index == 1) { enteredMenu.SetResult(); await releaseMenu.Task.WaitAsync(token); }
            return Reply(index == 0 ? "Altyazı" : "Menü");
        });
        using var sequentialHttp = new HttpClient(sequential);
        var adapter = new HyMt2Client(sequentialHttp, new Uri("http://localhost/api/generate"), HyMt2Profile.Model);
        var coordinator = new TranslationBatchCoordinator();
        var scene = coordinator.TranslateAsync("hy-profile", new[] { "Caption", "Menu" }, async (chunk, token) => {
            var request = JsonSerializer.Serialize(new { block_protocol = TranslationBlockProtocol.Version, source_language = "en", target_language = "tr",
                text_blocks = chunk.Select(b => new { id = b.Id, text = b.Text }) });
            return new(TranslationBlockProtocol.Parse(await adapter.TranslateAsync(request, token), true, chunk));
        }, default, ready => {
            published.AddRange(ready.Keys);
            if (ready.ContainsKey("Caption")) shownCaption.TrySetResult();
            return Task.CompletedTask;
        }, batchSize: 1);
        await enteredMenu.Task; await shownCaption.Task;
        check(!scene.IsCompleted && published.SequenceEqual(new[] { "Caption" }), "the local caption is published while the next menu generation remains pending");
        releaseMenu.SetResult();
        var completed = await scene;
        check(completed.Count == 2 && sequential.Calls == 2 && published.SequenceEqual(new[] { "Caption", "Menu" }),
            "local per-block scheduling reuses the existing provider slot and does not duplicate inference");
    }
    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request, Calls++, token);
    }
}
