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
    internal IReadOnlyList<OcrSceneBlock> Blocks => _blocks;
    internal bool IsEmpty => _blocks.Length == 0;
    internal int BlockCount => _blocks.Length;

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
            if (TryReadBlock(item, minimumTextLength, out var block)) blocks.Add(block!);
        return new(blocks);
    }

    internal static bool TryReadBlock(JsonElement item, int minimumTextLength, out OcrSceneBlock? block)
    {
        block = null;
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("confidence", out _) || !item.TryGetProperty("rect", out var rect) ||
            rect.ValueKind != JsonValueKind.Array) return false;
        string text = textValue.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(text) || text.Length < minimumTextLength) return false;
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (var p in rect.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2 ||
                p[0].ValueKind != JsonValueKind.Number || p[1].ValueKind != JsonValueKind.Number ||
                !p[0].TryGetDouble(out double x) || !p[1].TryGetDouble(out double y) ||
                !double.IsFinite(x) || !double.IsFinite(y)) return false;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right <= left || bottom <= top) return false;
        block = new(text, left, top, right - left, bottom - top);
        return true;
    }

}

// Accessed on the UI thread. Keep only the latest observation while a translation is busy.
internal sealed class OcrSceneState
{
    internal OcrSceneSnapshot? Latest { get; private set; }
    internal OcrSceneSnapshot? Rendered { get; private set; }
    private string? _deferred;
    private long _nextTranslationId;
    private long? _translationOwner;
    internal long Generation { get; private set; }
    internal bool IsTranslating => _translationOwner.HasValue;
    internal bool TryBeginTranslation(out long operationId)
    {
        operationId = 0;
        if (IsTranslating) return false;
        operationId = ++_nextTranslationId;
        _translationOwner = operationId;
        return true;
    }
    internal bool EndTranslation(long operationId)
    {
        if (_translationOwner != operationId) return false;
        _translationOwner = null;
        return true;
    }
    // Invalidation releases the UI immediately, even if an old provider takes
    // longer to observe cancellation. Its finally block no longer owns this state.
    internal void CancelTranslation()
    {
        _translationOwner = null;
        _deferred = null;
        Generation++;
    }
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
