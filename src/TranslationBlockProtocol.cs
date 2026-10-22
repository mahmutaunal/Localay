using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlpWareStudio.Localay;

internal sealed record TranslationBlock(string Id, string Text);
internal sealed record TranslationBlockResult(string? Text, TranslationFailure? Failure = null);
internal sealed record TranslationBatchResult(IReadOnlyDictionary<string, string> Translations, TranslationFailure? Failure = null);

// A batch owns its wire IDs. Neither output order nor an unrequested numeric ID
// authorizes an assignment. Valid partial replies keep only their explicit IDs.
internal static class TranslationBlockProtocol
{
    internal const string Version = "id-v1";
    internal const string PromptRule = "For block_protocol=id-v1, return one text_blocks item per requested ID. " +
        "Preserve each ID exactly; translate each block independently using previous_context and game_info for meaning. " +
        "Do not merge, split or reorder content between IDs. Do not insert block separators; any separator already inside a source block is literal content.";

    internal static string CacheContext(string settings, object previousContext) =>
        JsonSerializer.Serialize(new { protocol = Version, settings, previousContext });
    internal static bool IsEnabled(JsonElement root) => root.TryGetProperty("block_protocol", out var version) &&
        version.ValueKind == JsonValueKind.String && version.GetString() == Version;

    internal static TranslationBlock[] ReadBlocks(JsonElement root)
    {
        if (!root.TryGetProperty("text_blocks", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing text blocks.");
        var blocks = new List<TranslationBlock>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || !seen.Add(id.GetString()!) ||
                !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(text.GetString())) throw new JsonException("Invalid text block.");
            blocks.Add(new(id.GetString()!, text.GetString()!));
        }
        return blocks.ToArray();
    }

    // Array-based providers define positional correspondence inside their own API.
    // Establish wire IDs here only after validating the entire response count.
    internal static string? DirectResponse(IReadOnlyList<TranslationBlock> blocks, IReadOnlyList<string?>? values)
    {
        if (values == null || blocks.Count != values.Count || !values.Any(v => !string.IsNullOrWhiteSpace(v))) return null;
        return JsonSerializer.Serialize(new { translations = blocks.Select((b, i) =>
            new { id = b.Id, original_text = b.Text, translated_text = values[i] ?? "" }) });
    }

    internal static Dictionary<string, string> Parse(string? innerJson, bool translationsFormat,
        IReadOnlyList<TranslationBlock> requested)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(innerJson)) return result;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in requested)
            if (!expected.TryAdd(block.Id, block.Text)) return result;
        try
        {
            using var doc = JsonDocument.Parse(innerJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(translationsFormat ? "translations" : "text_blocks", out var items) ||
                items.ValueKind != JsonValueKind.Array) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idValue) ||
                    idValue.ValueKind != JsonValueKind.String) return new(StringComparer.Ordinal);
                string id = idValue.GetString()!;
                if (!expected.TryGetValue(id, out var source) || !seen.Add(id)) return new(StringComparer.Ordinal);
                // Adapters returning source metadata must agree with the actual request.
                if (item.TryGetProperty("original_text", out var original) &&
                    (original.ValueKind != JsonValueKind.String || original.GetString() != source)) return new(StringComparer.Ordinal);
                if (!item.TryGetProperty(translationsFormat ? "translated_text" : "text", out var text) ||
                    text.ValueKind != JsonValueKind.String) continue;
                string value = text.GetString()!;
                if (!string.IsNullOrWhiteSpace(value)) result.Add(id, value);
            }
        }
        catch (JsonException) { result.Clear(); }
        return result;
    }
}
