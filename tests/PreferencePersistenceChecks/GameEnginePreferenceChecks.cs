using AlpWareStudio.Localay;

internal static class GameEnginePreferenceChecks
{
    internal static void Run(Action<bool, string> Check)
    {
        var values = new Dictionary<string, string>(ServicePreferenceDraft.Defaults);
        foreach (var provider in ServicePreferenceDraft.KeyPrefixes)
        {
            values[provider.Value + "_api_key"] = "";
            values[provider.Value + "_api_keys"] = "";
        }
        foreach (string provider in ServicePreferenceDraft.Services.Where(ServicePreferenceDraft.UsesPrompt))
            values[ServicePreferenceDraft.PromptKey(provider)] = "Translate source_language to target_language.";
        Check(ServicePreferenceDraft.Defaults["ollama_model"] == HyMt2Profile.Model,
            "the settings editor and runtime share the game AI model default");
        var legacyGameValues = new Dictionary<string, string>(values) {
            ["ollama_model"] = "saved-old-model", ["google_translate_use_cloud_api"] = "true",
            ["google_translate_api_key"] = "fixture-retained-key", ["ollama_temperature"] = "unused-old-value",
            [ServicePreferenceDraft.PromptKey("Ollama")] = ""
        };
        var gameChoice = new ServicePreferenceDraft(legacyGameValues);
        gameChoice.SelectGameEngine(true);
        Check(gameChoice.IsDirty && gameChoice.Service == "Ollama" && gameChoice["ollama_model"] == HyMt2Profile.Model &&
            legacyGameValues["ollama_model"] == "saved-old-model" && gameChoice.Baseline["ollama_model"] == "saved-old-model",
            "selecting local AI stages the exact model without overwriting saved preferences");
        Check(ServicePreferenceDraft.Validate(gameChoice.Candidate(), gameChoice.Baseline) == null,
            "the fixed AI profile is not blocked by unused generic sampling or an unused empty prompt");
        gameChoice.SelectGameEngine(false);
        Check(gameChoice.Service == "Google Translate" && gameChoice["google_translate_use_cloud_api"] == "false" &&
            gameChoice["google_translate_auto_map_languages"] == "true" && gameChoice["google_translate_api_key"] == "fixture-retained-key",
            "the web preset stages keyless mode without deleting saved credentials");
        gameChoice.Reset(legacyGameValues);
        Check(!gameChoice.IsDirty && gameChoice["ollama_model"] == "saved-old-model" && gameChoice["google_translate_use_cloud_api"] == "true",
            "canceling engine selection restores the original model and provider mode");
    }
}
