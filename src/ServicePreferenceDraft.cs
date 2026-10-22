using System.Globalization;

namespace AlpWareStudio.Localay;

// No record-generated ToString: snapshots can contain credentials and prompts.
internal sealed class ServicePreferenceDraft
{
    internal static readonly string[] Services = ["Gemini", "Groq", "Ollama", "ChatGPT", "Mistral", "Google Translate", "Yandex", "LM Studio", "Microsoft", "Custom API", "Marian (local)"];
    internal static readonly Dictionary<string, string> Defaults = new()
    {
        ["translation_service"] = "Google Translate", ["gemini_model"] = "gemini-flash-lite-latest",
        ["groq_model"] = "moonshotai/kimi-k2-instruct-0905", ["mistral_model"] = "open-mistral-nemo",
        ["chatgpt_model"] = "gpt-3.5-turbo", ["custom_api_url"] = "",
        ["custom_api_model"] = "", ["ollama_url"] = "http://localhost", ["ollama_port"] = "11434",
        ["ollama_model"] = "llama3", ["ollama_temperature"] = "0.1", ["ollama_top_p"] = "0.9", ["ollama_top_k"] = "40",
        ["lm_studio_url"] = "http://localhost", ["lm_studio_port"] = "1234", ["lm_studio_model"] = "google/gemma-3-4b",
        ["google_translate_use_cloud_api"] = "false", ["google_translate_auto_map_languages"] = "true",
        ["google_free_fallback_service"] = "Microsoft", ["microsoft_legacy_signature_mode"] = "true"
    };
    internal static readonly Dictionary<string, string> KeyPrefixes = new()
    {
        ["Gemini"] = "gemini", ["Groq"] = "groq", ["Mistral"] = "mistral", ["ChatGPT"] = "chatgpt",
        ["Google Translate"] = "google_translate", ["Microsoft"] = "microsoft", ["Custom API"] = "custom_api"
    };
    internal static string PromptKey(string service) => "service_" + service.ToLowerInvariant().Replace(' ', '_') + "_prompt_template";
    internal static bool UsesPrompt(string service) => service is not ("Google Translate" or "Yandex" or "Microsoft" or "Marian (local)");
    private Dictionary<string, string> _baseline;
    private Dictionary<string, string> _values;
    private readonly Dictionary<string, string> _pendingKeys = new();
    internal ServicePreferenceDraft(IReadOnlyDictionary<string, string> saved)
    {
        _baseline = new(saved);
        _values = new(saved);
    }
    internal string this[string key] => _values.GetValueOrDefault(key, "");
    internal string Service => this["translation_service"];
    internal bool IsDirty => _pendingKeys.Values.Any(v => v.Length > 0) || !Same(_baseline, _values);
    internal IReadOnlyDictionary<string, string> Baseline => new Dictionary<string, string>(_baseline);
    internal static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? value) && value == pair.Value);
    internal bool HasConflict(IReadOnlyDictionary<string, string> current) => IsDirty && !Same(_baseline, current);
    internal void Set(string key, string value)
    {
        if (!_values.ContainsKey(key)) throw new ArgumentException("Unknown service preference field.", nameof(key));
        _values[key] = value;
    }
    internal void SetPendingKey(string service, string text) => _pendingKeys[service] = text.Trim();
    internal string PendingKey(string service) => _pendingKeys.GetValueOrDefault(service, "");
    internal List<string> Keys(string service) => KeyPrefixes.TryGetValue(service, out string? prefix)
        ? this[prefix + "_api_keys"].Split("+++", StringSplitOptions.RemoveEmptyEntries).ToList() : [];
    internal void SetKeys(string service, IEnumerable<string> keys)
    {
        if (!KeyPrefixes.TryGetValue(service, out string? prefix)) return;
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        Set(prefix + "_api_keys", string.Join("+++", list));
        Set(prefix + "_api_key", list.FirstOrDefault() ?? "");
    }
    internal void AddPendingKey(string service)
    {
        string key = PendingKey(service);
        if (key.Length == 0 || !KeyPrefixes.ContainsKey(service)) return;
        var keys = Keys(service);
        if (!keys.Contains(key)) keys.Add(key);
        SetKeys(service, keys);
        Set(KeyPrefixes[service] + "_api_key", key);
        _pendingKeys.Remove(service);
    }
    internal IReadOnlyDictionary<string, string> Candidate()
    {
        // Work on a copy: a failed save must retain all editor input, including pending keys.
        var copy = new ServicePreferenceDraft(_values);
        foreach (var key in _pendingKeys) { copy.SetPendingKey(key.Key, key.Value); copy.AddPendingKey(key.Key); }
        foreach (string key in Defaults.Keys.Where(key => key.EndsWith("_url") || key.EndsWith("_model") || key.EndsWith("_port")))
            copy._values[key] = copy._values[key].Trim();
        foreach (string key in new[] { "ollama_url", "lm_studio_url" })
            if (Uri.TryCreate(copy._values[key], UriKind.Absolute, out var url) && url.IsDefaultPort && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0)
                copy._values[key] = url.GetLeftPart(UriPartial.Authority);
        foreach (string key in copy._values.Keys.Where(key => key.EndsWith("_template")).ToArray())
            copy._values[key] = copy._values[key].Trim();
        return new Dictionary<string, string>(copy._values);
    }
    internal void Observe(IReadOnlyDictionary<string, string> current) { if (!IsDirty) Reset(current); }
    internal void Reset(IReadOnlyDictionary<string, string> current)
    {
        _baseline = new(current); _values = new(current); _pendingKeys.Clear();
    }
    internal static string? Validate(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string>? baseline = null)
    {
        string Get(string key) => values.GetValueOrDefault(key, "");
        bool Edited(string prefix) => baseline == null || values.Any(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && baseline.GetValueOrDefault(p.Key, "") != p.Value);
        string service = Get("translation_service");
        if (!Services.Contains(service)) return "Draft_ServiceInvalid";
        // Validate all edited providers, including providers hidden after a selection change.
        foreach (var prefix in new[] { "ollama", "lm_studio" })
        {
            if (service != (prefix == "ollama" ? "Ollama" : "LM Studio") && !Edited(prefix + "_")) continue;
            if (!Uri.TryCreate(Get(prefix + "_url").Trim(), UriKind.Absolute, out var url) ||
                url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.AbsolutePath != "/" ||
                url.Query.Length != 0 || url.Fragment.Length != 0 || url.UserInfo.Length != 0)
                return "Draft_ServiceLocalUrlInvalid";
            if (!int.TryParse(Get(prefix + "_port"), NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                return "Draft_ServicePortInvalid";
        }
        if ((service == "Custom API" || Edited("custom_api_url")) &&
            (!Uri.TryCreate(Get("custom_api_url").Trim(), UriKind.Absolute, out var custom) || custom.Scheme is not ("http" or "https") || custom.UserInfo.Length != 0))
            return "Draft_ServiceUrlInvalid";
        if ((service == "Ollama" || Edited("ollama_")) && (!double.TryParse(Get("ollama_temperature"), NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature) || !double.IsFinite(temperature) || temperature < 0 || temperature > 2 ||
            !double.TryParse(Get("ollama_top_p"), NumberStyles.Float, CultureInfo.InvariantCulture, out double topP) || !double.IsFinite(topP) || topP < 0 || topP > 1 ||
            !int.TryParse(Get("ollama_top_k"), NumberStyles.None, CultureInfo.InvariantCulture, out int topK) || topK < 0))
            return "Draft_ServiceSamplingInvalid";
        string? model = service switch
        {
            "Gemini" => "gemini_model", "Groq" => "groq_model", "Mistral" => "mistral_model", "ChatGPT" => "chatgpt_model",
            "Ollama" => "ollama_model", "LM Studio" => "lm_studio_model", "Custom API" => "custom_api_model", _ => null
        };
        if (model != null && string.IsNullOrWhiteSpace(Get(model))) return "Draft_ServiceModelRequired";
        // The legacy parser uses explicit multiline tags; reject injected terminators.
        foreach (var field in values)
        {
            if (field.Key.EndsWith("_template") && (field.Value.Contains("<" + field.Key + "_end>") || ((Edited(field.Key) || field.Key == PromptKey(service)) && string.IsNullOrWhiteSpace(field.Value))))
                return "Draft_ServicePromptInvalid";
            if (field.Key.EndsWith("_api_key") && field.Value.Contains("+++")) return "Draft_ServiceValueInvalid";
            if (!field.Key.EndsWith("_template") && field.Value.IndexOfAny(new[] { '\r', '\n', '|' }) >= 0)
                return "Draft_ServiceValueInvalid";
        }
        return null;
    }
}
