namespace AlpWareStudio.Localay;

// One initial attempt plus at most two automatic retries per unchanged scene.
// OCR ticks drive retries; no delayed task can resurrect a stopped/replaced session.
internal sealed class TranslationRetryState
{
    private int _failures;
    private DateTimeOffset _next = DateTimeOffset.MaxValue;
    internal bool IsDue(DateTimeOffset now) => _failures is > 0 and < 3 && now >= _next;
    internal void Reset() { _failures = 0; _next = DateTimeOffset.MaxValue; }
    internal void Failed(TranslationFailure failure, DateTimeOffset now)
    {
        _failures++;
        if (!failure.CanRetry || _failures >= 3) { _next = DateTimeOffset.MaxValue; return; }
        var delay = TimeSpan.FromSeconds(_failures == 1 ? 2 : 5);
        if (failure.Kind == TranslationFailureKind.RateLimited) delay = TimeSpan.FromSeconds(30);
        if (failure.RetryAfter is TimeSpan serverDelay && serverDelay > delay) delay = serverDelay;
        _next = delay > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;
    }
}
