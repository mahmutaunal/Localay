using System.Net;
using System.Text.Json;
using AlpWareStudio.Localay;

internal static class BatchContractChecks
{
    internal static async Task RunAsync()
    {
        var fifteen = Enumerable.Range(0, 15).Select(i => "source" + i).ToArray();
        var normal = await Exercise(fifteen);
        if (normal.Requests.Count != 1 || normal.Results.Count != 15 ||
            fifteen.Where((text, i) => normal.Results.GetValueOrDefault("id" + i) != "tr:" + text).Any())
            throw new Exception("Batch source ownership changed.");
        var multiline = await Exercise(["first", "multi\nline", "last"]);
        if (!multiline.Requests.SequenceEqual(new[] { "first", "multi\nline", "last" }) || multiline.Results.Count != 3)
            throw new Exception("Multiline isolation changed.");
        var collapsed = await Exercise(["first", "last"], collapse: true);
        if (collapsed.Requests.Count != 3 || collapsed.Results["id0"] != "tr:first" || collapsed.Results["id1"] != "tr:last")
            throw new Exception("Line-count mismatch did not fall back to individual requests.");
        var swapped = await Exercise(["first", "last"], reverse: true);
        if (swapped.Results["id0"] != "tr:last")
            throw new Exception("The observed positional-mapping behavior changed; update the assessment.");
        if (TranslationServiceFactory.Attempts != 0)
            throw new Exception("Managed batches attempted a second provider.");
        Console.WriteLine("PASS: production batch/ID parsing, multiline isolation, count-mismatch fallback and no second provider.");
        Console.WriteLine("Observed synthetic limitation: equal-count reordered lines are accepted positionally; count checks do not prove meaning ownership.");
    }

    private static async Task<(Dictionary<string, string> Results, List<string> Requests)> Exercise(
        string[] texts, bool collapse = false, bool reverse = false)
    {
        var time = new StepTime();
        var requests = new List<string>();
        using var http = new HttpClient(new ReplyHandler(request =>
        {
            string text = Uri.UnescapeDataString(request.RequestUri!.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("q="))[2..]);
            requests.Add(text);
            time.Advance(); // Skip real rate-spacing waits without changing production throttling.
            var lines = text.Split('\n').Select(s => "tr:" + s);
            string translated = collapse && requests.Count == 1 ? "collapsed" : string.Join("\n", reverse ? lines.Reverse() : lines);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                JsonSerializer.Serialize(new object[] { new object[] { new object?[] { translated, null } } })) };
        }));
        var service = new GoogleTranslateService(new GoogleTranslateFreeClient(http, time));
        var blocks = texts.Select((text, i) => new TranslationBlock("id" + i, text)).ToArray();
        string request = JsonSerializer.Serialize(new { block_protocol = TranslationBlockProtocol.Version,
            source_language = "en", target_language = "tr", text_blocks = blocks.Select(b => new { id = b.Id, text = b.Text }) });
        string? reply = await TranslationRequestContext.RunAsync(service, request, "", default, awaitProviderCompletion: true);
        return (TranslationBlockProtocol.Parse(reply, true, blocks), requests);
    }
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(reply(request));
    }
    private sealed class StepTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance() => _now += TimeSpan.FromSeconds(1);
    }
}
