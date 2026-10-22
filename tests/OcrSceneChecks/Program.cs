using System.Text.Json;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
OcrSceneSnapshot Scene(double y, string text = "Ubisoft Connect", double width = 180) =>
    new([new(text, 20, y, width, 24)]);

var state = new OcrSceneState();
var original = Scene(200);
state.Observe(original);
Check(!state.IsCurrent, "new text is not visible before its geometry is rendered");
state.MarkRendered(original);
Check(state.IsCurrent, "rendered text at the current position is visible");

var scrolled = Scene(70);
Check(!state.Observe(scrolled), "moving the same text does not replace its translation session");
Check(original.HasSameText(scrolled), "scrolling preserves translation identity");
Check(!state.IsCurrent, "scrolling invalidates the old boxes and erased background");
state.Defer("middle scroll result");
var latest = Scene(40);
state.Observe(latest);
state.Defer("latest scroll result");
Check(!state.IsCurrent, "finishing an old translation cannot validate its old geometry");
Check(state.TakeDeferred() == "latest scroll result" && !state.HasDeferred, "busy translation retains only the latest OCR result");
state.MarkRendered(latest);
Check(state.IsCurrent, "reusing a translation at the latest geometry makes it visible");

state.Observe(Scene(41));
Check(state.IsCurrent, "one-pixel OCR jitter does not clear a valid overlay");
state.Observe(Scene(42));
Check(state.IsCurrent, "two-pixel OCR jitter is tolerated");
state.Observe(Scene(43));
Check(!state.IsCurrent, "accumulated movement is measured against rendered coordinates");
state.Observe(Scene(40, width: 220));
Check(!state.IsCurrent, "a resized text box invalidates its old background");

var changed = Scene(40, "Ubisoft Store");
Check(state.Observe(changed), "different text requests invalidation of the previous translation session");
Check(!state.IsCurrent && !latest.HasSameText(changed), "changed content cannot inherit an old translation");
Check(!Scene(40, "Level 1").HasSameText(Scene(40, "Level 2")), "similar text with a different number is a different translation");
Check(!Scene(40, "Play!").HasSameText(Scene(40, "Play?")), "punctuation is part of translation identity");

var empty = new OcrSceneSnapshot([]);
state.Observe(empty);
Check(!state.IsCurrent, "text disappearing hides old translations immediately");
state.MarkRendered(empty);
Check(state.IsCurrent && empty.IsEmpty, "an empty scene can be committed without a settling delay");

var duplicates = new OcrSceneSnapshot([new("Play", 0, 0, 40, 20), new("Play", 0, 40, 40, 20)]);
Check(!duplicates.HasSameText(new([new("Play", 0, 0, 40, 20)])), "duplicate text blocks retain their count");
var ordered = new OcrSceneSnapshot([new("A", 0, 0, 40, 20), new("B", 0, 40, 40, 20)]);
var reordered = new OcrSceneSnapshot([new("B", 0, 0, 40, 20), new("A", 0, 40, 40, 20)]);
Check(!ordered.HasSameText(reordered), "reordered text blocks are never paired by the old index");

using var json = JsonDocument.Parse("""
[
  {"text":"Ubisoft Connect","confidence":0.9,"rect":[[20,40],[200,40],[200,64],[20,64]]},
  {"text":"x","confidence":0.9,"rect":[[0,0],[1,0],[1,1],[0,1]]},
  {"text":"   ","confidence":0.9,"rect":[[0,0],[1,0],[1,1],[0,1]]}
]
""");
var parsed = OcrSceneSnapshot.FromResults(json.RootElement, minimumTextLength: 2);
Check(parsed.HasSameLayout(latest), "production OCR polygons and fragment filtering produce the expected layout");

state.Defer("old session");
Check(state.TryBeginTranslation(out long firstTranslation) && !state.TryBeginTranslation(out _),
    "only one top-level translation can own the scene at a time");
state.Reset();
Check(state.Latest == null && state.Rendered == null && state.TakeDeferred() == null,
    "clearing presentation discards the previous scene and queued OCR");
Check(state.IsTranslating && !state.TryBeginTranslation(out _), "clearing presentation alone does not finish an active request");
Check(state.EndTranslation(firstTranslation) && !state.IsTranslating, "the current owner can complete its translation");
Check(state.TryBeginTranslation(out long canceledTranslation), "another translation can acquire ownership");
long oldGeneration = state.Generation;
state.Defer("old deferred frame");
state.CancelTranslation();
Check(!state.IsTranslating && state.Generation != oldGeneration && !state.HasDeferred,
    "cancellation immediately releases ownership and invalidates deferred OCR");
Check(state.TryBeginTranslation(out long currentTranslation), "restart acquires ownership without waiting for the canceled request");
Check(!state.EndTranslation(canceledTranslation) && state.IsTranslating,
    "a canceled request's late finally cannot release the new owner");
