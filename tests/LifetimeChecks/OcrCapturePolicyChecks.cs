namespace AlpWareStudio.Localay;

internal static class OcrCapturePolicyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var periodic = TimeSpan.FromSeconds(2);
        bool Recognize(bool same, bool pending, bool translating, bool settled, int ms) =>
            OcrCapturePolicy.ShouldRecognize(same, pending, translating, settled, TimeSpan.FromMilliseconds(ms), periodic);
        bool Cached(bool busy, bool pending, bool due, bool translating, bool settled, int ms) =>
            OcrCapturePolicy.NeedsCachedFrame(busy, pending, due, translating, settled, TimeSpan.FromMilliseconds(ms), periodic);

        check(!Recognize(true, false, true, true, 5000),
            "unchanged text never repeats OCR while a provider is still translating, including single-shot mode");
        check(Recognize(false, false, true, false, 10),
            "a provider in flight does not prevent recognizing changed source pixels");
        check(!Recognize(true, true, false, false, 5000),
            "pending settlement waits for pixel confirmation instead of repeating recognition");
        check(!Recognize(false, true, false, false, 5000),
            "a valid pending block survives unrelated movement without another OCR pass");
        check(Recognize(false, false, false, false, 10),
            "after intersecting damage cancels settlement, a changed source can be recognized immediately");
        check(!Recognize(true, false, false, true, 1999) && Recognize(true, false, false, true, 2000),
            "settled identical pixels retain the periodic recheck boundary in both capture modes");
        check(!Recognize(true, false, false, false, 249) && Recognize(true, false, false, false, 250),
            "an unresolved retry retains the existing same-image OCR rate limit");
        check(!Cached(false, false, false, true, true, 5000),
            "a static source is not copied from cache just to wait for translation");
        check(!Cached(false, true, false, false, false, 1000),
            "a long configured quiet time does not cause repeated cached bitmap copies before confirmation is due");
        check(Cached(false, true, true, false, false, 40),
            "settlement confirmation can obtain cached pixels before the 250ms OCR recheck gate");
        check(!Cached(true, true, true, false, false, 5000),
            "the active native input keeps ownership before cached confirmation");
        check(!Cached(false, false, false, false, true, 1999) && Cached(false, false, false, false, true, 2000),
            "cached recheck follows the settled-result boundary rather than the single-shot setting");
        check(!Cached(false, false, false, false, false, 249) && Cached(false, false, false, false, false, 250),
            "cached retry does not bypass the existing source recheck cadence");
    }
}
