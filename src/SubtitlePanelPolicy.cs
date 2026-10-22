namespace AlpWareStudio.Localay;

internal sealed record SubtitlePanelPlan(OverlayBox Bounds, double FontSize, IReadOnlyList<int> Members);

// Geometry and source-text evidence only. Uncertain regions retain their ordinary UI boxes.
// A lower-center location alone is insufficient: require different text at the same anchor.
internal sealed class SubtitlePanelPolicy
{
    private double _width, _height;
    private OverlayBox? _candidate, _panel;
    private string _lastText = "";
    private int _observations;

    private static double ValidHeight(double height) => double.IsFinite(height) && height > 0 ? height : 900;
    internal static double UiFontSize(double height) => Math.Round(Math.Clamp(ValidHeight(height) / 48, 16, 26));
    internal static double CaptionFontSize(double height) => Math.Round(Math.Clamp(ValidHeight(height) / 36, 18, 34));

    private static bool IsCaptionCandidate(OverlayBox box, string text, double width, double height) =>
        box.IsValid && box.X >= 0 && box.Right <= width && box.Bottom <= height * .98 &&
        box.Bottom >= height * .78 && box.Y >= height * .65 && box.Height <= height * .13 &&
        box.Width >= width * .20 && box.Width <= width * .78 &&
        Math.Abs(box.X + box.Width / 2 - width / 2) <= width * .12 &&
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 16;

    // A scheduling hint only: unlike panel admission, this does not mask pixels or
    // claim that a lower-center paragraph has already been confirmed as dialogue.
    internal static IReadOnlyList<int> TranslationOrder(IReadOnlyList<OverlayBox> sources,
        IReadOnlyList<string> texts, double width, double height)
    {
        var original = Enumerable.Range(0, texts.Count).ToArray();
        if (sources.Count != texts.Count || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return original;
        var priority = original.Where(i => IsCaptionCandidate(sources[i], texts[i], width, height))
            .Select(i => texts[i]).ToHashSet(StringComparer.Ordinal);
        // Stable partition also handles a repeated source string: the coordinator's
        // exact-text deduplication must not leave its caption occurrence in a later batch.
        return original.OrderByDescending(i => priority.Contains(texts[i])).ToArray();
    }

    internal void Reset()
    {
        _candidate = _panel = null;
        _lastText = "";
        _observations = 0;
        _width = _height = 0;
    }

    internal SubtitlePanelPlan? Observe(IReadOnlyList<OverlayBox> sources, IReadOnlyList<string> texts,
        double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || sources.Count != texts.Count)
        { Reset(); return null; }
        if (_width != width || _height != height) { Reset(); _width = width; _height = height; }
        var members = Enumerable.Range(0, sources.Count).Where(i =>
        {
            var box = sources[i];
            return IsCaptionCandidate(box, texts[i], width, height);
        }).OrderBy(i => sources[i].Y).ThenBy(i => sources[i].X).ToArray();
        if (members.Length is 0 or > 3) return null;
        for (int i = 1; i < members.Length; i++)
        {
            var previous = sources[members[i - 1]];
            var next = sources[members[i]];
            if (next.Y - previous.Bottom > Math.Max(previous.Height, next.Height) * 1.5) return null;
        }
        double left = members.Min(i => sources[i].X), top = members.Min(i => sources[i].Y);
        double right = members.Max(i => sources[i].Right), bottom = members.Max(i => sources[i].Bottom);
        var region = new OverlayBox(left, top, right - left, bottom - top);
        if (region.Height > height * .14) return null;
        string text = string.Join(" ", string.Join(" ", members.Select(i => texts[i]))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        bool sameAnchor = _candidate is { } prior && Math.Abs(prior.Bottom - bottom) <= height * .035 &&
            Math.Abs(prior.X + prior.Width / 2 - (left + right) / 2) <= width * .06;
        if (!sameAnchor)
        {
            _candidate = region;
            _panel = null;
            _observations = 1;
        }
        else if (text != _lastText && !text.StartsWith(_lastText, StringComparison.Ordinal) &&
                 !_lastText.StartsWith(text, StringComparison.Ordinal))
            _observations = Math.Min(2, _observations + 1);
        _lastText = text;
        if (_observations < 2) return null;

        double font = CaptionFontSize(height);
        double panelWidth = width * .82, panelHeight = Math.Ceiling(font * OverlayFontPolicy.LineHeightRatio * 3 + 16);
        if (panelHeight > height * .22) return null;
        _panel ??= new((width - panelWidth) / 2, Math.Max(0, bottom + 8 - panelHeight), panelWidth, panelHeight);
        var panel = _panel.Value;
        if (panel.Bottom > height || members.Any(i => !Contains(panel, sources[i]))) return null;
        // The panel may cover only its own source text, never HUD, prompts or menu labels.
        if (Enumerable.Range(0, sources.Count).Any(i => !members.Contains(i) && Overlaps(panel, sources[i]))) return null;
        return new(panel, font, members);
    }

    internal static bool Contains(OverlayBox outer, OverlayBox inner) => outer.IsValid && inner.IsValid &&
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    internal static bool Overlaps(OverlayBox first, OverlayBox second) => first.IsValid && second.IsValid &&
        Math.Min(first.Right, second.Right) - Math.Max(first.X, second.X) > 2 &&
        Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y) > 2;
}
