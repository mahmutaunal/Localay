using AlpWareStudio.Localay;

var completed = new List<string>();
var failures = new List<(string Phase, Exception Error)>();
var expected = new InvalidOperationException("Simulated optional feature failure");
int count = StartupSequence.Run((phase, error) => failures.Add((phase, error)),
    ("Window placement", () => completed.Add("placement")),
    ("Optional feature", () => throw expected),
    ("Settings command", () => completed.Add("settings")),
    ("Screen list", () => completed.Add("screens")));
Check(count == 1 && failures.Count == 1 && failures[0] == ("Optional feature", expected),
    "failed startup features retain their phase and original exception");
Check(completed.SequenceEqual(new[] { "placement", "settings", "screens" }),
    "one failed feature does not skip the remaining startup work");
Check(StartupSequence.Run((_, _) => throw new Exception("Unexpected failure"),
    ("First", () => { }), ("Second", () => { })) == 0,
    "successful startup does not report a failure");
Console.WriteLine("3 startup checks passed. WPF window lifecycle requires Windows.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}
