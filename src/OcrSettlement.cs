using System;
using System.Text.Json;

namespace AlpWareStudio.Localay;

// UI-owner serialized. A captured image, not a timer callback, confirms a result.
// The lease and accumulated damage survive the end of the native OCR call.
internal sealed class OcrSettlement<T>(TimeProvider? timeProvider = null) : IDisposable where T : class
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private T? _value;
    private System.Collections.Generic.IReadOnlyList<OcrSceneBlock>? _blocks;
    private OcrFrameObservation? _observation;
    private PipelineLifetime.Lease? _owner;
    private long _started;
    private TimeSpan _delay;
    private bool _disposed;
    internal bool HasPending => _value != null;
    internal bool NeedsConfirmation => HasPending && _time.GetElapsedTime(_started) >= _delay;

    internal void Begin(T value, JsonElement results, OcrFrameObservation observation,
        PipelineLifetime.Lease owner, TimeSpan delay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Cancel();
        try
        {
            var scene = OcrSceneSnapshot.FromResults(results, 0);
            if (scene.IsEmpty || scene.BlockCount != results.GetArrayLength())
                throw new ArgumentException("Invalid pending OCR geometry.", nameof(results));
            _blocks = scene.Blocks;
        }
        catch { owner.Dispose(); throw; }
        _value = value;
        _observation = observation;
        _owner = owner;
        _started = _time.GetTimestamp();
        _delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    // Call on every captured image, including unchanged images. Damage is sticky:
    // changed-then-restored pixels do not authorize the older OCR result.
    internal T? Observe(FrameChangeMap changes)
    {
        if (!HasPending) return null;
        if (_owner?.IsCurrent != true) { Cancel(); return null; }
        _observation!.Include(changes);
        for (int i = 0; i < _blocks!.Count; i++)
            if (!_observation.IsCurrent(_blocks[i])) { Cancel(); return null; }
        if (!NeedsConfirmation) return null;
        var value = _value;
        Cancel(); // Release ownership before publishing; callers can safely begin a successor.
        return value;
    }

    internal void Cancel()
    {
        _value = null;
        _blocks = null;
        _observation = null;
        _owner?.Dispose();
        _owner = null;
    }
    public void Dispose() { Cancel(); _disposed = true; }
}

// Preserve the user's quiet-time setting. Slow typewriter text may need one
// observed growth interval, bounded by twice that setting, instead of fixed +600ms.
internal sealed class OcrSettleDelay(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long? _lastChange;
    private string? _lastText;
    private TimeSpan _growthInterval;
    private bool _growing;

    internal TimeSpan Observe(string text, bool growing, bool endsSentence, double seconds)
    {
        var configured = TimeSpan.FromSeconds(double.IsFinite(seconds)
            ? Math.Clamp(seconds, 0, TimeSpan.MaxValue.TotalSeconds / 4) : 0);
        if (text != _lastText)
        {
            _growthInterval = growing && _lastChange is long previous ? _time.GetElapsedTime(previous) : TimeSpan.Zero;
            _lastChange = _time.GetTimestamp();
            _lastText = text;
            _growing = growing;
        }
        if (!_growing || endsSentence || configured == TimeSpan.Zero) return configured;
        return TimeSpan.FromMilliseconds(Math.Max(configured.TotalMilliseconds,
            Math.Min(configured.TotalMilliseconds * 2, _growthInterval.TotalMilliseconds * 1.5)));
    }

    internal void Reset() { _lastChange = null; _lastText = null; _growthInterval = TimeSpan.Zero; _growing = false; }
}
