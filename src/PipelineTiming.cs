using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AlpWareStudio.Localay;

internal enum PipelineStage { CaptureReady, OcrStarted, OcrCompleted, SceneObserved, Settled, TranslationStarted, TranslationReady, UiApplied }
internal enum PipelineOutcome { Pending, ObservationOnly, Unchanged, Empty, RecognitionOnly, UiApplied, Partial, NotPresented, Failed, Canceled, Superseded }
internal enum PipelineWork { Ocr, DispatcherQueue, SceneProcessing, Provider, PrefetchWait, Background, UiApply }
internal sealed record PipelineCapture(int Width, int Height, int OriginX, int OriginY);

internal interface IPipelineClock
{
    long Timestamp { get; }
    double Milliseconds(long start, long end);
}

internal sealed class MonotonicPipelineClock : IPipelineClock
{
    internal static readonly MonotonicPipelineClock Instance = new();
    public long Timestamp => Stopwatch.GetTimestamp();
    public double Milliseconds(long start, long end) => (end - start) * 1000d / Stopwatch.Frequency;
}

internal sealed record PipelineEvent(string Stage, double AtMs);
internal sealed record PipelineWorkSample(string Work, double StartMs, double DurationMs);
internal sealed record PipelineSample(long Id, long? ParentId, bool IsScene, int ExpectedBlocks,
    int TranslatedBlocks, int VisibleBlocks, string Outcome, double DurationMs,
    PipelineEvent[] Events, PipelineWorkSample[] Work, int DroppedEvents, PipelineCapture? Capture, int InFlightWork);
internal sealed record PipelineDistribution(int Count, double? P50Ms, double? P95Ms,
    double? P99Ms, double? MaxMs, int AtOrAbove100Ms, int Above500Ms);
internal sealed record PipelineResources(double ElapsedMs, double ProcessCpuMs,
    long ProcessAllocatedBytes, long WorkingSetBytes);

internal sealed class PipelineReport
{
    public int SchemaVersion { get; init; } = 1;
    public string Measurement { get; init; } = "capture-start-to-logical-ui-apply";
    public string SourceOnset { get; init; } = "not_measured";
    public string PhysicalPresentation { get; init; } = "not_measured";
    public string SemanticCorrectness { get; init; } = "not_measured";
    public string CoverageMeasurement { get; init; } = "logical-ui-elements-not-screen-pixels";
    public string Origin { get; init; } = "live";
    public int TargetMs { get; init; } = 100;
    public long TotalStarted { get; init; }
    public int RetainedSamples { get; init; }
    public long EvictedSamples { get; init; }
    public int PendingScenes { get; init; }
    public int SuccessfulScenes { get; init; }
    public int UnsuccessfulScenes { get; init; }
    public long ExpectedBlocks { get; init; }
    public long TranslatedBlocks { get; init; }
    public long VisibleBlocks { get; init; }
    public double? VisibleBlockCoverage { get; init; }
    public PipelineDistribution CaptureToUi { get; init; } = Distribution([]);
    public Dictionary<string, PipelineDistribution> Work { get; init; } = new();
    public Dictionary<string, int> Outcomes { get; init; } = new();
    public PipelineSample[] Samples { get; init; } = [];
    public PipelineResources? ProcessResources { get; set; }

