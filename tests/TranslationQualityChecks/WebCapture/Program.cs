using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AlpWareStudio.Localay;

if (args.Length is < 2 or > 3 || args[0] is not ("--check" or "--run" or "--run-batch15") ||
    (args[0] != "--check" && args.Length != 3))
{
    Console.WriteLine("Usage: WebCapture --check corpus.json | --run/--run-batch15 corpus.json NEW-output.json");
    return 2;
}
byte[] corpusBytes = await File.ReadAllBytesAsync(args[1]);
using var corpus = JsonDocument.Parse(corpusBytes);
var root = corpus.RootElement;
if (root.GetProperty("sourceLanguage").GetString() != "en" || root.GetProperty("targetLanguage").GetString() != "tr")
    throw new InvalidDataException("Expected the English-to-Turkish corpus.");
var cases = root.GetProperty("cases").EnumerateArray().Select(c =>
    new Result { id = c.GetProperty("id").GetString()!, source = c.GetProperty("source").GetString()! }).ToArray();
if (cases.Length != 60 || cases.Select(c => c.id).Distinct().Count() != 60 || cases.Any(c => string.IsNullOrWhiteSpace(c.source)))
    throw new InvalidDataException("Expected 60 uniquely identified source blocks.");
if (args[0] == "--check")
{
    await BatchContractChecks.RunAsync();
    Console.WriteLine("PASS: 60 source blocks; no network calls. Run requires explicit --run and a new output file.");
    return 0;
}
// Reserve the file before any network call. Never overwrite a previous experiment.
await using var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write, FileShare.Read);
var started = DateTimeOffset.UtcNow;
bool batched = args[0] == "--run-batch15";
var audit = new WebRequestAudit(cases);
using var http = new HttpClient(audit) { Timeout = TimeSpan.FromSeconds(8) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
var client = new GoogleTranslateFreeClient(http);
using var totalBudget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
string? stopped = null;
var batchObservations = new List<object>();
if (batched)
{
    var service = new GoogleTranslateService(client);
    int batchId = 0;
    foreach (var group in cases.Chunk(15))
    {
        batchId++;
        var blocks = group.Select(c => new TranslationBlock(c.id, c.source)).ToArray();
        string request = JsonSerializer.Serialize(new
        {
            block_protocol = TranslationBlockProtocol.Version, source_language = "en", target_language = "tr",
            text_blocks = blocks.Select(b => new { id = b.Id, text = b.Text })
        });
        TranslationFailure? failure = null;
        var timer = Stopwatch.StartNew();
        string? reply = null;
        try
        {
            reply = await TranslationRequestContext.RunAsync(service, request, "", totalBudget.Token,
                f => failure = f, awaitProviderCompletion: true);
        }
        catch (OperationCanceledException) { failure = new(TranslationFailureKind.Timeout); }
        var parsed = TranslationBlockProtocol.Parse(reply, true, blocks);
        foreach (var item in group)
        {
            item.batchId = batchId;
            item.translation = parsed.GetValueOrDefault(item.id);
            item.status = string.IsNullOrWhiteSpace(item.translation) ? "failed" : "success";
            item.failure = item.status == "failed" ? failure?.Kind.ToString() ?? "InvalidResponse" : null;
        }
        batchObservations.Add(new { batchId, sourceIds = group.Select(c => c.id),
            elapsedMsIncludingClientSpacing = Math.Round(timer.Elapsed.TotalMilliseconds, 2),
            completedCount = group.Count(c => c.status == "success") });
        Console.WriteLine("Batch " + batchId + ": " + parsed.Count + "/" + group.Length);
        if (group.Any(c => c.status != "success") || client.IsCoolingDown)
        {
            stopped = failure?.Kind.ToString() ?? "incomplete_batch";
            break;
        }
    }
}
else
{
    foreach (var item in cases)
    {
        TranslationFailure? failure = null;
        var timer = Stopwatch.StartNew();
        using var partBudget = CancellationTokenSource.CreateLinkedTokenSource(totalBudget.Token);
        partBudget.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            item.translation = await TranslationRequestContext.RunAsync(new SingleBlock(client), item.source, "",
                partBudget.Token, f => failure = f, awaitProviderCompletion: true);
            item.status = string.IsNullOrWhiteSpace(item.translation) ? "failed" : "success";
        }
        catch (OperationCanceledException)
        {
            item.status = "failed";
            failure = new(TranslationFailureKind.Timeout);
        }
        item.elapsedMsIncludingClientSpacing = Math.Round(timer.Elapsed.TotalMilliseconds, 2);
        item.failure = failure?.Kind.ToString();
        Console.WriteLine(item.id + ": " + item.status + (item.failure == null ? "" : " / " + item.failure));
        // Do not turn availability failures into sixty retries or bypass a cooldown.
        if (item.status != "success" || client.IsCoolingDown || totalBudget.IsCancellationRequested)
        {
            stopped = item.failure ?? "empty_response";
            break;
        }
    }
}

