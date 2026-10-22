namespace AlpWareStudio.Localay;

internal sealed record CapturePreferenceValues(string OcrMethod, int ScreenIndex,
    bool AutoOcr, bool AutoTranslate, bool MultipleAreas, bool MangaMode)
{
    internal string? Validate(IReadOnlyCollection<int> availableScreens, bool oneOcrAvailable)
    {
        if (OcrMethod is not ("Windows OCR" or "OneOCR")) return "Draft_CaptureOcrInvalid";
        if (OcrMethod == "OneOCR" && !oneOcrAvailable) return "Draft_CaptureOneOcrUnavailable";
        if (!availableScreens.Contains(ScreenIndex)) return "Draft_CaptureScreenRequired";
        if (MangaMode && OcrMethod != "OneOCR") return "Draft_CaptureMangaUnavailable";
        return null;
    }
}

internal sealed class CapturePreferenceDraft
{
    internal CapturePreferenceValues Baseline { get; private set; }
    internal CapturePreferenceValues Value { get; private set; }
    internal bool IsDirty => Value != Baseline;
    internal CapturePreferenceDraft(CapturePreferenceValues saved) => Baseline = Value = saved;
    internal void SetOcrMethod(string method) => Value = Value with { OcrMethod = method, MangaMode = method == "OneOCR" && Value.MangaMode };
    internal void SetScreen(int index) => Value = Value with { ScreenIndex = index };
    internal void SetAutoOcr(bool enabled) => Value = Value with { AutoOcr = enabled };
    internal void SetAutoTranslate(bool enabled) => Value = Value with { AutoTranslate = enabled };
    internal void SetMultipleAreas(bool enabled) => Value = Value with { MultipleAreas = enabled };
    internal void SetMangaMode(bool enabled) => Value = Value with { MangaMode = enabled };
    internal bool HasConflict(CapturePreferenceValues current) => IsDirty && current != Baseline;
    internal void Observe(CapturePreferenceValues current) { if (!IsDirty) Reset(current); }
    internal void Reset(CapturePreferenceValues current) => Baseline = Value = current;
}
