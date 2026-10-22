using System;
using System.Collections.Generic;

namespace AlpWareStudio.Localay;

internal static class TextMaskProtection
{
    // Masks use patch-local pixels; protected source boxes use capture pixels.
    // Neighboring glyphs must remain unchanged even when ink growth reaches them.
    internal static void Apply(bool[] mask, int width, int height, int originX, int originY, IReadOnlyList<OverlayBox> sources)
    {
        if (mask.Length != checked(width * height)) throw new ArgumentException("Mask dimensions do not match.");
        foreach (var source in sources)
        {
            if (!source.IsValid) continue;
            int left = (int)Math.Clamp(Math.Floor(source.X - originX - 1), 0, width);
            int right = (int)Math.Clamp(Math.Ceiling(source.Right - originX + 1), 0, width);
            int top = (int)Math.Clamp(Math.Floor(source.Y - originY - 1), 0, height);
            int bottom = (int)Math.Clamp(Math.Ceiling(source.Bottom - originY + 1), 0, height);
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++) mask[y * width + x] = false;
        }
    }
}
