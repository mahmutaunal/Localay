using AlpWareStudio.Localay;

internal static class GamePresentationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string first = "We need to reach the gate before dawn.";
        const string second = "The bridge is gone. Find another way.";
        OverlayBox[] boxes = [new(250, 680, 500, 40)];
        var menuTexts = Enumerable.Range(0, 20).Select(i => "Menu label " + i).Append(first).ToArray();
        var menuBoxes = Enumerable.Range(0, 20).Select(i => new OverlayBox(20, 20 + i * 20, 150, 18)).Append(boxes[0]).ToArray();
        var order = SubtitlePanelPolicy.TranslationOrder(menuBoxes, menuTexts, 1000, 800);
        check(order[0] == 20 && order.Skip(1).SequenceEqual(Enumerable.Range(0, 20)) && order.Count == menuTexts.Length,
            "a caption after twenty menu labels enters the first batch without losing or reordering ordinary UI");
        check(order.Select(i => menuTexts[i]).Distinct().Count() == menuTexts.Distinct().Count(),
            "caption priority changes neither exact sources nor the number of deduplicated translation units");
        var scaled = menuBoxes.Select(b => new OverlayBox(b.X * 1.5, b.Y * 1.5, b.Width * 1.5, b.Height * 1.5)).ToArray();
        check(SubtitlePanelPolicy.TranslationOrder(scaled, menuTexts, 1500, 1200).SequenceEqual(order),
            "physical capture geometry and a matching scaled viewport keep the same caption priority");
        check(SubtitlePanelPolicy.TranslationOrder(menuBoxes[..20], menuTexts[..20], 1000, 800).SequenceEqual(Enumerable.Range(0, 20)) &&
            SubtitlePanelPolicy.TranslationOrder(menuBoxes, menuTexts, double.NaN, 800).SequenceEqual(Enumerable.Range(0, 21)) &&
            SubtitlePanelPolicy.TranslationOrder([], menuTexts, 1000, 800).SequenceEqual(Enumerable.Range(0, 21)),
            "menus without caption candidates and unavailable geometry keep the original translation order");
        OverlayBox[] repeatedBoxes = [new(20, 20, 150, 18), new(20, 60, 150, 18), boxes[0], new(250, 725, 500, 20)];
        string[] repeatedTexts = [first, "Settings", first, second];
        check(SubtitlePanelPolicy.TranslationOrder(repeatedBoxes, repeatedTexts, 1000, 800).SequenceEqual(new[] { 0, 2, 3, 1 }),
            "repeated caption sources retain priority through exact-text deduplication and adjacent lines keep reading order");
        var policy = new SubtitlePanelPolicy();
        check(policy.Observe(boxes, [first], 1000, 800) == null,
            "one lower-center paragraph is not automatically a subtitle");
        bool remainsUncalibrated = true;
        for (int i = 0; i < 20; i++)
            remainsUncalibrated &= policy.Observe(boxes, [first], 1000, 800) == null;
        check(remainsUncalibrated, "rechecking the same static text cannot fabricate subtitle evidence");
        check(policy.Observe(boxes, [first + " Please hurry."], 1000, 800) == null,
            "typewriter growth does not count as a new subtitle");
        var plan = policy.Observe(boxes, [second], 1000, 800);
        check(plan != null && plan.Members.SequenceEqual(new[] { 0 }),
            "different dialogue at the same lower-center anchor enables the caption panel");
        check(plan != null && SubtitlePanelPolicy.Contains(plan.Bounds, boxes[0]) &&
            plan.Bounds.X >= 0 && plan.Bounds.Bottom <= 800,
            "the panel covers its original text inside the viewport");
        var jittered = policy.Observe([new(240, 678, 520, 42)], ["Captain, the western road is clear now."], 1000, 800);
        check(jittered?.Bounds == plan?.Bounds && jittered?.FontSize == plan?.FontSize,
            "caption length and minor OCR jitter do not move or resize a calibrated panel");
        check(policy.Observe([], [], 1000, 800) == null, "between dialogue lines no caption mask is presented");
        check(policy.Observe(boxes, [second], 1000, 800)?.Bounds == plan?.Bounds,
            "a gap between subtitles retains the learned location without showing stale text");

        OverlayBox[] withHud = [boxes[0], new(100, 690, 100, 20)];
        check(policy.Observe(withHud, [second, "Health: 100"], 1000, 800) == null,
            "a caption panel cannot hide an untranslated HUD label");
        check(policy.Observe([new(30, 680, 700, 40)], [second], 1000, 800) == null,
            "off-center lower-screen UI stays on the ordinary box path");
        check(policy.Observe([new(250, 300, 500, 40)], [second], 1000, 800) == null,
            "menu paragraphs outside the subtitle band stay ordinary UI");
        check(policy.Observe([new(450, 700, 100, 24)], ["Continue playing"], 1000, 800) == null,
            "short narrow menu controls do not become a caption panel");
        check(policy.Observe(boxes, [second], 1200, 900) == null,
            "viewport or DPI-layout changes invalidate subtitle calibration");
        policy.Reset();
        check(policy.Observe(boxes, [second], 1000, 800) == null,
            "choosing a new game does not inherit subtitle calibration");

        policy.Reset();
        OverlayBox[] lines = [new(250, 680, 500, 20), new(300, 702, 400, 20)];
        policy.Observe(lines, [first, "There is no time to waste."], 1000, 800);
        var multiline = policy.Observe(lines, [second, "We must get out of here."], 1000, 800);
        check(multiline?.Members.SequenceEqual(new[] { 0, 1 }) == true && lines.All(b => SubtitlePanelPolicy.Contains(multiline.Bounds, b)),
            "adjacent subtitle lines share one panel while preserving reading order");
        policy.Reset();
        OverlayBox[] separated = [new(250, 630, 500, 20), new(300, 750, 400, 20)];
        policy.Observe(separated, [first, second], 1000, 800);
        check(policy.Observe(separated, [second, first], 1000, 800) == null,
            "separate lower-screen regions are not merged into one caption");

        check(policy.Observe(boxes, [], 1000, 800) == null &&
            policy.Observe(boxes, [first], double.NaN, 800) == null &&
            policy.Observe([new(double.NaN, 680, 500, 40)], [first], 1000, 800) == null,
            "invalid inputs never produce a masking rectangle");
        check(double.IsFinite(SubtitlePanelPolicy.UiFontSize(double.NaN)) &&
            double.IsFinite(SubtitlePanelPolicy.CaptionFontSize(double.PositiveInfinity)),
            "unavailable viewport metrics still produce finite text sizes");
        check(SubtitlePanelPolicy.CaptionFontSize(800) > SubtitlePanelPolicy.UiFontSize(800) &&
            SubtitlePanelPolicy.UiFontSize(240) >= 16 && SubtitlePanelPolicy.CaptionFontSize(8000) <= 34,
            "caption and UI use distinct viewport-based sizes without source-font matching");
        check(!SubtitlePanelPolicy.Overlaps(new(0, 0, 100, 20), new(102, 0, 100, 20)) &&
            SubtitlePanelPolicy.Overlaps(new(0, 0, 100, 20), new(90, 0, 100, 20)),
            "admitted UI translations cannot claim overlapping free space");

        // A deterministic geometry sweep includes malformed, offscreen and differently sized sources.
        var random = new Random(42);
        bool allMasksSafe = true;
        for (int i = 0; i < 200; i++)
        {
            policy.Reset();
            var box = new OverlayBox(random.Next(-100, 1000), random.Next(-50, 800), random.Next(1, 800), random.Next(1, 160));
            policy.Observe([box], [first], 1000, 800);
            var candidate = policy.Observe([box], [second], 1000, 800);
            allMasksSafe &= candidate == null || (SubtitlePanelPolicy.Contains(candidate.Bounds, box) && candidate.Bounds.X >= 0 &&
                candidate.Bounds.Y >= 0 && candidate.Bounds.Right <= 1000 && candidate.Bounds.Bottom <= 800);
        }
        check(allMasksSafe, "200-case geometry sweep: every admitted mask covers its source and remains on screen");
    }
}