state.Defer("new deferred frame");
if (oldGeneration == state.Generation) state.TakeDeferred(); // A queued old callback's guard.
Check(state.HasDeferred, "an old-generation callback cannot drain the new session's OCR");
Check(state.EndTranslation(currentTranslation) && !state.IsTranslating, "the new owner can finish normally");
Check(!state.EndTranslation(currentTranslation), "a repeated completion has no effect");

OcrSceneBlock Block(string text, double x, double y, double width = 40) => new(text, x, y, width, 20);
var oldBlocks = new[] { Block("Header", 0, 0), Block("First", 0, 100), Block("Second", 0, 200) };
var matched = OcrBlockMatcher.Match(oldBlocks,
    new[] { Block("Second", 0, 150), Block("Header", 0, 0), Block("New", 0, 200), Block("First", 0, 50) });
Check(matched.SequenceEqual(new[] { 2, 0, -1, 1 }), "reordered scrolling blocks keep exact identities while new text stays unmatched");
matched = OcrBlockMatcher.Match(oldBlocks, new[] { Block("Header", 0, 0), Block("Changed", 0, 100), Block("Second", 0, 200) });
Check(matched.SequenceEqual(new[] { 0, -1, 2 }), "one changed block does not invalidate unrelated block identities");
Check(OcrBlockMatcher.Match(new[] { Block("Level 1", 0, 0) }, new[] { Block("Level 2", 0, 0) })[0] == -1,
    "same geometry cannot transfer a translation across changed numbers");
Check(OcrBlockMatcher.Match(new[] { Block("Play!", 0, 0) }, new[] { Block("Play?", 0, 0) })[0] == -1,
    "punctuation changes cannot reuse source identity");
var repeated = new[] { Block("Play", 0, 100), Block("Play", 0, 200) };
Check(OcrBlockMatcher.Match(repeated, new[] { Block("Play", 0, 100) })[0] == -1,
    "a repeated label at an old position remains ambiguous without motion anchors");
var anchored = new[] { Block("Anchor A", 80, 100), Block("Anchor B", 80, 200), Block("Play", 0, 100), Block("Play", 0, 200) };
matched = OcrBlockMatcher.Match(anchored,
    new[] { Block("Play", 0, 150), Block("Anchor B", 80, 150), Block("Play", 0, 50), Block("Anchor A", 80, 50) });
Check(matched.SequenceEqual(new[] { 3, 1, 2, 0 }), "two independent anchors separate repeated labels after scrolling and OCR reordering");
Check(OcrBlockMatcher.Match(anchored, new[] { Block("Anchor A", 80, 50), Block("Play", 0, 50) })[1] == -1,
    "one motion anchor cannot authorize duplicate reuse");
var twoLayers = anchored.Concat(new[] { Block("Fixed A", 150, 0), Block("Fixed B", 220, 0) }).ToArray();
matched = OcrBlockMatcher.Match(twoLayers,
    new[] { Block("Anchor A", 80, 0), Block("Anchor B", 80, 100), Block("Fixed A", 150, 0), Block("Fixed B", 220, 0), Block("Play", 0, 100) });
Check(matched[4] == -1, "competing stationary and scrolling matches cannot guess duplicate ownership");
matched = OcrBlockMatcher.Match(anchored,
    new[] { Block("Anchor A", 80, 50), Block("Anchor B", 80, 150), Block("Play", 0, 50, 60) });
Check(matched[2] == -1, "a resized duplicate needs fresh recognition rather than a guessed identity");
matched = OcrBlockMatcher.Match(anchored,
    new[] { Block("Anchor A", 80, 50), Block("Anchor B", 80, 150), Block("Play", 0, 50), Block("Play", 0, 51) });
Check(matched[2] == -1 && matched[3] == -1, "two new labels cannot claim the same old duplicate");
Check(OcrBlockMatcher.Match(Array.Empty<OcrSceneBlock>(), oldBlocks).All(i => i == -1), "first observation has no inherited identities");
Check(OcrBlockMatcher.Match(oldBlocks, Array.Empty<OcrSceneBlock>()).Length == 0, "all disappeared blocks leave no retained mapping");

