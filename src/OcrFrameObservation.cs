using System.Collections.Generic;
using System.Text.Json;

namespace AlpWareStudio.Localay;

// One native OCR input owns one observation. UI capture ticks accumulate damage
// without touching its bitmap or starting another native recognition operation.
internal sealed class OcrFrameObservation(int width, int height)
{
    private readonly FrameChangeMap _damage = FrameChangeMap.Empty(width, height);
    internal void Include(FrameChangeMap changes) => _damage.Include(changes);
    internal bool IsCurrent(OcrSceneBlock block) =>
        !_damage.Intersects(block.X - 16, block.Y - 16, block.Width + 32, block.Height + 32);

    internal (JsonElement Results, bool FullyCurrent) FilterResults(JsonElement results)
    {
        var valid = new List<JsonElement>();
        bool complete = true;
        foreach (var item in results.EnumerateArray())
        {
            // Include background/eraser margin, not just glyph pixels. Bad geometry
            // cannot establish ownership of any portion of the current screen.
            if (OcrSceneSnapshot.TryReadBlock(item, 0, out var block) &&
                IsCurrent(block!)) valid.Add(item);
            else complete = false;
        }
        return (JsonSerializer.SerializeToElement(valid), complete);
    }
}
