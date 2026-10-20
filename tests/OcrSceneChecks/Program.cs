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
Check(state.TryBeginTranslation() && !state.TryBeginTranslation(), "only one top-level translation can own the scene at a time");
state.Reset();
Check(state.Latest == null && state.Rendered == null && state.TakeDeferred() == null,
    "stop or capture/language changes discard the previous scene and queued OCR");
Check(state.IsTranslating && !state.TryBeginTranslation(), "clearing a scene cannot release a request that is still running");
state.EndTranslation();
Check(!state.IsTranslating && state.TryBeginTranslation(), "only completion releases the next translation");
Console.WriteLine($"{passed} OCR scene checks passed. Windows capture and overlay rendering require Windows.");