    // The retained window includes failures and pending scenes. Logical UI application is
    // never evidence of semantic correctness or physical presentation on the display.
    internal static PipelineReport Create(PipelineSample[] samples, long totalStarted, long evicted, string origin = "live")
    {
        var scenes = samples.Where(s => s.IsScene).ToArray();
        long expected = scenes.Sum(s => (long)s.ExpectedBlocks);
        long visible = scenes.Sum(s => (long)s.VisibleBlocks);
        var successes = scenes.Where(s => s.Outcome == nameof(PipelineOutcome.UiApplied) &&
            s.ExpectedBlocks > 0 && s.VisibleBlocks == s.ExpectedBlocks &&
            s.TranslatedBlocks == s.ExpectedBlocks && s.Events.Any(e => e.Stage == nameof(PipelineStage.UiApplied))).ToArray();
        return new()
        {
            Origin = origin, TotalStarted = totalStarted, RetainedSamples = samples.Length, EvictedSamples = evicted,
            PendingScenes = scenes.Count(s => s.Outcome == nameof(PipelineOutcome.Pending)), SuccessfulScenes = successes.Length,
            UnsuccessfulScenes = scenes.Count(s => s.Outcome != nameof(PipelineOutcome.Pending)) - successes.Length,
            ExpectedBlocks = expected, VisibleBlocks = visible, TranslatedBlocks = scenes.Sum(s => (long)s.TranslatedBlocks),
            VisibleBlockCoverage = expected == 0 ? null : (double)visible / expected,
            CaptureToUi = Distribution(successes.Select(s => s.Events.First(e => e.Stage == nameof(PipelineStage.UiApplied)).AtMs)),
            Work = samples.SelectMany(s => s.Work).GroupBy(w => w.Work).ToDictionary(g => g.Key, g => Distribution(g.Select(w => w.DurationMs))),
            Outcomes = samples.GroupBy(s => s.Outcome).ToDictionary(g => g.Key, g => g.Count()), Samples = samples
        };
    }

    internal static PipelineDistribution Distribution(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double? Percentile(double p) => sorted.Length == 0 ? null : sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
        return new(sorted.Length, Percentile(.5), Percentile(.95), Percentile(.99),
            sorted.Length == 0 ? null : sorted[^1], sorted.Count(v => v >= 100), sorted.Count(v => v > 500));
    }
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

internal sealed class PipelineTimingRecorder
{
    private readonly object _gate = new();
    private readonly Queue<PipelineTrace> _traces = new();
    private readonly int _capacity;
    private readonly IPipelineClock _clock;
    private long _nextId, _evicted;
    private readonly long _resourceStart, _allocatedStart;
    private readonly double _cpuStart;
    private readonly bool _measureResources;
    internal PipelineTimingRecorder(int capacity = 256, IPipelineClock? clock = null, bool measureResources = false)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _clock = clock ?? MonotonicPipelineClock.Instance;
        _measureResources = measureResources;
        if (_measureResources)
        {
            _resourceStart = Stopwatch.GetTimestamp();
            _allocatedStart = GC.GetTotalAllocatedBytes(false);
            using var process = Process.GetCurrentProcess();
            _cpuStart = process.TotalProcessorTime.TotalMilliseconds;
        }
    }
    internal PipelineTrace Begin(long? parentId = null)
    {
        lock (_gate)
        {
            var trace = new PipelineTrace(++_nextId, parentId, _clock);
            if (_traces.Count == _capacity) { _traces.Dequeue(); _evicted++; }
            _traces.Enqueue(trace);
            return trace;
        }
    }
    internal PipelineReport Snapshot(string origin = "live")
    {
        PipelineTrace[] traces; long total, evicted;
        lock (_gate) { traces = _traces.ToArray(); total = _nextId; evicted = _evicted; }
        var report = PipelineReport.Create(traces.Select(t => t.Snapshot()).ToArray(), total, evicted, origin);
        if (_measureResources)
        {
            using var process = Process.GetCurrentProcess();
            report.ProcessResources = new(Stopwatch.GetElapsedTime(_resourceStart).TotalMilliseconds,
                Math.Max(0, process.TotalProcessorTime.TotalMilliseconds - _cpuStart),
                Math.Max(0, GC.GetTotalAllocatedBytes(false) - _allocatedStart), process.WorkingSet64);
        }
        return report;
    }
}

