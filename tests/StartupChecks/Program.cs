using AlpWareStudio.Localay;

int passed = 0;
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
// No wizard, saved region or ChatBox placement is needed once the selected game is ready.
Check(GameWorkflowReadiness.StartIssue(true, false, null, true, false, true) == null,
    "a selected game and valid languages can start without confirmation steps");
Check(GameWorkflowReadiness.StartIssue(false, false, null, true, false, true) == "Flow_GameRequired",
    "saved screen areas cannot substitute for a selected game");
Check(GameWorkflowReadiness.StartIssue(true, true, null, true, false, true) == "Flow_GameRequired",
    "changed capture topology requires choosing the game again");
foreach (string issue in new[] { "Flow_DifferentLanguages", "Flow_LanguageRequired", "Flow_RecognitionUnavailable" })
    Check(GameWorkflowReadiness.StartIssue(true, false, issue, true, false, true) == issue,
        "language readiness is checked directly: " + issue);
Check(GameWorkflowReadiness.StartIssue(true, false, null, false, false, true) == "Flow_OverlayUnavailable",
    "a capture path that would read its own overlay cannot start");
Check(GameWorkflowReadiness.StartIssue(true, false, null, true, true, true) == "Flow_FinishSelection",
    "an unfinished selection blocks start");
Check(GameWorkflowReadiness.StartIssue(true, false, null, true, false, false) == "Flow_ServiceRequired",
    "missing translation credentials block start");
Check(GameWorkflowReadiness.StartIssue(false, false, "Flow_DifferentLanguages", false, true, false) == "Flow_GameRequired",
    "the first missing action follows game-first UI order");
GameSessionChecks.Run(Check);
Check(GameTranslationProfile.IsConfigured("Google Translate", false),
    "the game profile starts with keyless Google web mode");
Check(!GameTranslationProfile.IsConfigured("Google Translate", true) &&
    !GameTranslationProfile.IsConfigured("Ollama", false),
    "Cloud mode and other engines cannot silently replace the keyless game profile");
Check(GameTranslationProfile.IsConfigured("Ollama", true, HyMt2Profile.Model) &&
    !GameTranslationProfile.IsConfigured("Ollama", false, "llama3"),
    "the exact Hy-MT2 profile is eligible independently of legacy Google Cloud settings");
Check(HyMt2Profile.SupportsPair("en", "tr") && HyMt2Profile.SupportsPair("Japanese", "German") &&
    HyMt2Profile.SupportsPair("ch_tra", "ThaiLand") && !HyMt2Profile.SupportsPair("en", "da") &&
    !HyMt2Profile.SupportsPair("en", "English"),
    "local readiness accepts published multilingual pairs and rejects unsupported or identical languages");
Check(!GameTranslationProfile.AllowsDispatch(true, true) && GameTranslationProfile.AllowsDispatch(false, true),
    "managed game batches reject Cloud dispatch even after readiness was checked");
Check(!GameTranslationProfile.AllowsFallback(true) && GameTranslationProfile.AllowsFallback(false),
    "managed game batches cannot switch providers or launch a speculative second engine");
Console.WriteLine($"{passed} startup and game readiness checks passed. No Windows APIs or application execution.");

void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
