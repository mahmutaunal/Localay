using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlpWareStudio.Localay;

internal sealed record OcrSceneBlock(string Text, double X, double Y, double Width, double Height);

// OCR geometry belongs to a screen observation, independently of the translation cache.
internal sealed class OcrSceneSnapshot
{
    private readonly OcrSceneBlock[] _blocks;
    internal OcrSceneSnapshot(IEnumerable<OcrSceneBlock> blocks) => _blocks = blocks.ToArray();
    internal bool IsEmpty => _blocks.Length == 0;

    internal bool HasSameText(OcrSceneSnapshot other) =>
        _blocks.Select(b => b.Text).SequenceEqual(other._blocks.Select(b => b.Text), StringComparer.Ordinal);

    internal bool HasSameLayout(OcrSceneSnapshot other) => HasSameText(other) && HasSameGeometry(other);

    internal bool HasSameGeometry(OcrSceneSnapshot other)
    {
        if (_blocks.Length != other._blocks.Length) return false;
        // Compare against the rendered scene, not the preceding frame, so small movements
        // accumulate into a real update instead of drifting indefinitely.
        const double tolerance = 2;
        return _blocks.Zip(other._blocks).All(pair =>
            Math.Abs(pair.First.X - pair.Second.X) <= tolerance &&
            Math.Abs(pair.First.Y - pair.Second.Y) <= tolerance &&
            Math.Abs(pair.First.Width - pair.Second.Width) <= tolerance &&
            Math.Abs(pair.First.Height - pair.Second.Height) <= tolerance);
    }

    internal static OcrSceneSnapshot FromResults(JsonElement results, int minimumTextLength)
    {
        var blocks = new List<OcrSceneBlock>();
        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("text", out var textValue) ||
                !item.TryGetProperty("confidence", out _) ||
                !item.TryGetProperty("rect", out var rect)) continue;
            string text = textValue.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(text) || text.Length < minimumTextLength) continue;
            var points = rect.EnumerateArray().Select(p => (X: p[0].GetDouble(), Y: p[1].GetDouble())).ToArray();
            if (points.Length == 0 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) continue;
            double x = points.Min(p => p.X), y = points.Min(p => p.Y);
            blocks.Add(new(text, x, y, points.Max(p => p.X) - x, points.Max(p => p.Y) - y));
        }
        return new(blocks);
    }
}

// Accessed on the UI thread. Keep only the latest observation while a translation is busy.
internal sealed class OcrSceneState
{
    internal OcrSceneSnapshot? Latest { get; private set; }
    internal OcrSceneSnapshot? Rendered { get; private set; }
    private string? _deferred;
    internal bool IsTranslating { get; private set; }
    internal bool TryBeginTranslation()
    {
        if (IsTranslating) return false;
        IsTranslating = true;
        return true;
    }
    internal void EndTranslation() => IsTranslating = false;
    internal bool HasDeferred => _deferred != null;
    internal bool IsCurrent => Latest != null && Rendered != null && Rendered.HasSameLayout(Latest);
    internal bool Observe(OcrSceneSnapshot snapshot)
    {
        bool contentChanged = Latest != null && !Latest.HasSameText(snapshot);
        Latest = snapshot;
        return contentChanged;
    }
    internal void MarkRendered(OcrSceneSnapshot snapshot) => Rendered = snapshot;
    internal void Defer(string data) => _deferred = data;
    internal string? TakeDeferred()
    {
        string? data = _deferred;
        _deferred = null;
        return data;
    }
    internal void Reset()
    {
        // Clearing presentation does not finish an outstanding asynchronous request.
        Latest = Rendered = null;
        _deferred = null;
    }
}
