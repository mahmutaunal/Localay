using System.Diagnostics;
using System.Text.Json;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException("FAILED: " + description);
    passed++;
    Console.WriteLine("PASS: " + description);
}

if (args.Length == 2 && args[0] == "--replay")
{
    var input = JsonSerializer.Deserialize<PipelineReport>(File.ReadAllText(args[1]))
        ?? throw new InvalidDataException("Missing report.");
    if (input.SchemaVersion != 1 || input.Samples.Length > 10000)
        throw new InvalidDataException("Unsupported report schema/size.");
    foreach (var sample in input.Samples)
    {
        if (sample.DurationMs < 0 || !double.IsFinite(sample.DurationMs) ||
            sample.ExpectedBlocks < 0 || sample.VisibleBlocks < 0 ||
            sample.TranslatedBlocks < sample.VisibleBlocks || sample.ExpectedBlocks < sample.TranslatedBlocks ||
            sample.Events.Any(e => e.AtMs < 0 || !double.IsFinite(e.AtMs)) ||
            sample.Work.Any(w => w.StartMs < 0 || w.DurationMs < 0 || !double.IsFinite(w.StartMs + w.DurationMs)))
            throw new InvalidDataException("Invalid timing sample.");
    }
    Console.WriteLine(PipelineReport.Create(input.Samples, input.TotalStarted, input.EvictedSamples,
        input.Origin == "synthetic-stage-replay" ? input.Origin : "recorded-stage-replay").ToJson());
    return;
}

var clock = new ManualClock();
var recorder = new PipelineTimingRecorder(64, clock);
var scene = new PipelineSceneTiming();
var first = recorder.Begin();
first.Mark(PipelineStage.CaptureReady);
first.Mark(PipelineStage.OcrStarted);
clock.Advance(20);
first.Mark(PipelineStage.OcrCompleted);
scene.Observe(first, true, 2);
clock.Advance(250);
var recheck = recorder.Begin();
scene.Observe(recheck, true, 2);
recheck.FinishObservation();
Check(scene.Current == first && recheck.Snapshot().ParentId == first.Id,
    "settling and OCR rechecks retain the first capture clock and scene identity");
clock.Advance(30);
first.SetCoverage(2, 2, 2);
first.Mark(PipelineStage.UiApplied);
first.Finish(PipelineOutcome.UiApplied);
var result = recorder.Snapshot();
Check(result.CaptureToUi.P95Ms == 300 && result.CaptureToUi.AtOrAbove100Ms == 1,
    "capture-to-UI includes settling, rather than just the final provider request");

var old = recorder.Begin();
scene.Observe(old, true, 1);
var middle = recorder.Begin();
scene.Defer(middle);
middle.FinishObservation();
Check(!middle.IsFinished, "deferred OCR retains its original observation until consumed");
var latest = recorder.Begin();
scene.Defer(latest);
Check(middle.IsFinished && scene.TakeDeferred() == latest,
    "replaced deferred measurements close while the newest observation survives");
old.Finish(PipelineOutcome.Failed);
scene.Observe(latest, true, 1);
Check(scene.Current == latest && !latest.IsFinished, "deferred layout can start a fresh presentation measurement");
scene.Invalidate(true);
var restarted = recorder.Begin();
scene.Observe(restarted, true, 1);
old.Finish(PipelineOutcome.UiApplied);
Check(old.Snapshot().Outcome == nameof(PipelineOutcome.Failed) && !restarted.IsFinished,
    "a late completion cannot rewrite an outcome or finish the restarted scene");

var overlap = recorder.Begin();
using (overlap.Measure(PipelineWork.Provider))
{
    clock.Advance(10);
    using (overlap.Measure(PipelineWork.Provider)) clock.Advance(20);
    clock.Advance(10);
}
Check(overlap.Snapshot().Work.Select(w => w.DurationMs).Order().SequenceEqual(new[] { 20d, 40d }),
    "overlapping provider work has independent spans, not one shared stopwatch");
overlap.Mark(PipelineStage.SceneObserved);
clock.Advance(10);
overlap.Mark(PipelineStage.SceneObserved);
Check(overlap.Snapshot().Events.Count(e => e.Stage == nameof(PipelineStage.SceneObserved)) == 1,
    "repeated markers cannot move the start of a stage forward");
var late = recorder.Begin();
var lateWork = late.Measure(PipelineWork.Provider);
clock.Advance(5);
late.Finish(PipelineOutcome.Canceled);
clock.Advance(995);
lateWork.Dispose(); lateWork.Dispose();
Check(late.Snapshot().Outcome == nameof(PipelineOutcome.Canceled) &&
    late.Snapshot().DurationMs == 5 && late.Snapshot().Work.Single().DurationMs == 1000,
    "uncooperative canceled work keeps its real resource time without counting as success");

