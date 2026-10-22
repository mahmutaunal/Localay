using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay;

// Translation-specific protocol. IDs never enter the language model's prompt.
internal sealed class TranslateGemmaClient(HttpClient client, Uri generateEndpoint, string model)
{
    internal const string TemplateVersion = "translategemma-en-tr-v1";
    internal static bool IsCandidate(string model) => model == "translategemma:4b";

    internal static void RequireEnglishTurkish(JsonElement root)
    {
        string source = root.GetProperty("source_language").GetString() ?? "";
        string target = root.GetProperty("target_language").GetString() ?? "";
        if (!(source.Equals("en", StringComparison.OrdinalIgnoreCase) || source.Equals("english", StringComparison.OrdinalIgnoreCase)) ||
            !(target.Equals("tr", StringComparison.OrdinalIgnoreCase) || target.Equals("turkish", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This local profile supports English to Turkish only.");
    }

    internal static string Prompt(string text) =>
        "You are a professional English (en) to Turkish (tr) translator. Your goal is to accurately convey the meaning and nuances of the original English text while adhering to Turkish grammar, vocabulary, and cultural sensitivities.\n" +
        "Produce only the Turkish translation, without any additional explanations or commentary. Please translate the following English text into Turkish:\n\n\n" + text;

    internal async Task<string?> TranslateAsync(string json, CancellationToken cancellation)
    {
        using var input = JsonDocument.Parse(json);
        if (!TranslationBlockProtocol.IsEnabled(input.RootElement))
            throw new ArgumentException("TranslateGemma requires the screen block protocol.");
        RequireEnglishTurkish(input.RootElement);
        var blocks = TranslationBlockProtocol.ReadBlocks(input.RootElement);
        if (blocks.Length is 0 or > 15) throw new ArgumentException("Invalid local batch size.");
        // Reject the batch before doing any inference; never silently truncate a source.
        foreach (var block in blocks)
            if (block.Text.Length > 4096) throw new ArgumentException("Local translation input exceeds the profile limit.");
        var values = new string?[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var payload = new
            {
                model, stream = false, keep_alive = "5m",
                messages = new[] { new { role = "user", content = Prompt(blocks[i].Text) } },
                // Bounded deterministic inference. Hitting the limit is a failure, not a translation.
                options = new { temperature = 0, num_predict = 1024, num_ctx = 8192 }
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(generateEndpoint, "chat"), content, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                TranslationRequestContext.ReportFailure(TranslationFailure.FromStatus(response));
                return TranslationBlockProtocol.DirectResponse(blocks, values);
            }
            JsonDocument result;
            try { result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)); }
            catch (JsonException)
            {
                TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                return TranslationBlockProtocol.DirectResponse(blocks, values);
            }
            using (result)
            {
                var root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("done_reason", out var reason) || reason.ValueKind != JsonValueKind.String || reason.GetString() != "stop" ||
                    !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                    !message.TryGetProperty("content", out var translated) || translated.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(translated.GetString()))
                {
                    TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                    return TranslationBlockProtocol.DirectResponse(blocks, values);
                }
                values[i] = translated.GetString()!.Trim();
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return TranslationBlockProtocol.DirectResponse(blocks, values);
    }
}
