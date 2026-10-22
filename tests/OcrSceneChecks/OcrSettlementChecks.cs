using System.Text.Json;

namespace AlpWareStudio.Localay;

// Production state/observation/ownership classes with a monotonic fake clock.
// No OCR implementation, timing sleeps, WPF, network or user configuration.
internal static class OcrSettlementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const int width = 256, height = 128;
        var clock = new SettlementClock();
        var lifetime = new PipelineLifetime();
        using var pending = new OcrSettlement<string>(clock);
        var results = JsonSerializer.SerializeToElement(new[] { new
        {
            text = "Save", confidence = 0.9,
            rect = new[] { new[] { 32, 32 }, new[] { 64, 32 }, new[] { 64, 48 }, new[] { 32, 48 } }
        }});
        var empty = FrameChangeMap.Empty(width, height);
        void Begin(string text, double ms, OcrFrameObservation? observation = null) => pending.Begin(text, results,
            observation ?? new(width, height), lifetime.Capture(), TimeSpan.FromMilliseconds(ms));
        FrameChangeMap Damage(int x, int y)
        {
            var before = new byte[width * height * 4];
            var after = new byte[before.Length];
            after[(y * width + x) * 4] = 1;
            return FrameChangeMap.Compare(after, before, width, height);
        }

        Begin("first", 100);
        clock.Advance(99);
        check(!pending.NeedsConfirmation && pending.Observe(empty) == null && pending.HasPending,
            "a fresh unchanged capture cannot bypass the configured quiet time");
        clock.Advance(1);
        check(pending.NeedsConfirmation && pending.HasPending, "elapsed time requests a capture; it does not publish on its own");
        check(pending.Observe(empty) == "first" && !pending.HasPending && pending.Observe(empty) == null,
            "an unchanged image publishes the stored OCR result exactly once without another OCR call");

        Begin("scroll", 50);
        check(pending.Observe(Damage(40, 40)) == null && !pending.HasPending,
            "text movement cancels pending geometry before the deadline");
        clock.Advance(1000);
        check(pending.Observe(empty) == null, "returning pixels cannot revive a cancelled OCR result");
        Begin("patch margin", 0);
        check(pending.Observe(Damage(16, 16)) == null && !pending.HasPending,
            "eraser-margin damage invalidates a result even when glyph pixels are unchanged");

        Begin("stationary", 30);
        clock.Advance(30);
        check(pending.Observe(Damage(224, 100)) == "stationary", "unrelated animation does not starve a stationary pending block");
        Begin("resize", 0);
        check(pending.Observe(FrameChangeMap.All(320, 128)) == null && !pending.HasPending,
            "capture resize or origin replacement invalidates the complete pending result");

        var damagedDuringRecognition = new OcrFrameObservation(width, height);
        damagedDuringRecognition.Include(Damage(40, 40));
        Begin("already stale", 0, damagedDuringRecognition);
        check(pending.Observe(empty) == null, "damage accumulated during recognition survives native OCR completion");
        var owner = lifetime.Capture();
        pending.Begin("superseded", results, new(width, height), owner, TimeSpan.FromMilliseconds(20));
        clock.Advance(10);
        Begin("latest", 20);
        check(!owner.IsCurrent, "replacing a candidate releases its earlier ownership lease");
        clock.Advance(10);
        check(pending.Observe(empty) == null, "the superseded candidate deadline cannot publish its successor early");
        clock.Advance(10);
        check(pending.Observe(empty) == "latest", "only the latest OCR candidate survives replacement");

        Begin("old session", 0);
        lifetime.Invalidate();
        check(pending.Observe(empty) == null && !pending.HasPending,
            "stop, settings invalidation or restart prevents publishing an earlier session");
        Begin("cancelled", 0); pending.Cancel(); pending.Cancel();
        check(pending.Observe(empty) == null, "cancellation is idempotent and cannot display old text");
        var invalidOwner = lifetime.Capture();
        bool invalidRejected = false;
        try { pending.Begin("bad geometry", JsonSerializer.SerializeToElement(new[] { new { text = "Save" } }),
            new(width, height), invalidOwner, TimeSpan.Zero); }
        catch (ArgumentException) { invalidRejected = true; }
        check(invalidRejected && !invalidOwner.IsCurrent && !pending.HasPending,
            "invalid pending geometry is rejected and releases its lease instead of skipping validation");

        var policy = new OcrSettleDelay(clock);
        check(policy.Observe("H", false, false, 0.1).TotalMilliseconds == 100, "normal text keeps the configured quiet time");
        clock.Advance(80);
        check(policy.Observe("Hello", true, false, 0.1).TotalMilliseconds == 120, "typewriter delay follows measured growth cadence instead of a fixed 600ms");
        clock.Advance(5);
        check(policy.Observe("Hello", false, false, 0.1).TotalMilliseconds == 120,
            "an identical OCR observation does not erase the measured growth interval");
        clock.Advance(500);
        check(policy.Observe("Hello there", true, false, 0.1).TotalMilliseconds == 200,
            "a long typing gap cannot create an unbounded extra delay");
        check(policy.Observe("Hello there", true, true, 0.1).TotalMilliseconds == 100,
            "sentence punctuation removes only the growth extension, never the configured quiet time");
        check(policy.Observe("Hello there", true, false, 0).TotalMilliseconds == 0, "explicit zero disables settling as before");
        policy.Reset();
        check(policy.Observe("New", true, false, 0.1).TotalMilliseconds == 100, "a resumed session does not inherit an old typing cadence");
    }

    private sealed class SettlementClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(long milliseconds) => _timestamp += milliseconds;
    }
}