var coverageRecorder = new PipelineTimingRecorder(8, clock);
foreach (var outcome in new[] { PipelineOutcome.UiApplied, PipelineOutcome.Partial, PipelineOutcome.Failed, PipelineOutcome.Pending })
{
    var trace = coverageRecorder.Begin();
    trace.ObserveScene(2);
    trace.SetCoverage(2, outcome == PipelineOutcome.UiApplied ? 2 : outcome == PipelineOutcome.Partial ? 1 : 0,
        outcome == PipelineOutcome.UiApplied ? 2 : outcome == PipelineOutcome.Partial ? 1 : 0);
    if (outcome == PipelineOutcome.UiApplied) trace.Mark(PipelineStage.UiApplied);
    if (outcome != PipelineOutcome.Pending) trace.Finish(outcome);
}
var coverage = coverageRecorder.Snapshot();
Check(coverage.SuccessfulScenes == 1 && coverage.UnsuccessfulScenes == 2 && coverage.PendingScenes == 1 &&
    coverage.VisibleBlockCoverage == 3d / 8, "failure, partial and pending scenes remain in the coverage denominator");
var fabricated = coverageRecorder.Begin();
fabricated.ObserveScene(2); fabricated.SetCoverage(2, 2, 2); fabricated.Finish(PipelineOutcome.UiApplied);
Check(coverageRecorder.Snapshot().SuccessfulScenes == 1, "completion without a UI marker is not presentation success");
var chat = coverageRecorder.Begin();
chat.ObserveScene(2);
chat.RecordChatBoxBlock("1", 2, 2); chat.RecordChatBoxBlock("1", 2, 2);
Check(!chat.Snapshot().Events.Any(e => e.Stage == nameof(PipelineStage.UiApplied)),
    "duplicate ChatBox callbacks do not double-count visible blocks");
chat.RecordChatBoxBlock("2", 2, 2);
Check(chat.Snapshot().VisibleBlocks == 2, "ChatBox coverage records distinct block callbacks");
var distribution = PipelineReport.Distribution(Enumerable.Range(1, 100).Select(i => (double)i));
Check(distribution.P50Ms == 50 && distribution.P95Ms == 95 && distribution.P99Ms == 99 &&
    distribution.AtOrAbove100Ms == 1, "percentiles and the strict under-100-ms boundary use known values");
Check(PipelineReport.Distribution([]).P95Ms == null, "an empty success set does not report a fabricated zero-ms result");
var bounded = new PipelineTimingRecorder(4, clock);
for (int i = 0; i < 100; i++) bounded.Begin().Finish(PipelineOutcome.Unchanged);
Check(bounded.Snapshot().RetainedSamples == 4 && bounded.Snapshot().EvictedSamples == 96,
    "bounded history reports evictions instead of hiding sampling limits");
for (int i = 0; i < 150; i++) using (late.Measure(PipelineWork.Ocr)) { }
Check(late.Snapshot().Work.Length == 96 && late.Snapshot().DroppedEvents == 55,
    "per-operation work buffers are bounded, including late work");