internal sealed class PipelineTrace
{
    private const int MaximumWorkSamples = 96;
    private readonly object _gate = new();
    private readonly IPipelineClock _clock;
    private readonly long _start;
    private readonly List<PipelineEvent> _events = new();
    private readonly List<PipelineWorkSample> _work = new();
    private PipelineOutcome _outcome;
    private double? _finishedMs;
    private bool _isScene, _deferred;
    private readonly HashSet<string> _chatBoxBlocks = new(StringComparer.Ordinal);
    private int _expected, _translated, _visible, _dropped, _inFlightWork;
    private PipelineCapture? _capture;
    internal long Id { get; }
    internal long? ParentId { get; private set; }
    internal bool IsFinished { get { lock (_gate) return _outcome != PipelineOutcome.Pending; } }
    internal PipelineTrace(long id, long? parentId, IPipelineClock clock)
    { Id = id; ParentId = parentId; _clock = clock; _start = clock.Timestamp; }
    private double Now => Math.Max(0, _clock.Milliseconds(_start, _clock.Timestamp));
    internal void RecordCapture(int width, int height, int originX, int originY)
    { lock (_gate) _capture = new(width, height, originX, originY); }
    internal void AttachToScene(long sceneId)
    { lock (_gate) if (sceneId != Id) ParentId = sceneId; }
    internal void Mark(PipelineStage stage)
    {
        lock (_gate)
        {
            if (_outcome != PipelineOutcome.Pending || _events.Any(e => e.Stage == stage.ToString())) return;
            _events.Add(new(stage.ToString(), Now));
        }
    }
    internal void ObserveScene(int expectedBlocks)
    {
        lock (_gate)
        {
            if (_outcome != PipelineOutcome.Pending) return;
            _isScene = true; _expected = Math.Max(0, expectedBlocks);
        }
        Mark(PipelineStage.SceneObserved);
    }
    internal void SetCoverage(int expected, int translated, int visible)
    {
        lock (_gate)
        {
            if (_outcome != PipelineOutcome.Pending) return;
            _expected = Math.Max(0, expected); _translated = Math.Clamp(translated, 0, _expected);
            _visible = Math.Clamp(Math.Max(_visible, visible), 0, _translated);
        }
    }
    internal void RecordChatBoxBlock(string id, int expected, int translated)
    {
        lock (_gate)
        {
            if (_outcome != PipelineOutcome.Pending) return;
            if (_chatBoxBlocks.Count < 1024 || _chatBoxBlocks.Contains(id)) _chatBoxBlocks.Add(id);
            else _dropped++;
            SetCoverage(expected, translated, _chatBoxBlocks.Count);
            if (_expected > 0 && _visible == _expected) Mark(PipelineStage.UiApplied);
        }
    }
    internal void HoldObservation() { lock (_gate) _deferred = true; }
    internal void ReleaseObservation() { lock (_gate) _deferred = false; }
    internal void Finish(PipelineOutcome outcome)
    {
        if (outcome == PipelineOutcome.Pending) throw new ArgumentOutOfRangeException(nameof(outcome));
        lock (_gate)
        {
            if (_outcome != PipelineOutcome.Pending) return;
            _outcome = outcome; _finishedMs = Now;
        }
    }
    internal void FinishObservation()
    {
        lock (_gate)
            if (!_isScene && !_deferred) Finish(PipelineOutcome.ObservationOnly);
    }
    internal void FinishCaptureIfUnused()
    {
        lock (_gate)
            if (!_events.Any(e => e.Stage == nameof(PipelineStage.OcrStarted)))
                Finish(_events.Any(e => e.Stage == nameof(PipelineStage.CaptureReady))
                    ? PipelineOutcome.Unchanged : PipelineOutcome.Failed);
    }
    internal IDisposable Measure(PipelineWork work)
    {
        lock (_gate) _inFlightWork++;
        return new WorkScope(this, work, _clock.Timestamp);
    }
    private void EndWork(PipelineWork work, long start)
    {
        lock (_gate)
        {
            _inFlightWork--;
            // Late work retains its actual cost but cannot revive a canceled outcome.
            if (_work.Count < MaximumWorkSamples)
                _work.Add(new(work.ToString(), Math.Max(0, _clock.Milliseconds(_start, start)),
                    Math.Max(0, _clock.Milliseconds(start, _clock.Timestamp))));
            else _dropped++;
        }
    }
    internal PipelineSample Snapshot()
    {
        lock (_gate) return new(Id, ParentId, _isScene, _expected, _translated, _visible,
            _outcome.ToString(), _finishedMs ?? Now, _events.ToArray(), _work.ToArray(), _dropped, _capture, _inFlightWork);
    }
    private sealed class WorkScope(PipelineTrace trace, PipelineWork work, long start) : IDisposable
    {
        private PipelineTrace? _trace = trace;
        public void Dispose() => Interlocked.Exchange(ref _trace, null)?.EndWork(work, start);
    }
}

