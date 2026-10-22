using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

// One published model tag; arbitrary Ollama models must not inherit this protocol.
internal static class HyMt2Profile
{
    internal const string Model = "hf.co/tencent/Hy-MT2-1.8B-GGUF:Q4_K_M";
    internal const string TemplateVersion = "hymt2-plain-v1";
    internal static bool IsModel(string? model) => string.Equals(model, Model, StringComparison.Ordinal);
    internal static bool IsSelected(string service, string? model) => service == "Ollama" && IsModel(model);

    // Names, not just codes, are accepted because Logic serializes full language names.
    private static readonly Dictionary<string, string> Languages = BuildLanguages();
    private static Dictionary<string, string> BuildLanguages()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in new (string, string)[] {
            ("zh", "Chinese"), ("en", "English"), ("fr", "French"), ("pt", "Portuguese"),
            ("es", "Spanish"), ("ja", "Japanese"), ("tr", "Turkish"), ("ru", "Russian"),
            ("ar", "Arabic"), ("ko", "Korean"), ("th", "Thai"), ("it", "Italian"),
            ("de", "German"), ("vi", "Vietnamese"), ("ms", "Malay"), ("id", "Indonesian"),
            ("tl", "Filipino"), ("hi", "Hindi"), ("zh-Hant", "Traditional Chinese"),
            ("pl", "Polish"), ("cs", "Czech"), ("nl", "Dutch"), ("km", "Khmer"),
            ("my", "Burmese"), ("fa", "Persian"), ("gu", "Gujarati"), ("ur", "Urdu"),
            ("te", "Telugu"), ("mr", "Marathi"), ("he", "Hebrew"), ("bn", "Bengali"),
            ("ta", "Tamil"), ("uk", "Ukrainian"), ("bo", "Tibetan"), ("kk", "Kazakh"),
            ("mn", "Mongolian"), ("ug", "Uyghur"), ("yue", "Cantonese") })
        { result[code] = name; result[name] = name; }
        result["ch_sim"] = result["zh-Hans"] = result["Simplified Chinese"] = "Chinese";
        result["ch_tra"] = "Traditional Chinese";
        result["Farsi"] = "Persian";
        result["ThaiLand"] = "Thai";
        return result;
    }

    internal static string? LanguageName(string? language) =>
        language != null && Languages.TryGetValue(language.Trim(), out var name) ? name : null;
    internal static bool SupportsPair(string? source, string? target) =>
        LanguageName(source) is { } s && LanguageName(target) is { } t && s != t;

    internal static string Prompt(string text, string target) =>
        $"Translate the following text into {target}. Note that you should only output the translated result without any additional explanation. " +
        "This is game text. Preserve variable placeholders and markup exactly.\n\n" + text;

    private static readonly Regex Tokens = new(@"\{\{[^{}\r\n]+\}\}|\{[^{}\r\n]+\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.(?:\d+|\*))?[a-zA-Z]|</?[A-Za-z][^>\r\n]*>", RegexOptions.CultureInvariant);
    internal static bool PreservesTokens(string source, string translated) =>
        Tokens.Matches(source).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)
            .SequenceEqual(Tokens.Matches(translated).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal), StringComparer.Ordinal);
}
