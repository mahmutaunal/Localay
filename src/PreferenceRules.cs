using System.Globalization;

namespace AlpWareStudio.Localay;

// Platform-independent preference resolution. Stored choices never contain a resolved system value.
internal static class PreferenceRules
{
    internal const string System = "system";
    internal static readonly string[] KnownLanguages = ["en", "vi", "zh_cn", "ja", "ko", "ru", "pt_br", "tr"];

    internal static string NormalizeLanguagePreference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals(System, StringComparison.OrdinalIgnoreCase)) return System;
        string? key = LanguageKey(value);
        return key != null && KnownLanguages.Contains(key) ? key : "en";
    }

    internal static string ResolveLanguage(string? preference, IEnumerable<string> systemLanguages, IEnumerable<string> supported)
    {
        var available = new HashSet<string>(supported, StringComparer.OrdinalIgnoreCase);
        string choice = NormalizeLanguagePreference(preference);
        if (choice != System) return available.Contains(choice) ? choice : "en";
        foreach (string language in systemLanguages)
        {
            string? key = LanguageKey(language);
            if (key != null && available.Contains(key)) return key;
        }
        return "en";
    }

    private static string? LanguageKey(string value)
    {
        string tag = value.Trim().Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            string language = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (language == "pt") return "pt_br";
            if (language == "zh")
            {
                string lower = tag.ToLowerInvariant();
                bool traditional = lower.Contains("hant") || lower is "zh-tw" or "zh-hk" or "zh-mo";
                return traditional ? "zh_tw" : "zh_cn";
            }
            return language;
        }
        catch (CultureNotFoundException) { return null; }
    }

    internal static string ResolveThemeMode(string? mode, string? legacyDark = null)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return bool.TryParse(legacyDark, out bool dark) ? dark ? "dark" : "light" : System;
        return mode.Trim().ToLowerInvariant() switch { "light" => "light", "dark" => "dark", _ => System };
    }

    internal static bool ResolveDark(string mode, bool systemDark) => ResolveThemeMode(mode) switch
    {
        "dark" => true, "light" => false, _ => systemDark
    };

    internal static string NextThemeMode(string mode) => ResolveThemeMode(mode) switch
    {
        "light" => "dark", "dark" => System, _ => "light"
    };
}
