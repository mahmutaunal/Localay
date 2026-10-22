using System.Globalization;

namespace AlpWareStudio.Localay;

// Keep numeric input as text in the draft so incomplete/invalid edits survive focus changes.
internal sealed record GeneralPreferenceValues(string Language, string Theme,
    bool ClipboardEnabled = false, bool ClipboardCopyResult = true,
    string ClipboardDebounce = "300", string ClipboardMaxChars = "5000", bool ClipboardConsent = false)
{
    internal string? ValidationIssue
    {
        get
        {
            if (!int.TryParse(ClipboardDebounce, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay) || delay < 100 || delay > 2000)
                return "Draft_ClipboardDelayInvalid";
            if (!int.TryParse(ClipboardMaxChars, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) || limit < 10 || limit > 10000)
                return "Draft_ClipboardLimitInvalid";
            if (ClipboardEnabled && !ClipboardConsent) return "Draft_ClipboardConsentRequired";
            return null;
        }
    }
}

internal sealed class GeneralPreferenceDraft
{
    internal GeneralPreferenceValues Baseline { get; private set; }
    internal GeneralPreferenceValues Value { get; private set; }
    internal bool IsDirty => Value != Baseline;

    internal GeneralPreferenceDraft(GeneralPreferenceValues saved) => Baseline = Value = saved;
    internal void SetLanguage(string language) => Value = Value with { Language = PreferenceRules.NormalizeLanguagePreference(language) };
    internal void SetTheme(string theme) => Value = Value with { Theme = PreferenceRules.ResolveThemeMode(theme) };
    internal void SetClipboardEnabled(bool enabled) => Value = Value with { ClipboardEnabled = enabled, ClipboardConsent = enabled && Value.ClipboardConsent };
    internal void SetClipboardCopyResult(bool enabled) => Value = Value with { ClipboardCopyResult = enabled };
    internal void SetClipboardConsent(bool consent) => Value = Value with { ClipboardConsent = Value.ClipboardEnabled && consent };
    internal void SetClipboardDebounce(string text) => Value = Value with { ClipboardDebounce = text };
    internal void SetClipboardMaxChars(string text) => Value = Value with { ClipboardMaxChars = text };
    internal bool HasConflict(GeneralPreferenceValues current) => IsDirty && current != Baseline;

    internal void Observe(GeneralPreferenceValues current)
    {
        if (!IsDirty) Reset(current);
    }

    internal void Reset(GeneralPreferenceValues current) => Baseline = Value = current;
}
