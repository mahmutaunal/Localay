using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
bool Near(double first, double second) => Math.Abs(first - second) < 0.000001;
Check(OverlayFontPolicy.Median([20, 21, 200, 19]) == 20.5, "line metric resists one oversized OCR box");
Check(OverlayFontPolicy.Median([double.NaN, double.PositiveInfinity, -5, 0, 20]) == 20, "invalid OCR metrics are excluded");
Check(OverlayFontPolicy.Median([]) == 0, "absent line metrics allow the documented fallback");
Check(OverlayFontPolicy.SourceLineHeight(400, 72, "one\ntwo\nthree") == 24, "fallback divides paragraph height by source lines");
Check(OverlayFontPolicy.SourceLineHeight(400, 80, "flattened", lineCount: 4) == 20, "OCR line count survives flattened text");
Check(OverlayFontPolicy.SourceLineHeight(60, 400, "vertical", lineCount: 3, vertical: true) == 20, "vertical columns use column width instead of total height");
Check(OverlayFontPolicy.SourceLineHeight(400, 200, "many lines", measured: 23) == 23, "measured line height takes precedence over paragraph bounds");
Check(OverlayFontPolicy.SourceLineHeight(400, double.NaN, "invalid") == 0, "invalid dimensions produce no fabricated source size");
Check(OverlayFontPolicy.CanMergeSourceSizes(20, 22), "nearby lines tolerate minor OCR size differences");
Check(!OverlayFontPolicy.CanMergeSourceSizes(20, 40), "body text and a heading remain separate font-size groups");
Check(OverlayFontPolicy.CanMergeSourceSizes(0, 20), "missing metadata does not forbid legacy OCR merging");
Check(OverlayFontPolicy.Bounds(68, 10) == (10, 68), "reversed user limits are normalized");
Check(OverlayFontPolicy.Bounds(double.NaN, double.PositiveInfinity) == (10, 68), "non-finite settings use safe defaults");
Check(OverlayFontPolicy.Bounds(0, 100000) == (10, 512), "invalid minimum and extreme maximum cannot produce invalid WPF sizes");
Check(OverlayFontPolicy.PreferredSize(23, 10, 68) == 20, "source line height determines the preferred font size");
Check(OverlayFontPolicy.PreferredSize(46, 10, 68) == 40, "a heading preserves its larger source size");
Check(OverlayFontPolicy.PreferredSize(23.05, 10, 68) == OverlayFontPolicy.PreferredSize(23, 10, 68), "small OCR jitter does not resize text");
Check(OverlayFontPolicy.PreferredSize(1000, 10, 68) == 68 && OverlayFontPolicy.PreferredSize(2, 10, 68) == 10, "source-based sizing respects configured limits");
Check(OverlayFontPolicy.PreferredSize(double.NaN, 10, 68) == 24, "unknown source size uses the established fixed fallback");
Check(OverlayFontPolicy.ChooseSize(20, 10, false, _ => false) == 20, "an overflowing translation is not shrunk before space is considered");
Check(OverlayFontPolicy.ChooseSize(20, 10, true, _ => true) == 20, "last-resort fitting leaves already fitting text unchanged");
double fitted = OverlayFontPolicy.ChooseSize(30, 10, true, size => size <= 22);
Check(fitted <= 22 && fitted > 21.75, "last-resort fitting finds the largest readable fitting size");
Check(OverlayFontPolicy.ChooseSize(30, 12, true, _ => false) == 12, "impossible fits honor the minimum instead of shrinking indefinitely");

var pixels = new OverlayPixelGeometry(300, 150, 600, 90, 30);
foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
{
    var dips = pixels.ToDips(scale, scale);
    Check(Near(dips.X * scale, pixels.X) && Near(dips.Width * scale, pixels.Width) && Near(dips.LineHeight * scale, 30), "capture pixels round-trip at scaling " + scale);
}
var asymmetric = pixels.ToDips(1.25, 1.5);
Check(asymmetric.X == 240 && asymmetric.Y == 100 && asymmetric.Width == 480 && asymmetric.Height == 60 && asymmetric.LineHeight == 20, "X and Y scales are applied to the correct dimensions");
Check(pixels.ToDips(0, double.NaN) == pixels, "invalid DPI falls back to one without corrupting geometry");
var original = pixels.ToDips(1, 1);
for (int i = 0; i < 100; i++) { _ = pixels.ToDips(1.5, 1.5); _ = pixels.ToDips(2, 2); }
Check(pixels.ToDips(1, 1) == original, "repeated monitor changes do not compound DPI scaling");
RenderingResourceChecks.Run(Check);
Console.WriteLine($"{passed} portable overlay layout checks passed. WPF shaping and physical monitors require Windows.");
