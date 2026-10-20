namespace AlpWareStudio.Localay;

internal enum OcrLanguageState { Ready, Missing, Unsupported, Unavailable }
internal sealed record OcrLanguageStatus(string SourceCode, string? CapabilityTag, string? RecognizerTag,
    OcrLanguageState State, IReadOnlyList<string> InstalledTags)
{
    internal bool IsReady => State == OcrLanguageState.Ready;
}

internal static class OcrLanguageSetup
{
    // These tags select a Windows capability to query, not a promise that this OS offers it.
    // The installer verifies both capabilities exist BEFORE adding either one.
    private static readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US", ["ch_sim"] = "zh-CN", ["ch_tra"] = "zh-TW", ["ja"] = "ja-JP",
        ["ko"] = "ko-KR", ["es"] = "es-ES", ["fr"] = "fr-FR", ["de"] = "de-DE",
        ["it"] = "it-IT", ["ru"] = "ru-RU", ["pt"] = "pt-BR", ["ar"] = "ar-SA",
        ["bg"] = "bg-BG", ["nl"] = "nl-NL", ["hr"] = "hr-HR", ["pl"] = "pl-PL",
        ["ro"] = "ro-RO", ["fa"] = "fa-IR", ["cs"] = "cs-CZ", ["id"] = "id-ID",
        ["th"] = "th-TH", ["hu"] = "hu-HU", ["tr"] = "tr-TR", ["si"] = "si-LK",
        ["da"] = "da-DK", ["uk"] = "uk-UA", ["fi"] = "fi-FI", ["ckb"] = "ckb-IQ",
        ["bn"] = "bn-BD", ["el"] = "el-GR", ["he"] = "he-IL", ["vi"] = "vi-VN", ["hi"] = "hi-IN"
    };
    internal static string? CapabilityTag(string code)
    {
        if (Tags.TryGetValue(code, out var tag)) return tag;
        // Explicit regional variants remain explicit, rather than falling back to another language.
        return Tags.Values.Concat(new[] { "en-GB", "es-MX", "pt-PT", "zh-Hans-CN", "zh-Hant-TW" })
            .FirstOrDefault(t => t.Equals(code, StringComparison.OrdinalIgnoreCase)) switch
        {
            "zh-Hans-CN" => "zh-CN", "zh-Hant-TW" => "zh-TW", var value => value
        };
    }
    internal static string CanonicalCapabilityTag(string tag) =>
        Tags.Values.Concat(new[] { "en-GB", "es-MX", "pt-PT" })
            .FirstOrDefault(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown OCR capability tag.", nameof(tag));

    internal static OcrLanguageStatus Evaluate(string source, IEnumerable<string> installed, bool querySucceeded = true)
    {
        string[] tags = installed.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string? capability = CapabilityTag(source);
        if (capability == null) return new(source, null, null, OcrLanguageState.Unsupported, tags);
        if (!querySucceeded) return new(source, capability, null, OcrLanguageState.Unavailable, tags);
        bool simplified = capability == "zh-CN", traditional = capability == "zh-TW";
        string? selected = tags.FirstOrDefault(t => t.Equals(capability, StringComparison.OrdinalIgnoreCase));
        selected ??= tags.FirstOrDefault(t =>
        {
            if (simplified) return t.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) || t.Equals("zh-SG", StringComparison.OrdinalIgnoreCase);
            if (traditional) return t.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || t.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) || t.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);
            // The app's generic language choices accept an installed regional variant of THAT language.
            return Tags.ContainsKey(source) && t.Split('-')[0].Equals(capability.Split('-')[0], StringComparison.OrdinalIgnoreCase);
        });
        return new(source, capability, selected, selected == null ? OcrLanguageState.Missing : OcrLanguageState.Ready, tags);
    }
}