const int frameWidth = 193, frameHeight = 129;
byte[] pixels = new byte[frameWidth * frameHeight * 4];
byte[] nextPixels = (byte[])pixels.Clone();
void Pixel(byte[] target, int x, int y, byte value, int channel = 0) => target[(y * frameWidth + x) * 4 + channel] = value;
var damage = FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight);
Check(!damage.HasChanges && !damage.Intersects(0, 0, frameWidth, frameHeight), "identical screen pixels keep all regions valid");
Pixel(nextPixels, 192, 128, 1);
damage = FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight);
Check(damage.HasChanges && damage.Intersects(192, 128, 1, 1), "one low-contrast edge pixel is detected without downsampling");
Check(!damage.Intersects(0, 0, 20, 20), "a changed tile does not invalidate a distant stationary header");
Check(!damage.Intersects(-100, -100, 10, 10) && !damage.Intersects(300, 300, 20, 20), "offscreen boxes are clipped safely");
Check(damage.Intersects(double.NaN, 0, 20, 20), "unknown geometry fails closed");
nextPixels = (byte[])pixels.Clone(); Pixel(nextPixels, 0, 0, 255, 3);
Check(!FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight).HasChanges, "alpha-only changes do not trigger OCR");
for (int channel = 0; channel < 3; channel++)
{
    nextPixels = (byte[])pixels.Clone(); Pixel(nextPixels, 70, 70, 1, channel);
    Check(FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight).Intersects(70, 70, 1, 1),
        $"RGB channel {channel} preserves subtle text changes");
}
var cumulative = FrameChangeMap.Empty(frameWidth, frameHeight);
cumulative.Include(damage);
cumulative.Include(FrameChangeMap.Compare(pixels, pixels, frameWidth, frameHeight));
Check(cumulative.Intersects(192, 128, 1, 1), "returning to unchanged pixels cannot revalidate damage accumulated during OCR");
cumulative.Include(FrameChangeMap.All(frameWidth + 1, frameHeight));
Check(cumulative.WholeFrame && cumulative.Intersects(0, 0, 1, 1), "capture resize invalidates the complete old coordinate system");
Check(FrameChangeMap.All(frameWidth, frameHeight).Intersects(0, 0, 1, 1), "capture origin changes can invalidate every old patch");

using var observationJson = JsonDocument.Parse("""
[
 {"text":"Header","confidence":0.9,"rect":[[5,5],[30,5],[30,20],[5,20]]},
 {"text":"Body","confidence":0.9,"rect":[[100,90],[140,90],[140,110],[100,110]]}
]
""");
var input = observationJson.RootElement;
var observing = new OcrFrameObservation(frameWidth, frameHeight);
Check(observing.FilterResults(input).FullyCurrent && observing.FilterResults(input).Results.GetArrayLength() == 2,
    "unmodified OCR input accepts both source blocks");
nextPixels = (byte[])pixels.Clone(); Pixel(nextPixels, 150, 100, 1);
observing.Include(FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight));
var filtered = observing.FilterResults(input);
Check(!filtered.FullyCurrent && filtered.Results.GetArrayLength() == 1 && filtered.Results[0].GetProperty("text").GetString() == "Header",
    "movement in the eraser margin rejects a stale body without discarding the stationary header");
observing.Include(FrameChangeMap.Compare(pixels, pixels, frameWidth, frameHeight));
Check(observing.FilterResults(input).Results.GetArrayLength() == 1, "an old OCR result cannot revive a stale patch when motion stops");
var freshObservation = new OcrFrameObservation(frameWidth, frameHeight);
Check(freshObservation.FilterResults(input).FullyCurrent && !observing.FilterResults(input).FullyCurrent,
    "a fresh OCR input has its own validity and cannot make the old input current");
observing.Include(FrameChangeMap.All(frameWidth, frameHeight));
Check(observing.FilterResults(input).Results.GetArrayLength() == 0, "a moved capture rejects every block from the old OCR input");
using var invalidGeometry = JsonDocument.Parse("""[{"text":"bad","confidence":0.9,"rect":[]}]""");
Check(!freshObservation.FilterResults(invalidGeometry.RootElement).FullyCurrent &&
    freshObservation.FilterResults(invalidGeometry.RootElement).Results.GetArrayLength() == 0,
    "malformed source geometry cannot validate an overlay");
using var invalidPoint = JsonDocument.Parse("""[{"text":"bad","confidence":0.9,"rect":[["x",0],[20,20]]},42]""");
Check(!freshObservation.FilterResults(invalidPoint.RootElement).FullyCurrent &&
    freshObservation.FilterResults(invalidPoint.RootElement).Results.GetArrayLength() == 0,
    "nonnumeric points and nonobject results are rejected without raising an exception");

// Provider completion and capture observation share the actual production state types.
var inFlight = new OcrSceneState();
inFlight.Observe(original); inFlight.MarkRendered(original);
Check(inFlight.TryBeginTranslation(out long busyOwner), "a provider can own translation while capture keeps observing");
inFlight.Observe(scrolled); inFlight.Defer("geometry before another scroll");
var whileBusy = new OcrFrameObservation(frameWidth, frameHeight);
whileBusy.Include(FrameChangeMap.Compare(nextPixels, pixels, frameWidth, frameHeight));
inFlight.TakeDeferred(); // Production capture invalidates queued geometry without releasing the provider.
Check(inFlight.IsTranslating && !inFlight.HasDeferred && !whileBusy.FilterResults(input).FullyCurrent,
    "capture damage drops stale deferred geometry without releasing unrelated translation ownership");
inFlight.Observe(latest); inFlight.Defer("fresh geometry");
Check(inFlight.EndTranslation(busyOwner) && inFlight.TakeDeferred() == "fresh geometry",
    "provider completion releases only the latest verified geometry");
OcrSettlementChecks.Run(Check);
Console.WriteLine($"{passed} OCR scene checks passed. No Windows capture, downloads or user settings were accessed.");
