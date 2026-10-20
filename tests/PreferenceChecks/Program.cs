using System.Globalization;
using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
string[] languages = ["en", "tr", "ja", "ko", "vi", "ru", "zh_cn", "pt_br"];
foreach (var (tag, expected) in new[] { ("tr-TR", "tr"), ("en-GB", "en"), ("ja-JP", "ja"), ("ko-KR", "ko"),
    ("vi-VN", "vi"), ("ru-RU", "ru"), ("pt-PT", "pt_br"), ("pt_BR", "pt_br"), ("zh-CN", "zh_cn"), ("zh-Hans", "zh_cn") })
    Check(PreferenceRules.ResolveLanguage("system", [tag], languages) == expected, "system display language " + tag);
Check(PreferenceRules.ResolveLanguage("en", ["tr-TR"], languages) == "en", "explicit English overrides Turkish Windows");
Check(PreferenceRules.ResolveLanguage("tr", ["en-US"], languages) == "tr", "explicit Turkish overrides English Windows");
Check(PreferenceRules.ResolveLanguage("system", ["de-DE", "ja-JP"], languages) == "ja", "preferred list selects first supported display language");
Check(PreferenceRules.ResolveLanguage("system", ["de-DE"], languages) == "en", "unsupported system language falls back to English");
Check(PreferenceRules.ResolveLanguage("system", [], languages) == "en", "empty OS language list falls back to English");
Check(PreferenceRules.ResolveLanguage("system", ["tr-TR"], ["en", "ja"]) == "en", "language without a shipped resource is not advertised as supported");
Check(PreferenceRules.ResolveLanguage("system", ["zh-TW"], languages) == "en", "traditional Chinese is not silently rendered in simplified Chinese");
Check(PreferenceRules.ResolveLanguage("system", ["zh-Hant-CN"], languages) == "en", "Chinese script takes priority over region");
Check(PreferenceRules.ResolveLanguage("system", ["zh-Hans-HK"], languages) == "zh_cn", "explicit simplified Chinese script is supported");
Check(PreferenceRules.NormalizeLanguagePreference(null) == "system", "missing language preference follows Windows");
Check(PreferenceRules.NormalizeLanguagePreference(" SYSTEM ") == "system", "system preference normalization");
Check(PreferenceRules.NormalizeLanguagePreference("pt_br") == "pt_br", "legacy Portuguese preference stays explicit");
Check(PreferenceRules.NormalizeLanguagePreference("zh_cn") == "zh_cn", "legacy Chinese preference stays explicit");
Check(PreferenceRules.NormalizeLanguagePreference("../outside") == "en", "malformed language value cannot become a resource path");
Check(PreferenceRules.ResolveThemeMode(null, "true") == "dark", "legacy dark choice migrates to explicit dark");
Check(PreferenceRules.ResolveThemeMode(null, "false") == "light", "legacy light choice migrates to explicit light");
Check(PreferenceRules.ResolveThemeMode(null, null) == "system", "missing theme preference follows Windows");
Check(PreferenceRules.ResolveThemeMode("light", "true") == "light", "new explicit choice overrides stale legacy flag");
Check(PreferenceRules.ResolveThemeMode("system", "true") == "system", "system choice overrides stale legacy flag");
Check(PreferenceRules.ResolveThemeMode("invalid") == "system", "invalid theme preference safely follows Windows");
Check(PreferenceRules.ResolveDark("system", true) && !PreferenceRules.ResolveDark("system", false), "system mode reacts to both OS theme states");
Check(!PreferenceRules.ResolveDark("light", true), "manual light survives a system dark change");
Check(PreferenceRules.ResolveDark("dark", false), "manual dark survives a system light change");
Check(PreferenceRules.NextThemeMode("system") == "light" && PreferenceRules.NextThemeMode("light") == "dark" &&
      PreferenceRules.NextThemeMode("dark") == "system", "header theme cycle can return to system mode");
CultureInfo oldUi = CultureInfo.CurrentUICulture;
try
{
    CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    Check(PreferenceRules.ResolveLanguage("system", ["ja-JP"], languages) == "ja", "invariant formatting does not erase captured system language");
}
finally { CultureInfo.CurrentUICulture = oldUi; }
if (OperatingSystem.IsWindows())
{
    Check(WindowsPreferenceReader.TryReadLanguages(out string[] preferred) && preferred.Length > 0 &&
          preferred.All(name => !string.IsNullOrWhiteSpace(CultureInfo.GetCultureInfo(name).Name)),
          "Windows production reader returns valid display languages through the native API");
    Console.WriteLine("Windows application dark preference: " + WindowsPreferenceReader.ReadDark());
}
Console.WriteLine($"{passed} preference checks passed. Windows event, registry and WPF integration require Windows.");
