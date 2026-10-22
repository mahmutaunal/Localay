namespace AlpWareStudio.Localay;

internal static class RenderingResourceChecks
{
    internal static void Run(Action<bool, string> Check)
    {
        int created = 0;
        var cache = new SingleResourceCache<string, Resource>(r => r.Dispose());
        Resource Create() { created++; return new(); }
        var first = cache.GetOrCreate("same-size", Create);
        bool reused = Enumerable.Range(0, 60).All(_ => ReferenceEquals(first, cache.GetOrCreate("same-size", Create)));
        Check(reused && created == 1 && first.Disposals == 0, "a stable capture does not allocate a native resource per frame");
        try { cache.GetOrCreate("resize", () => throw new InvalidOperationException("allocation failed")); }
        catch (InvalidOperationException) { }
        Check(ReferenceEquals(first, cache.GetOrCreate("same-size", Create)) && first.Disposals == 0,
            "failed replacement keeps the previous resource alive and valid");
        var second = cache.GetOrCreate("resize", Create);
        Check(first.Disposals == 1 && second.Disposals == 0 && created == 2, "successful resize releases only the superseded resource");
        cache.Clear(); cache.Clear();
        Check(second.Disposals == 1, "failure invalidation clears a resource exactly once");
        var third = cache.GetOrCreate("new-language-or-format", Create);
        cache.Dispose(); cache.Dispose();
        Check(third.Disposals == 1, "shutdown releases the retained resource once");
        bool rejected = false;
        try { cache.GetOrCreate("late", Create); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected && created == 3, "a late frame cannot create a resource after owner disposal");

        OverlayBox[] boxes = [new(20, 10, 60, 20), new(150, 10, 40, 20), new(20, 80, 120, 30)];
        bool[] ltr = [false, false, false];
        double width = OverlayPlacementPolicy.AvailableWidth(0, boxes, 300, false, ltr);
        Check(width == 128, "a translation can widen only up to the next source box, including untranslated neighbors");
        var anchor = OverlayPlacementPolicy.Anchor(boxes[0], width, 20, false);
        Check(anchor.X == boxes[0].X && anchor.Y == boxes[0].Y &&
            OverlayPlacementPolicy.CanPresent(0, boxes, anchor, 300, 200), "LTR widening retains its source anchor without moving a row");
        Check(!OverlayPlacementPolicy.CanPresent(0, boxes, new(20, 10, 160, 20), 300, 200),
            "a translated border cannot cover the next source text");
        Check(!OverlayPlacementPolicy.CanPresent(0, boxes, new(20, 81, 60, 20), 300, 200),
            "an overflowing box cannot be pushed into another source row");
        Check(!OverlayPlacementPolicy.CanPresent(0, boxes, new(30, 10, 60, 20), 300, 200),
            "placement cannot silently change both source anchors");
        var rtl = OverlayPlacementPolicy.Anchor(boxes[1], 68, 20, true);
        Check(rtl.Right == boxes[1].Right && rtl.Y == boxes[1].Y, "RTL widening preserves the source right edge and baseline");
        var leftSpace = OverlayPlacementPolicy.AvailableWidth(0, boxes, 300, true, [true, true, true]);
        Check(leftSpace == 80, "RTL expansion remains inside the viewport");
        Check(!OverlayPlacementPolicy.CanPresent(0, boxes, new(-1, 10, 60, 20), 300, 200) &&
            !OverlayPlacementPolicy.CanPresent(0, boxes, anchor, double.NaN, 200), "offscreen and unknown viewport geometry fail closed");
        OverlayBox[] mixed = [new(0, 0, 20, 20), new(200, 0, 20, 20)];
        bool[] directions = [false, true];
        var left = OverlayPlacementPolicy.Anchor(mixed[0], OverlayPlacementPolicy.AvailableWidth(0, mixed, 240, false, directions), 20, false);
        var right = OverlayPlacementPolicy.Anchor(mixed[1], OverlayPlacementPolicy.AvailableWidth(1, mixed, 240, true, directions), 20, true);
        Check(left.Right < right.X, "opposite reading directions cannot both claim the same free gap");
        Check(OverlayPlacementPolicy.SameSource(boxes[0], new(21, 11, 61, 21)) &&
            !OverlayPlacementPolicy.SameSource(boxes[0], new(23, 10, 60, 20)),
            "visual reuse tolerates bounded OCR jitter but not accumulated source movement");

        var mask = Enumerable.Repeat(true, 100).ToArray();
        TextMaskProtection.Apply(mask, 10, 10, 100, 100, [new(105, 102, 3, 5)]);
        Check(!mask[2 * 10 + 5] && !mask[6 * 10 + 7] && mask[0] && mask[9 * 10],
            "eraser ink growth cannot erase a neighboring source region");
        var unchanged = (bool[])mask.Clone();
        TextMaskProtection.Apply(mask, 10, 10, 100, 100, [new(-10, -10, 2, 2), new(double.NaN, 0, 1, 1)]);
        Check(mask.SequenceEqual(unchanged), "offscreen or invalid neighbor geometry cannot corrupt mask indexing");
    }

    private sealed class Resource : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }
}
