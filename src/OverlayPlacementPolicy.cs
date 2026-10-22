using System;
using System.Collections.Generic;

namespace AlpWareStudio.Localay;

internal readonly record struct OverlayBox(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

// Geometry only. Text shaping stays in WPF. Original source boxes, including those
// without translations, are obstacles; fitting never pushes a neighbor downward.
internal static class OverlayPlacementPolicy
{
    internal static double AvailableWidth(int index, IReadOnlyList<OverlayBox> sources, double viewportWidth, bool rtl,
        IReadOnlyList<bool>? rightToLeft = null)
    {
        var source = sources[index];
        if (!source.IsValid || !double.IsFinite(viewportWidth)) return source.Width;
        double left = rtl ? 0 : source.X, right = rtl ? source.Right : viewportWidth;
        for (int i = 0; i < sources.Count; i++)
        {
            var other = sources[i];
            if (i == index || !other.IsValid || other.Y >= source.Bottom || other.Bottom <= source.Y) continue;
            if (!rtl && other.X >= source.Right)
                right = Math.Min(right, rightToLeft == null || rightToLeft[i] ? (source.Right + other.X) / 2 - 1 : other.X - 2);
            if (rtl && other.Right <= source.X)
                left = Math.Max(left, rightToLeft == null || !rightToLeft[i] ? (other.Right + source.X) / 2 + 1 : other.Right + 2);
        }
        return Math.Max(source.Width, right - left);
    }

    internal static OverlayBox Anchor(OverlayBox source, double measuredWidth, double measuredHeight, bool rtl) =>
        new(rtl ? source.Right - measuredWidth : source.X, source.Y, measuredWidth, measuredHeight);

    internal static bool CanPresent(int index, IReadOnlyList<OverlayBox> sources, OverlayBox proposed,
        double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0 ||
            !proposed.IsValid || proposed.X < 0 || proposed.Y < 0 || proposed.Right > viewportWidth + 0.5 ||
            proposed.Bottom > viewportHeight + 0.5 || Math.Abs(proposed.Y - sources[index].Y) > 0.01) return false;
        if (Math.Abs(proposed.X - sources[index].X) > 0.01 && Math.Abs(proposed.Right - sources[index].Right) > 0.01) return false;
        for (int i = 0; i < sources.Count; i++)
        {
            var other = sources[i];
            if (i == index || !other.IsValid) continue;
            double overlapX = Math.Min(proposed.Right, other.Right) - Math.Max(proposed.X, other.X);
            double overlapY = Math.Min(proposed.Bottom, other.Bottom) - Math.Max(proposed.Y, other.Y);
            // Adjacent OCR lines can overlap by one or two pixels of rounding.
            if (overlapX > 2 && overlapY > 2) return false;
        }
        return true;
    }

    internal static bool SameSource(OverlayBox a, OverlayBox b) => a.IsValid && b.IsValid &&
        Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2;
}
