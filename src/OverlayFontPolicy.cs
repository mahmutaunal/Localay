namespace AlpWareStudio.Localay;

internal static class OverlayFontPolicy
{
    internal const double LineHeightRatio = 1.15;
    internal static double Median(IEnumerable<double> values)
    {
        var ordered = values.Where(v => double.IsFinite(v) && v > 0).Order().ToArray();
        if (ordered.Length == 0) return 0;
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? ordered[middle - 1] / 2 + ordered[middle] / 2 : ordered[middle];
    }

    internal static double SourceLineHeight(double width, double height, string text, int lineCount = 0,
        bool vertical = false, double measured = 0)
    {
        if (double.IsFinite(measured) && measured > 0) return measured;
        int lines = Math.Max(1, Math.Max(lineCount, text.Count(c => c == '\n') + 1));
        double extent = vertical ? width : height;
        return double.IsFinite(extent) && extent > 0 ? extent / lines : 0;
    }

    internal static (double Min, double Max) Bounds(double min, double max)
    {
        min = double.IsFinite(min) && min > 0 ? Math.Clamp(min, 1, 512) : 10;
        max = double.IsFinite(max) && max > 0 ? Math.Clamp(max, 1, 512) : 68;
        return min <= max ? (min, max) : (max, min);
    }

    internal static bool CanMergeSourceSizes(double first, double second) =>
        !double.IsFinite(first) || !double.IsFinite(second) || first <= 0 || second <= 0 ||
        Math.Max(first, second) / Math.Min(first, second) <= 1.35;

    internal static double PreferredSize(double lineHeight, double min, double max)
    {
        (min, max) = Bounds(min, max);
        double size = double.IsFinite(lineHeight) && lineHeight > 0 ? lineHeight / LineHeightRatio : 24;
        // Half-DIP steps avoid tiny changes from OCR bounding-box jitter.
        return Math.Clamp(Math.Round(size * 2, MidpointRounding.AwayFromZero) / 2, min, max);
    }

    internal static double ChooseSize(double preferred, double minimum, bool allowShrink, Func<double, bool> fits)
    {
        if (!allowShrink || fits(preferred)) return preferred;
        if (!fits(minimum)) return minimum;
        double low = minimum, high = preferred;
        while (high - low > 0.25)
        {
            double mid = (low + high) / 2;
            if (fits(mid)) low = mid; else high = mid;
        }
        return low;
    }
}

// Original capture pixels are retained; DPI changes always recompute from these values.
internal readonly record struct OverlayPixelGeometry(double X, double Y, double Width, double Height, double LineHeight)
{
    internal OverlayPixelGeometry ToDips(double scaleX, double scaleY) =>
        new(X / ValidScale(scaleX), Y / ValidScale(scaleY), Width / ValidScale(scaleX),
            Height / ValidScale(scaleY), LineHeight / ValidScale(scaleY));
    internal static double ValidScale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}

internal sealed record OverlayTextOptions(bool AutoSize, bool AutoBackground, string FontFamily, double Minimum, double Maximum);