// Exercise production AsyncLocal/HTTP wrapper integration in both diagnostics modes.
var ambientTrace = PipelineDiagnostics.BeginCapture();
using (PipelineDiagnostics.Use(ambientTrace))
{
    Check(PipelineDiagnostics.Current == ambientTrace, "ambient scope belongs to the caller's operation");
    var continuation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var request = TranslationRequestContext.RunAsync(new ControlledService(continuation.Task), "private input", "private prompt", default);
    Check(!request.IsCompleted, "provider timing does not make an asynchronous provider synchronous");
    var other = PipelineDiagnostics.BeginCapture();
    using (PipelineDiagnostics.Use(other))
    {
        continuation.SetResult("private output");
        await request;
        Check(PipelineDiagnostics.Current == other, "awaiting an older request cannot overwrite the current caller scope");
    }
    if (ambientTrace != null)
        Check(ambientTrace.Snapshot().Work.Count(w => w.Work == nameof(PipelineWork.Provider)) == 1,
            "the production provider wrapper records into the originating job");
}
Check(PipelineDiagnostics.Current == null, "ambient scope restores after completion");
if (PipelineDiagnostics.Enabled)
{
    var ignoredCancellation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource();
    var trace = PipelineDiagnostics.BeginCapture()!;
    using (PipelineDiagnostics.Use(trace))
    {
        var request = TranslationRequestContext.RunAsync(new ControlledService(ignoredCancellation.Task), "", "", cancellation.Token);
        cancellation.Cancel();
        try { await request; throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { }
        trace.Finish(PipelineOutcome.Canceled);
        Check(trace.Snapshot().Work.Length == 0 && trace.Snapshot().InFlightWork == 1,
            "canceling the foreground wait reports the provider as still in flight");
        ignoredCancellation.SetResult("late result");
        for (int i = 0; i < 1000 && trace.Snapshot().Work.Length == 0; i++) await Task.Delay(1);
        Check(trace.Snapshot().Work.Length == 1 && trace.Snapshot().InFlightWork == 0 && trace.Snapshot().Outcome == nameof(PipelineOutcome.Canceled),
            "the production wrapper records late provider completion without reviving its canceled scene");
    }
}
if (!PipelineDiagnostics.Enabled)
{
    int directoryCalls = 0;
    await PipelineDiagnostics.ExportAsync(() => { directoryCalls++; return "unused"; });
    Check(directoryCalls == 0 && PipelineDiagnostics.BeginCapture() == null,
        "disabled diagnostics allocate no traces and do not access the report directory");
}
var json = coverage.ToJson();
Check(!json.Contains("private input") && !json.Contains("private output") && !json.Contains("private prompt") &&
    coverage.SourceOnset == "not_measured" && coverage.PhysicalPresentation == "not_measured" &&
    coverage.SemanticCorrectness == "not_measured", "report schema excludes content and identifies measurement limits");
var resources = new PipelineTimingRecorder(measureResources: true).Snapshot().ProcessResources;
Check(resources is { ProcessCpuMs: >= 0, ProcessAllocatedBytes: >= 0, WorkingSetBytes: > 0 },
    "opt-in resource sampling reports process CPU/allocation/working set without per-frame process queries");

var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "scenarios.json");
var fixtures = JsonSerializer.Deserialize<ReplayFixture[]>(File.ReadAllText(fixturePath))!;
var replaySamples = new List<PipelineSample>();
foreach (var fixture in fixtures)
{
    var replayClock = new ManualClock();
    var replayRecorder = new PipelineTimingRecorder(8, replayClock);
    var trace = replayRecorder.Begin();
    foreach (var entry in fixture.Events)
    {
        replayClock.Set(entry.AtMs);
        trace.Mark(Enum.Parse<PipelineStage>(entry.Stage));
        if (entry.Stage == nameof(PipelineStage.OcrCompleted)) trace.ObserveScene(fixture.Blocks);
    }
    replayClock.Set(fixture.FinishMs);
    trace.SetCoverage(fixture.Blocks, fixture.Translated, fixture.Visible);
    trace.Finish(Enum.Parse<PipelineOutcome>(fixture.Outcome));
    var report = replayRecorder.Snapshot("synthetic-stage-replay");
    replaySamples.Add(report.Samples.Single() with { Id = replaySamples.Count + 1 });
    Check(report.SuccessfulScenes == fixture.Successes && report.CaptureToUi.MaxMs == fixture.ExpectedUiMs,
        "stage replay: " + fixture.Name);
}
int reportArgument = Array.IndexOf(args, "--report");
if (reportArgument >= 0 && reportArgument + 1 < args.Length)
{
    string path = Path.GetFullPath(args[reportArgument + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, PipelineReport.Create(replaySamples.ToArray(), replaySamples.Count, 0,
        "synthetic-stage-replay").ToJson());
}

// Run explicitly with --benchmark; this measures instrumentation overhead, not OCR performance.
if (args.Contains("--benchmark"))
{
    var benchmark = new PipelineTimingRecorder();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    for (int i = 0; i < 20000; i++)
    {
        var trace = benchmark.Begin(); trace.Mark(PipelineStage.CaptureReady); trace.ObserveScene(1);
        using (trace.Measure(PipelineWork.Provider)) { }
        trace.SetCoverage(1, 1, 1); trace.Mark(PipelineStage.UiApplied); trace.Finish(PipelineOutcome.UiApplied);
    }
    watch.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new { Kind = "diagnostics-overhead-only", Operations = 20000,
        Milliseconds = watch.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before,
        RetainedSamples = benchmark.Snapshot().RetainedSamples }));
}
Console.WriteLine($"{passed} pipeline timing checks passed. Synthetic replay is not a Windows OCR/display benchmark.");

sealed class ManualClock : IPipelineClock
{
    private long _timestamp;
    public long Timestamp => _timestamp;
    public double Milliseconds(long start, long end) => (end - start) / 1000d;
    internal void Advance(double milliseconds) => _timestamp += (long)(milliseconds * 1000);
    internal void Set(double milliseconds)
    {
        long next = (long)(milliseconds * 1000);
        if (next < _timestamp) throw new InvalidOperationException("Replay time must be monotonic.");
        _timestamp = next;
    }
}
sealed class ControlledService(Task<string?> result) : ITranslationService
{ public Task<string?> TranslateAsync(string jsonData, string prompt) => result; }
sealed record ReplayFixture(string Name, int Blocks, int Translated, int Visible, string Outcome,
    double FinishMs, ReplayEvent[] Events, int Successes, double? ExpectedUiMs);
sealed record ReplayEvent(string Stage, double AtMs);