// Measurement-only companion to OcrSceneState. It never decides whether OCR/translation runs.
// Keeping it portable lets replay checks exercise the same lifetime rules used by the UI.
internal sealed class PipelineSceneTiming
{
    internal PipelineTrace? Current { get; private set; }
    private PipelineTrace? _deferred;
    internal void Observe(PipelineTrace? observation, bool needsPresentation, int expectedBlocks)
    {
        if (observation == null) return;
        if (Current == null || (Current.IsFinished && needsPresentation))
        {
            Current = observation;
            Current.ObserveScene(expectedBlocks);
        }
        else observation.AttachToScene(Current.Id);
    }
    internal void Defer(PipelineTrace? observation)
    {
        DiscardDeferred();
        _deferred = observation;
        observation?.HoldObservation();
    }
    internal PipelineTrace? TakeDeferred()
    {
        var result = _deferred;
        _deferred = null;
        result?.ReleaseObservation();
        return result;
    }
    internal void DiscardDeferred() => TakeDeferred()?.FinishObservation();
    internal void Invalidate(bool canceled)
    {
        Current?.Finish(canceled ? PipelineOutcome.Canceled : PipelineOutcome.Superseded);
        Current = null;
        DiscardDeferred();
    }
}

internal static class PipelineDiagnostics
{
    // Opt-in: no stage buffers or report I/O on the normal processing path.
    internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("LOCALAY_PIPELINE_DIAGNOSTICS") == "1";
    private static PipelineTimingRecorder _recorder = new();
    private static readonly AsyncLocal<PipelineTrace?> Ambient = new();
    internal static PipelineTrace? Current => Enabled ? Ambient.Value : null;
    internal static PipelineTrace? BeginCapture() => Enabled ? _recorder.Begin() : null;
    internal static IDisposable? Capture()
    {
        var trace = BeginCapture();
        return trace == null ? null : new CaptureScope(trace, Use(trace));
    }
    internal static void BeginSession() { if (Enabled) _recorder = new(measureResources: true); }
    internal static IDisposable? Use(PipelineTrace? trace)
    {
        if (!Enabled) return null;
        var previous = Ambient.Value; Ambient.Value = trace;
        return new RestoreScope(previous);
    }
    internal static IDisposable? Measure(PipelineWork work) => Current?.Measure(work);
    internal static Task ExportAsync(Func<string> directory)
    {
        if (!Enabled) return Task.CompletedTask;
        var recorder = _recorder;
        return Task.Run(() =>
        {
            try
            {
                var report = recorder.Snapshot();
                string folder = directory(); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"pipeline-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
                File.WriteAllText(path, report.ToJson());
                Console.WriteLine($"[Pipeline timing] Diagnostic report: {path}");
            }
            catch (Exception ex) { Console.WriteLine($"[Pipeline timing] Report failed: {ex.GetType().Name}"); }
        });
    }
    private sealed class RestoreScope(PipelineTrace? previous) : IDisposable
    { public void Dispose() => Ambient.Value = previous; }
    private sealed class CaptureScope(PipelineTrace trace, IDisposable? scope) : IDisposable
    {
        public void Dispose() { trace.FinishCaptureIfUnused(); scope?.Dispose(); }
    }
}
