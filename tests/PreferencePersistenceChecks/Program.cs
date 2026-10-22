using System.ComponentModel;
using System.Text.Json;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
string temporary = Path.Combine(Path.GetTempPath(), "Localay.PreferenceChecks." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    string path = Path.Combine(temporary, "preferences.json");
    var values = new Dictionary<string, string> { ["language_interface"] = "en", ["source_language"] = "ja" };
    void Persist(IReadOnlyDictionary<string, string> candidate) => AppPaths.WriteAtomic(path, JsonSerializer.Serialize(candidate));
    Persist(values);
    Check(PreferenceCommit.TrySet(values, "language_interface", "tr", candidate =>
    {
        Check(values["language_interface"] == "en", "visible state remains unchanged while the new preference is being saved");
        Persist(candidate);
    }, out var error) && error == null && values["language_interface"] == "tr",
        "successful persistence commits the preference in memory");
    Check(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!["language_interface"] == "tr",
        "a fresh reader restores the committed choice");
    string committed = File.ReadAllText(path);
    var failure = new IOException("Simulated replacement failure");
    Check(!PreferenceCommit.TrySet(values, "language_interface", "en", _ => throw failure, out error) && ReferenceEquals(error, failure),
        "persistence failures return the original error");
    Check(values["language_interface"] == "tr" && File.ReadAllText(path) == committed,
        "a failed write preserves both the committed file and visible preference");
    Check(!PreferenceCommit.TrySet(values, "language_interface", "en", _ => throw new UnauthorizedAccessException(), out error) &&
        error is UnauthorizedAccessException && values["language_interface"] == "tr", "access denial does not apply an unsaved choice");
    string blocked = Path.Combine(temporary, "blocked");
    Directory.CreateDirectory(blocked);
    Check(!PreferenceCommit.TrySet(values, "language_interface", "en",
        candidate => AppPaths.WriteAtomic(blocked, JsonSerializer.Serialize(candidate)), out error),
        "a real filesystem replacement failure is detected");
    Check(values["language_interface"] == "tr" && Directory.GetFiles(temporary, "*.tmp").Length == 0,
        "failed atomic replacement cleans its temporary file and preserves the preference");

    var batch = new Dictionary<string, string>
    {
        ["language_interface"] = "en", ["theme_mode"] = "light", ["dark_mode"] = "false", ["unrelated"] = "keep"
    };
    var edits = new Dictionary<string, string> { ["language_interface"] = "tr", ["theme_mode"] = "dark" };
    Check(!PreferenceCommit.TryUpdate(batch, edits, _ => throw failure, out error, "dark_mode") &&
        batch["language_interface"] == "en" && batch["theme_mode"] == "light" && batch.ContainsKey("dark_mode"),
        "failed batch commit changes neither preference and keeps the legacy key");
    int writes = 0;
    Check(PreferenceCommit.TryUpdate(batch, edits, candidate =>
    {
        writes++;
        Check(batch["language_interface"] == "en" && batch["theme_mode"] == "light",
            "both visible preferences stay committed while the replacement is written");
        Check(candidate["language_interface"] == "tr" && candidate["theme_mode"] == "dark" &&
            candidate["unrelated"] == "keep" && !candidate.ContainsKey("dark_mode"),
            "one candidate merges both edits, preserves other settings and removes legacy theme");
        Persist(candidate);
    }, out error, "dark_mode") && writes == 1 && batch["language_interface"] == "tr" && batch["theme_mode"] == "dark",
        "both preferences publish after exactly one successful replacement");
    var batchOnDisk = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    Check(batchOnDisk["language_interface"] == "tr" && batchOnDisk["theme_mode"] == "dark",
        "fresh reader sees the complete batch");

    var baseline = new GeneralPreferenceValues("system", "system");
    var draft = new GeneralPreferenceDraft(baseline);
    Check(!draft.IsDirty, "new interface draft is clean");
    draft.SetLanguage("tr"); draft.SetTheme("dark");
    Check(draft.IsDirty && draft.Baseline == baseline && draft.Value == new GeneralPreferenceValues("tr", "dark"),
        "draft edits never mutate saved preferences");
    draft.Observe(baseline);
    Check(draft.Value.Language == "tr" && !draft.HasConflict(baseline), "effective system refresh preserves a dirty draft");
    var external = new GeneralPreferenceValues("en", "light");
    draft.Observe(external);
    Check(draft.HasConflict(external) && draft.Value.Theme == "dark", "external changes preserve the draft and surface a conflict");
    draft.Reset(external);
    Check(!draft.IsDirty && draft.Value == external, "cancel restores current committed preferences");
    draft.SetTheme("dark"); draft.SetTheme("light");
    Check(!draft.IsDirty, "restoring saved selections clears dirty state");
    draft.Observe(baseline);
    Check(draft.Value == baseline && !draft.IsDirty, "clean drafts follow changed saved preferences");

    var general = new GeneralPreferenceDraft(new GeneralPreferenceValues("tr", "light"));
    general.SetClipboardEnabled(true);
    Check(general.Value.ValidationIssue == "Draft_ClipboardConsentRequired" && !general.Baseline.ClipboardEnabled,
        "enabling clipboard is draft-only and requires consent");
    general.SetClipboardConsent(true);
    Check(general.Value.ValidationIssue == null, "explicit draft consent permits saving valid options");
    general.SetClipboardEnabled(false);
    Check(!general.Value.ClipboardConsent, "disabling clipboard revokes draft consent");
    general.SetClipboardEnabled(true);
    Check(general.Value.ValidationIssue == "Draft_ClipboardConsentRequired", "re-enabling after revocation requires consent again");
    general.SetClipboardConsent(true);
    foreach (string input in new[] { "", "abc", "99", "2001", "1.5", "2147483648" })
    {
        general.SetClipboardDebounce(input);
        Check(general.Value.ValidationIssue == "Draft_ClipboardDelayInvalid" && general.Value.ClipboardDebounce == input,
            "invalid debounce remains in the draft: " + input);
    }
    foreach (string input in new[] { "100", "750", "2000" })
    {
        general.SetClipboardDebounce(input);
        Check(general.Value.ValidationIssue == null, "valid debounce boundary/value: " + input);
    }
    foreach (string input in new[] { "", "9", "10001", "2.5" })
    {
        general.SetClipboardMaxChars(input);
        Check(general.Value.ValidationIssue == "Draft_ClipboardLimitInvalid", "invalid character limit: " + input);
    }
    general.SetClipboardMaxChars("10");
    Check(general.Value.ValidationIssue == null, "minimum character limit is valid");
    general.SetClipboardMaxChars("10000");
    Check(general.Value.ValidationIssue == null, "maximum character limit is valid");
    var externalClipboard = general.Baseline with { ClipboardDebounce = "600" };
    general.Observe(externalClipboard);
    Check(general.HasConflict(externalClipboard) && general.Value.ClipboardMaxChars == "10000",
        "external clipboard edits trigger conflict without overwriting draft inputs");
    general.Reset(externalClipboard);
    Check(!general.IsDirty && general.Value == externalClipboard, "cancel restores every General option");

    var serviceValues = new Dictionary<string, string>(ServicePreferenceDraft.Defaults);
    foreach (var provider in ServicePreferenceDraft.KeyPrefixes)
    {
        serviceValues[provider.Value + "_api_keys"] = "";
        serviceValues[provider.Value + "_api_key"] = "";
    }
    foreach (string provider in ServicePreferenceDraft.Services.Where(ServicePreferenceDraft.UsesPrompt))
        serviceValues[ServicePreferenceDraft.PromptKey(provider)] = "Translate source_language to target_language.";
    var serviceDraft = new ServicePreferenceDraft(serviceValues);
    Check(!serviceDraft.IsDirty && ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == null,
        "default service editor can save without configuring inactive Custom API");
    serviceDraft.Set("translation_service", "Ollama");
    serviceDraft.Set("ollama_model", "draft-model");
    serviceDraft.Set("ollama_port", "");
    Check(ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == "Draft_ServicePortInvalid" &&
        serviceDraft["ollama_port"] == "", "incomplete port input survives in the draft");
    serviceDraft.Set("translation_service", "Google Translate");
    Check(serviceDraft["ollama_model"] == "draft-model" &&
        ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == "Draft_ServicePortInvalid",
        "switching providers preserves edits and cannot hide an invalid edited port");
    serviceDraft.Set("ollama_port", "65535");
    Check(ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == null, "maximum port is valid");
    serviceDraft.Set("ollama_port", "65536");
    Check(ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == "Draft_ServicePortInvalid", "out-of-range port is invalid");
    serviceDraft.Reset(serviceValues);
    serviceDraft.SetPendingKey("Gemini", "fake-service-draft-key");
    var serviceCandidate = serviceDraft.Candidate();
    Check(serviceDraft.IsDirty && serviceDraft.Keys("Gemini").Count == 0 &&
        serviceCandidate["gemini_api_key"] == "fake-service-draft-key" && serviceCandidate["gemini_api_keys"] == "fake-service-draft-key",
        "pending keys join the save candidate without mutating the editor or saved values");
    Check(serviceDraft.PendingKey("Gemini") == "fake-service-draft-key", "creating a failed-save candidate retains pending key input");
    serviceDraft.AddPendingKey("Gemini");
    serviceDraft.SetKeys("Gemini", []);
    Check(serviceDraft["gemini_api_key"] == "" && serviceDraft["gemini_api_keys"] == "", "removing all draft keys clears primary and rotation keys together");
    string promptKey = ServicePreferenceDraft.PromptKey("Gemini");
    serviceDraft.Set(promptKey, "replacement prompt");
    serviceDraft.Set("translation_service", "Gemini");
    Check(ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == null,
        "a valid prompt and provider form one candidate");
    serviceDraft.Set(promptKey, "injected <" + promptKey + "_end>");
    Check(ServicePreferenceDraft.Validate(serviceDraft.Candidate(), serviceDraft.Baseline) == "Draft_ServicePromptInvalid",
        "prompt text cannot escape its multiline field in the shared config");
    serviceDraft.Reset(serviceValues);
    Check(!serviceDraft.IsDirty && serviceDraft.PendingKey("Gemini") == "", "cancel discards provider edits, prompts and pending keys");
    var changedService = new Dictionary<string, string>(serviceValues) { ["gemini_model"] = "external-model" };
    serviceDraft.Set("translation_service", "Ollama");
    serviceDraft.Observe(changedService);
    Check(serviceDraft.HasConflict(changedService) && serviceDraft.Service == "Ollama", "external service changes never overwrite a dirty draft");
    Check(ConfigFileSecurity.IsProfilePrivate(promptKey), "new prompt overrides retain the legacy exclusion from game profiles");

    var captureSaved = new CapturePreferenceValues("Windows OCR", 0, true, true, false, false);
    var captureDraft = new CapturePreferenceDraft(captureSaved);
    captureDraft.SetAutoOcr(false); captureDraft.SetAutoTranslate(false); captureDraft.SetMultipleAreas(true);
    Check(captureDraft.IsDirty && captureDraft.Baseline == captureSaved && captureSaved.AutoOcr,
        "capture edits leave the saved choices unchanged");
    Check(captureDraft.Value.Validate([0, 1], false) == null, "valid Windows OCR capture needs no optional native assets");
    captureDraft.SetOcrMethod("OneOCR"); captureDraft.SetMangaMode(true);
    Check(captureDraft.Value.Validate([0, 1], false) == "Draft_CaptureOneOcrUnavailable", "unavailable OneOCR cannot be committed");
    Check(captureDraft.Value.Validate([0, 1], true) == null, "available OneOCR permits manga mode");
    captureDraft.SetOcrMethod("Windows OCR");
    Check(!captureDraft.Value.MangaMode && captureSaved.OcrMethod == "Windows OCR", "changing OCR clears only the draft's incompatible manga option");
    captureDraft.SetScreen(2);
    Check(captureDraft.Value.Validate([0, 1], true) == "Draft_CaptureScreenRequired", "a disconnected monitor never falls back silently");
    captureDraft.SetScreen(-1);
    Check(captureDraft.Value.Validate([0], true) == "Draft_CaptureScreenRequired", "topology invalidation requires an explicit monitor choice");
    var captureExternal = captureSaved with { AutoTranslate = false };
    captureDraft.Observe(captureExternal);
    Check(captureDraft.HasConflict(captureExternal) && captureDraft.Value.ScreenIndex == -1, "external capture changes preserve a dirty draft and mark conflict");
    captureDraft.Reset(captureExternal);
    Check(!captureDraft.IsDirty && captureDraft.Value == captureExternal, "cancel restores every capture field together");
    Check((captureSaved with { MangaMode = true }).Validate([0], true) == "Draft_CaptureMangaUnavailable", "incompatible legacy manga settings remain visibly invalid until corrected");

    var resultSaved = new Dictionary<string, string>(ResultPreferenceDraft.Defaults);
    var resultDraft = new ResultPreferenceDraft(resultSaved);
    Check(ResultPreferenceDraft.Validate(resultDraft.Candidate()) == null && !resultDraft.IsDirty,
        "default result appearance is valid and clean");
    resultDraft.Set("OverlayTextColor", " #aabbcc ");
    resultDraft.Set("chatbox_font_size", "18");
    Check(resultDraft.Candidate()["OverlayTextColor"] == "#FFAABBCC" && resultSaved["chatbox_font_size"] == "14" &&
        resultDraft["OverlayTextColor"] == " #aabbcc ", "result normalization preserves raw edits and the committed baseline");
    foreach (var invalid in new (string Key, string Value)[] {
        ("OverlayTextColor", "red"), ("chatbox_font_size", "NaN"), ("chatbox_font_size", "Infinity"),
        ("chatbox_font_size", "49"), ("chatbox_window_opacity", "0"), ("chatbox_background_opacity", "1.1"),
        ("language_font_size_min", "70"), ("language_font_size_max", "257"),
        ("auto_clear_chat_timeout", "1.5"), ("auto_clear_chat_timeout", "61"),
        ("chatbox_font_family", "  "), ("chatbox_font_family", "font|value"), ("show_icon_signal", "yes") })
    {
        var invalidResult = new ResultPreferenceDraft(resultSaved);
        invalidResult.Set(invalid.Key, invalid.Value);
        Check(ResultPreferenceDraft.Validate(invalidResult.Candidate()) != null,
            "invalid result value cannot be committed: " + invalid.Key + "=" + invalid.Value);
    }
    var resultExternal = new Dictionary<string, string>(resultSaved) { ["show_icon_signal"] = "false" };
    resultDraft.Observe(resultExternal);
    Check(resultDraft.HasConflict(resultExternal) && resultDraft["chatbox_font_size"] == "18",
        "external result changes preserve a dirty draft and mark conflict");
    resultDraft.Reset(resultExternal);
    Check(!resultDraft.IsDirty && resultDraft["show_icon_signal"] == "false", "result cancel reloads the complete saved state");
    resultDraft.UseDefaults();
    Check(resultDraft.IsDirty && resultExternal["show_icon_signal"] == "false", "default result choices remain unsaved draft edits");
    Check(ResultPreferenceDraft.ChatBackgroundOpacity(0) == 0 && ResultPreferenceDraft.ChatBackgroundOpacity(1) == 1 &&
        ResultPreferenceDraft.ChatBackgroundOpacity(0.5) > 0.5 && ResultPreferenceDraft.ChatBackgroundOpacity(0.5) < 1,
        "preview and ChatBox share the same transparent, intermediate and opaque mapping");

    var advancedValues = new Dictionary<string, string>(SectionPreferenceDraft.AdvancedDefaults);
    var sectionDraft = new SectionPreferenceDraft(advancedValues);
    Check(SectionPreferenceDraft.InvalidAdvancedField(sectionDraft.Values) == null && !sectionDraft.IsDirty,
        "advanced defaults form a valid clean draft");
    sectionDraft.Set("block_detection_scale", "unfinished");
    Check(SectionPreferenceDraft.InvalidAdvancedField(sectionDraft.Values) == "block_detection_scale" &&
        advancedValues["block_detection_scale"] == "5", "incomplete numeric edits stay isolated from saved preferences");
    foreach (var invalid in new (string Key, string Value)[] {
        ("block_detection_scale", "NaN"), ("line_spacing_factor", "Infinity"), ("textsimilar_threshold", "0.4"),
        ("block_detection_settle_time", "-1"), ("min_context_size", "1.5"), ("max_context_pieces", "1e3"),
        ("min_line_confidence", "1.01"), ("hdr_support", "yes"), ("game_info", "<game_info_end>") })
    {
        var badAdvanced = new SectionPreferenceDraft(advancedValues); badAdvanced.Set(invalid.Key, invalid.Value);
        Check(SectionPreferenceDraft.InvalidAdvancedField(badAdvanced.Values) == invalid.Key,
            "invalid advanced preference is rejected: " + invalid.Key + "=" + invalid.Value);
    }
    var changedAdvanced = new Dictionary<string, string>(advancedValues) { ["char_level"] = "true" };
    sectionDraft.Observe(changedAdvanced);
    Check(sectionDraft.HasConflict(changedAdvanced) && sectionDraft["block_detection_scale"] == "unfinished",
        "external advanced updates preserve unsaved input and report conflict");
    sectionDraft.Reset(changedAdvanced);
    Check(!sectionDraft.IsDirty && sectionDraft["char_level"] == "true", "advanced cancel restores the full saved snapshot");
    Check(ProfileDocument.ValidName("Oyun — bölüm 2") && ProfileDocument.ValidName("game.profile"),
        "profile names support readable Unicode and ordinary extensions");
    foreach (string name in new[] { "", "../game", "C:\\game", "game|value", "CON", "nul.txt", "LPT1", "game.", " game" })
        Check(!ProfileDocument.ValidName(name), "Windows-invalid profile name rejected on every platform: " + name);
    var importedProfile = ProfileDocument.Read("# sample\ntheme_mode|dark|\napi_key|private|\nblock_detection_scale|8|\n" +
        "<game_info_start>\nLine one\nLine two\n<game_info_end>\ntranslation_areas|old coordinates|\n",
        new HashSet<string> { "block_detection_scale", "game_info" });
    Check(importedProfile.Count == 2 && importedProfile["block_detection_scale"] == "8" &&
        importedProfile["game_info"] == "Line one\nLine two", "profile import reads approved fields and multiline context only");

    values["dark_mode"] = "true";
    Check(PreferenceCommit.TrySet(values, "theme_mode", "system", Persist, out error, "dark_mode") &&
        !values.ContainsKey("dark_mode"), "committing theme preference removes the legacy key");
    Check(!JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!.ContainsKey("dark_mode"),
        "legacy-key removal is part of the persisted replacement");
    foreach (string choice in new[] { "en", "tr", "system", "pt-BR" })
    {
        string normalized = PreferenceRules.NormalizeLanguagePreference(choice);
        Check(PreferenceCommit.TrySet(values, "language_interface", normalized, Persist, out _) &&
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!["language_interface"] == normalized,
            "reopening preserves the normalized explicit preference: " + choice);
    }
    PreferenceCommit.TrySet(values, "language_interface", "system", Persist, out _);
    Check(PreferenceRules.ResolveLanguage(values["language_interface"], ["tr-TR"], ["en", "tr"]) == "tr" &&
        PreferenceRules.ResolveLanguage(values["language_interface"], ["en-US"], ["en", "tr"]) == "en" &&
        values["language_interface"] == "system", "system resolution never replaces the saved system preference");

    string profile = ConfigFileSecurity.RewriteSecrets("language_interface|en|\ntheme_mode|light|\ndark_mode|false|\nsource_language|ja|\n", false);
    Check(!profile.Contains("language_interface") && !profile.Contains("theme_mode") && !profile.Contains("dark_mode") &&
        profile.Contains("source_language|ja|"), "game profiles cannot replace application language/theme preferences");
    Check(!ConfigFileSecurity.RewriteSecrets("<language_interface_start>en<language_interface_end>", false).Contains("language_interface"),
        "legacy multiline profile preferences are also excluded");

    string legacy = ConfigFileSecurity.RewriteSecrets("language_interface|tr|\ntheme_mode|light|\ngemini_api_key|test-secret|\n", false,
        preserveGeneralPreferences: true);
    Check(legacy.Contains("language_interface|tr|") && legacy.Contains("theme_mode|light|") && !legacy.Contains("test-secret"),
        "sanitizing the original legacy settings preserves interface preferences while removing credentials");

    string languages = Directory.CreateDirectory(Path.Combine(temporary, "Languages")).FullName;
    File.WriteAllText(Path.Combine(languages, "en.json"), """{"Greeting":"Hello","OnlyEnglish":"Fallback","Blank":"English value","NullValue":"English null fallback"}""");
    File.WriteAllText(Path.Combine(languages, "tr.json"), """{"Greeting":"Merhaba","Blank":"","NullValue":null,"TurkishOnly":"Türkçe"}""");
    var strings = new LocalizationManager(languages);
    Check(strings.CurrentLanguage == "en" && strings["Greeting"] == "Hello", "initial language and dictionary agree");
    var notifications = new List<string>();
    var observerError = new InvalidOperationException("Simulated observer failure");
    PropertyChangedEventHandler faulty = (_, e) => { if (e.PropertyName == nameof(LocalizationManager.Strings)) throw observerError; };
    strings.PropertyChanged += faulty;
    strings.PropertyChanged += (_, e) =>
    {
        notifications.Add(e.PropertyName!);
        if (strings.CurrentLanguage == "tr" && strings["Greeting"] != "Merhaba")
            throw new Exception("A notification observed a partially applied dictionary.");
    };
    strings.CurrentLanguage = "tr";
    Check(strings.CurrentLanguage == "tr" && strings["Greeting"] == "Merhaba", "language and strings are committed together before notification");
    Check(notifications.SequenceEqual(new[] { "CurrentLanguage", "Strings", "Item[]" }),
        "all binding notifications reach the next observer even after another observer throws");
    Check(App.Failures.Count == 1 && ReferenceEquals(App.Failures[0], observerError), "observer exceptions are recorded without reverting the language");
    strings.PropertyChanged -= faulty;
    Check(strings["OnlyEnglish"] == "Fallback" && strings["Blank"] == "English value" && strings["NullValue"] == "English null fallback",
        "missing, empty and null translations fall back to English");
    strings.CurrentLanguage = "en";
    Check(strings["TurkishOnly"] == "[TurkishOnly]", "switching languages does not retain a previous dictionary's extra keys");
    File.WriteAllText(Path.Combine(languages, "tr.json"), "broken JSON");
    strings.CurrentLanguage = "tr";
    Check(strings.CurrentLanguage == "en" && strings["Greeting"] == "Hello", "malformed resources report the actual English fallback language");
    File.WriteAllText(Path.Combine(languages, "tr.json"), """{"Greeting":"Merhaba"}""");
    strings.CurrentLanguage = "tr";
    Check(strings.CurrentLanguage == "tr" && strings["Greeting"] == "Merhaba", "the same requested language can recover after its resource is repaired");
    File.Delete(Path.Combine(languages, "tr.json"));
    strings.LoadLanguage("tr");
    Check(strings.CurrentLanguage == "en" && strings["Greeting"] == "Hello", "missing resources cannot leave an incorrect effective-language value");
    strings.LoadLanguage("../../outside");
    Check(strings.CurrentLanguage == "en", "language identifiers cannot escape the resource directory");
    Console.WriteLine($"{passed} preference persistence checks passed. Real ConfigManager/WPF integration requires Windows.");
}
finally { Directory.Delete(temporary, recursive: true); }
