using System.Globalization;
using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

internal sealed class ResultPreferenceDraft
{
    internal static readonly Dictionary<string, string> Defaults = new()
    {
        ["OverlayBackgroundColor"] = "#FF000000", ["OverlayTextColor"] = "#FFFFFFFF",
        ["language_font_family"] = "Arial", ["language_font_override"] = "false",
        ["language_font_size_min"] = "10", ["language_font_size_max"] = "68",
        ["IsAutoSetOverlayBackground"] = "true", ["show_icon_signal"] = "true",
        ["AutoMergeOverlappingText"] = "true", ["leave_translation_onscreen"] = "false",
        ["chatbox_background_color"] = "#FF000000", ["chatbox_background_opacity"] = "0.5",
        ["chatbox_window_opacity"] = "1", ["chatbox_font_family"] = "Segoe UI", ["chatbox_font_size"] = "14",
        ["chatbox_original_text_color"] = "#FFF8E0A0", ["chatbox_translated_text_color"] = "#FFFFFFFF",
        ["chatbox_text_outline_color"] = "#FF000000", ["auto_clear_chat_history"] = "false",
        ["auto_clear_chat_timeout"] = "30", ["chatbox_recreate_on_show"] = "false"
    };
    internal static readonly string[] ColorFields = ["OverlayBackgroundColor", "OverlayTextColor", "chatbox_background_color", "chatbox_original_text_color", "chatbox_translated_text_color", "chatbox_text_outline_color"];
    private Dictionary<string, string> _baseline;
    private Dictionary<string, string> _values;
    internal ResultPreferenceDraft(IReadOnlyDictionary<string, string> saved) { _baseline = new(saved); _values = new(saved); }
    internal string this[string key] => _values[key];
    internal bool IsDirty => !Same(_baseline, _values);
    internal IReadOnlyDictionary<string, string> Baseline => new Dictionary<string, string>(_baseline);
    internal static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out string? value) && value == p.Value);
    internal bool HasConflict(IReadOnlyDictionary<string, string> current) => IsDirty && !Same(_baseline, current);
    internal void Set(string key, string value)
    {
        if (!Defaults.ContainsKey(key)) throw new ArgumentException("Unknown output preference.", nameof(key));
        _values[key] = value;
    }
    internal void Observe(IReadOnlyDictionary<string, string> current) { if (!IsDirty) Reset(current); }
    internal void Reset(IReadOnlyDictionary<string, string> current) { _baseline = new(current); _values = new(current); }
    internal void UseDefaults() => _values = new(Defaults);
    internal IReadOnlyDictionary<string, string> Candidate()
    {
        var copy = new Dictionary<string, string>(_values);
        foreach (string key in ColorFields)
        {
            string value = copy[key].Trim().ToUpperInvariant();
            copy[key] = value.Length == 7 ? "#FF" + value[1..] : value;
        }
        foreach (string key in new[] { "language_font_family", "chatbox_font_family" }) copy[key] = copy[key].Trim();
        return copy;
    }
    internal static double ChatBackgroundOpacity(double opacity) => opacity >= 0.95 ? 1 : 0.7 * Math.Sqrt(opacity) + 0.3 * opacity;
    internal static bool TryNumber(string value, out double number) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    internal static string? Validate(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count != Defaults.Count || Defaults.Keys.Any(k => !values.ContainsKey(k))) return "Draft_ResultInvalid";
        foreach (string key in ColorFields)
            if (!Regex.IsMatch(values[key].Trim(), "^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")) return "Draft_ResultColorInvalid";
        if (!TryNumber(values["language_font_size_min"], out double min) || !TryNumber(values["language_font_size_max"], out double max) || min < 1 || max > 256 || min > max)
            return "Draft_ResultFontRangeInvalid";
        if (!TryNumber(values["chatbox_font_size"], out double size) || size < 8 || size > 48) return "Draft_ResultChatSizeInvalid";
        if (!TryNumber(values["chatbox_background_opacity"], out double background) || background < 0 || background > 1 ||
            !TryNumber(values["chatbox_window_opacity"], out double window) || window < 0.1 || window > 1) return "Draft_ResultOpacityInvalid";
        if (!int.TryParse(values["auto_clear_chat_timeout"], NumberStyles.None, CultureInfo.InvariantCulture, out int timeout) || timeout < 0 || timeout > 60)
            return "Draft_ResultTimeoutInvalid";
        foreach (string key in new[] { "language_font_family", "chatbox_font_family" })
            if (string.IsNullOrWhiteSpace(values[key]) || values[key].Length > 128) return "Draft_ResultFontInvalid";
        foreach (var value in values)
        {
            if ((Defaults[value.Key] is "true" or "false") && value.Value is not ("true" or "false")) return "Draft_ResultInvalid";
            if (value.Value.IndexOfAny(new[] { '\r', '\n', '|' }) >= 0) return "Draft_ResultInvalid";
        }
        return null;
    }
}
