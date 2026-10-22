using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay;

// IDs stay outside the model. The coordinator sends one source per game call so
// a ready caption is not held behind fifteen sequential local generations.
internal sealed class HyMt2Client(HttpClient client, Uri generateEndpoint, string model)
{
    internal async Task<string?> TranslateAsync(string json, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!HyMt2Profile.IsModel(model)) throw new ArgumentException("Unexpected Hy-MT2 model tag.");
        using var input = JsonDocument.Parse(json);
        var root = input.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TranslationBlockProtocol.IsEnabled(root))
            throw new ArgumentException("Hy-MT2 requires the screen block protocol.");
        string source = root.GetProperty("source_language").GetString() ?? "";
        string target = root.GetProperty("target_language").GetString() ?? "";
        if (!HyMt2Profile.SupportsPair(source, target)) throw new ArgumentException("Unsupported Hy-MT2 language pair.");
        var blocks = TranslationBlockProtocol.ReadBlocks(root);
        if (blocks.Length is 0 or > 15) throw new ArgumentException("Invalid local batch size.");
        foreach (var block in blocks)
            if (Encoding.UTF8.GetByteCount(block.Text) > 4096)
                throw new ArgumentException("Local translation input exceeds the profile limit.");

        var values = new string?[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var payload = new {
                model, stream = false, keep_alive = "5m",
                messages = new[] { new { role = "user", content = HyMt2Profile.Prompt(blocks[i].Text, HyMt2Profile.LanguageName(target)!) } },
                // Publisher sampling parameters; output/context are bounded by this adapter.
                options = new { temperature = 0.7, top_p = 0.6, top_k = 20, repeat_penalty = 1.05, num_predict = 2048, num_ctx = 8192 }
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(generateEndpoint, "chat"), content, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                TranslationRequestContext.ReportFailure(TranslationFailure.FromStatus(response));
                return TranslationBlockProtocol.DirectResponse(blocks, values);
            }
            try
            {
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
                var reply = result.RootElement;
                if (reply.ValueKind != JsonValueKind.Object ||
                    !reply.TryGetProperty("model", out var replyModel) || replyModel.ValueKind != JsonValueKind.String || replyModel.GetString() != model ||
                    !reply.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
                    !reply.TryGetProperty("done_reason", out var reason) || reason.ValueKind != JsonValueKind.String || reason.GetString() != "stop" ||
                    !reply.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                    !message.TryGetProperty("content", out var translated) || translated.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(translated.GetString()) || !HyMt2Profile.PreservesTokens(blocks[i].Text, translated.GetString()!))
                {
                    TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                    return TranslationBlockProtocol.DirectResponse(blocks, values);
                }
                values[i] = translated.GetString()!.Trim();
            }
            catch (JsonException)
            {
                TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                return TranslationBlockProtocol.DirectResponse(blocks, values);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return TranslationBlockProtocol.DirectResponse(blocks, values);
    }
}
