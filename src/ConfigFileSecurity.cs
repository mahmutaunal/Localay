using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

internal static class ConfigFileSecurity
{
    private const string Prefix = "dpapi:v1:";
    internal static bool IsSecret(string key) =>
        key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("signature_key", StringComparison.OrdinalIgnoreCase);

    private static bool IsInterfacePreference(string key) =>
        key.Equals("language_interface", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("theme_mode", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("dark_mode", StringComparison.OrdinalIgnoreCase);

    internal static bool IsProfilePrivate(string key) => IsSecret(key) || IsInterfacePreference(key) ||
        (key.StartsWith("service_", StringComparison.Ordinal) && key.EndsWith("_prompt_template", StringComparison.Ordinal)) ||
        key.Equals("clipboard_privacy_consent_v1", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("clipboard_auto_translate_enabled", StringComparison.OrdinalIgnoreCase);

    internal static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential protection requires Windows.");
        byte[] plain = Encoding.UTF8.GetBytes(value);
        try
        {
            return Prefix + Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static string Unprotect(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value; // Legacy plaintext import.
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential protection requires Windows.");
        byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]),
            null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    // Used before importing legacy files, never copy plaintext keys or include them in profiles.
    internal static string RewriteSecrets(string document, bool includeSecrets, bool preserveInterfacePreferences = false)
    {
        bool Rewrite(string key) => includeSecrets ? IsSecret(key) :
            IsProfilePrivate(key) && !(preserveInterfacePreferences && IsInterfacePreference(key));
        document = Regex.Replace(document, @"<(\w+)_start>(.*?)<\1_end>", m =>
            !Rewrite(m.Groups[1].Value) ? m.Value : includeSecrets
                ? $"{m.Groups[1].Value}|{Protect(m.Groups[2].Value)}|" : "", RegexOptions.Singleline);
        return Regex.Replace(document, @"^([ \t]*)(\w+)\|([^\r\n]*)\r?$", m =>
        {
            if (!Rewrite(m.Groups[2].Value)) return m.Value;
            if (!includeSecrets) return "";
            string value = m.Groups[3].Value.TrimEnd('\r');
            if (value.EndsWith('|')) value = value[..^1];
            return $"{m.Groups[1].Value}{m.Groups[2].Value}|{Protect(value)}|" + (m.Value.EndsWith('\r') ? "\r" : "");
        }, RegexOptions.Multiline);
    }
}