var record = new
{
    corpusSha256 = Convert.ToHexString(SHA256.HashData(corpusBytes)).ToLowerInvariant(),
    provenance = new
    {
        kind = "engine_output", provider = "Google Translate", mode = "web",
        sourceLanguage = "en", targetLanguage = "tr", requestLayout = batched ? "batch15" : "single",
        adapter = batched ? "production GoogleTranslateService + TranslationBlockProtocol + GoogleTranslateFreeClient" : "production GoogleTranslateFreeClient", cacheEnabled = false,
        settings = "isolated en/tr; Cloud disabled; no credentials; fallback factory forbidden",
        runnerAssemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(Result).Assembly.Location))).ToLowerInvariant(),
        startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
        timingScope = "client call including request spacing/retries/network; not end-to-end game latency"
    },
    stoppedReason = stopped,
    wireRequests = audit.Requests,
    batches = batchObservations,
    fallbackProviderAttempts = TranslationServiceFactory.Attempts,
    attemptedCount = cases.Count(c => c.status != "not_attempted"),
    completedCount = cases.Count(c => c.status == "success"),
    results = cases
};
await JsonSerializer.SerializeAsync(output, record, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine($"Saved {record.completedCount}/60 translations; {record.attemptedCount} attempted. No Cloud API or key used.");
return record.completedCount == 60 ? 0 : 3;

internal sealed class Result
{
    public required string id { get; init; }
    public required string source { get; init; }
    public string? translation { get; set; }
    public string status { get; set; } = "not_attempted";
    public int? batchId { get; set; }
    public double? elapsedMsIncludingClientSpacing { get; set; }
    public string? failure { get; set; }
}
internal sealed class SingleBlock(GoogleTranslateFreeClient client) : ITranslationService
{
    public Task<string?> TranslateAsync(string jsonData, string prompt) =>
        client.TranslateAsync(jsonData, "en", "tr", TranslationRequestContext.Cancellation);
}

internal sealed class WebRequestAudit(Result[] cases) : DelegatingHandler(new TranslationHttpHandler())
{
    internal List<object> Requests { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
        bool translation = uri.Host == "translate.googleapis.com" && uri.AbsolutePath == "/translate_a/single" ||
            uri.Host == "translate.google.com.hk" && uri.AbsolutePath == "/translate_a/single";
        bool seed = uri.Host == "translate.google.com.hk" && uri.AbsolutePath == "/";
        if (request.Method != HttpMethod.Get || (!translation && !seed))
            throw new InvalidOperationException("Only the existing keyless Google web endpoints are allowed.");
        string? text = uri.Query.TrimStart('?').Split('&').Where(x => x.StartsWith("q=", StringComparison.Ordinal))
            .Select(x => Uri.UnescapeDataString(x[2..])).SingleOrDefault();
        string[] sourceIds = text == null ? [] : cases.Where(c => ("\n" + text + "\n").Contains("\n" + c.source + "\n", StringComparison.Ordinal))
            .Select(c => c.id).ToArray();
        var timer = Stopwatch.StartNew();
        var response = await base.SendAsync(request, token);
        Requests.Add(new { ordinal = Requests.Count + 1, host = uri.Host, path = uri.AbsolutePath,
            sourceText = text, sourceIds, status = (int)response.StatusCode,
            elapsedToHeadersMs = Math.Round(timer.Elapsed.TotalMilliseconds, 2) });
        return response;
    }
}
