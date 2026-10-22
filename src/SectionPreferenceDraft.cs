using System.Globalization;

namespace AlpWareStudio.Localay;

// Shared draft for the remaining preference pages. Raw input stays editable until Save.
internal sealed class SectionPreferenceDraft
{
    private Dictionary<string, string> _baseline;
    private Dictionary<string, string> _values;
    internal SectionPreferenceDraft(IReadOnlyDictionary<string, string> values) { _baseline = new(values); _values = new(values); }
    internal IReadOnlyDictionary<string, string> Baseline => new Dictionary<string, string>(_baseline);
    internal IReadOnlyDictionary<string, string> Values => new Dictionary<string, string>(_values);
    internal string this[string key] => _values[key];
    internal bool IsDirty => !Same(_baseline, _values);
    internal bool HasConflict(IReadOnlyDictionary<string, string> current) => IsDirty && !Same(_baseline, current);
    internal void Set(string key, string value)
    {
        if (!_baseline.ContainsKey(key)) throw new ArgumentException("Unknown preference.", nameof(key));
        _values[key] = value;
    }
    internal void Reset(IReadOnlyDictionary<string, string> current) { _baseline = new(current); _values = new(current); }
    internal void Observe(IReadOnlyDictionary<string, string> current) { if (!IsDirty) Reset(current); }
    internal static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && value == p.Value);

    internal static readonly IReadOnlyDictionary<string, string> AdvancedDefaults = new Dictionary<string, string>
    {
        ["block_detection_scale"] = "5", ["line_spacing_factor"] = "0.63", ["block_detection_settle_time"] = "0.15",
        ["textsimilar_threshold"] = "0.75", ["char_level"] = "false", ["min_text_fragment_size"] = "2",
        ["min_letter_confidence"] = "0.1", ["min_line_confidence"] = "0.2", ["hdr_support"] = "false",
        ["max_context_pieces"] = "3", ["min_context_size"] = "20", ["chatbox_min_text_size"] = "2",
        ["game_info"] = "", ["llm_prefer_speed"] = "true", ["translation_cache_enabled"] = "true",
        ["translation_prefetch"] = "true", ["ignore_phrases"] = ""
    };
    internal static readonly IReadOnlyDictionary<string, (double Min, double Max, bool Integer)> Ranges =
        new Dictionary<string, (double, double, bool)>
        {
            ["block_detection_scale"] = (0.01, 100, false), ["line_spacing_factor"] = (0.01, 100, false),
            ["block_detection_settle_time"] = (0, 60, false), ["textsimilar_threshold"] = (0.5, 1, false),
            ["min_text_fragment_size"] = (1, 100000, true), ["min_letter_confidence"] = (0, 1, false),
            ["min_line_confidence"] = (0, 1, false), ["max_context_pieces"] = (0, 10000, true),
            ["min_context_size"] = (0, 100000, true), ["chatbox_min_text_size"] = (0, 100000, true)
        };
    internal static string? InvalidAdvancedField(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count != AdvancedDefaults.Count || AdvancedDefaults.Keys.Any(k => !values.ContainsKey(k))) return "";
        foreach (var pair in Ranges)
        {
            if (!double.TryParse(values[pair.Key], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number) || number < pair.Value.Min || number > pair.Value.Max ||
                (pair.Value.Integer && !int.TryParse(values[pair.Key], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return pair.Key;
        }
        foreach (var pair in values)
        {
            if ((AdvancedDefaults[pair.Key] is "true" or "false") && pair.Value is not ("true" or "false")) return pair.Key;
            if (pair.Key == "game_info")
            {
                if (pair.Value.Length > 32000 || pair.Value.Contains("<game_info_end>", StringComparison.OrdinalIgnoreCase)) return pair.Key;
            }
            else if (pair.Key != "ignore_phrases" && pair.Value.IndexOfAny(['|', '\r', '\n']) >= 0) return pair.Key;
        }
        return null;
    }
}
