using System.IO;
using AlpWareStudio.Localay;
using System.Security.Cryptography;
using System.Text;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
string temporary = Path.Combine(Path.GetTempPath(), "LocalaySecurityChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    const string secret = "test-secret-do-not-use-123456789";
    const string document = "# test\r\ngemini_api_key|" + secret + "|\r\ngemini_api_keys|" + secret + "+++another-test-key|\r\nsource_language|en|\r\n<llm_prompt_multi_start>hello\nworld<llm_prompt_multi_end>\r\n";
    Check(ConfigFileSecurity.IsSecret("microsoft_legacy_signature_key"), "signature keys are credentials");
    string profile = ConfigFileSecurity.RewriteSecrets(document, false);
    Check(!profile.Contains(secret) && !profile.Contains("api_key"), "profile export removes single and multiple keys");
    Check(profile.Contains("source_language|en|") && profile.Contains("hello\nworld"), "profile sanitizing preserves settings and multiline prompts");
    Check(!ConfigFileSecurity.RewriteSecrets("<gemini_api_key_start>" + secret + "<gemini_api_key_end>", false).Contains(secret), "multiline credentials removed from imports");
    Check(!ConfigFileSecurity.RewriteSecrets("clipboard_privacy_consent_v1|true|\nclipboard_auto_translate_enabled|true|\n", false).Contains("true"), "profiles cannot grant clipboard consent or enable monitoring");
    CredentialRedactor.Register(secret);
    string redacted = CredentialRedactor.Redact(secret + " ?key=unknown-key&x=1 Bearer unknown-token");
    Check(!redacted.Contains(secret) && !redacted.Contains("unknown-key") && !redacted.Contains("unknown-token"), "known keys, query keys and bearer tokens redacted");
    using var output = new StringWriter();
    var writer = new RedactingTextWriter(output);
    writer.Write(secret[..8]);
    writer.WriteLine(secret[8..]);
    Check(!output.ToString().Contains(secret) && output.ToString().Contains("[REDACTED]"), "fragmented console writes redact credentials");
    string atomic = Path.Combine(temporary, "atomic.txt");
    AppPaths.WriteAtomic(atomic, "first");
    AppPaths.WriteAtomic(atomic, "second");
    Check(File.ReadAllText(atomic) == "second" && Directory.GetFiles(temporary, "*.tmp").Length == 0, "atomic replacement leaves no temporary files");
    Check(AppRestart.RunHelper([]) == null, "normal startup does not enter restart helper");
    Check(AppRestart.RunHelper(["--restart-after", "invalid", "invalid"]) == 1, "malformed restart request rejected");
    Check(AppRestart.RunHelper(["--restart-after", Environment.ProcessId.ToString(), "1"]) == 1, "restart cannot wait on itself");

    string source = Directory.CreateDirectory(Path.Combine(temporary, "legacy")).FullName;
    string destination = Path.Combine(temporary, "migrated");
    Directory.CreateDirectory(Path.Combine(source, "Profiles"));
    File.WriteAllText(Path.Combine(source, "Profiles", "game.txt"), document);
    File.WriteAllText(Path.Combine(source, "crash_log.txt"), "private log");
    File.WriteAllText(Path.Combine(source, "unknown.exe"), "not executable");
    File.WriteAllText(Path.Combine(source, "chatgpt_config.txt"), "<llm_prompt_multi_start>prompt<llm_prompt_multi_end>");
    AppPaths.MigrateLegacySettings(source, destination);
    Check(!File.Exists(Path.Combine(destination, "unknown.exe")) && !File.Exists(Path.Combine(destination, "crash_log.txt")), "migration excludes executable files and logs");
    Check(!File.ReadAllText(Path.Combine(destination, "Profiles", "game.txt")).Contains(secret), "migration sanitizes legacy profile keys");
    File.WriteAllText(Path.Combine(destination, "chatgpt_config.txt"), "new settings");
    AppPaths.MigrateLegacySettings(source, destination);
    Check(File.ReadAllText(Path.Combine(destination, "chatgpt_config.txt")) == "new settings", "migration never overwrites existing user settings");

    if (OperatingSystem.IsWindows())
    {
        string protectedValue = ConfigFileSecurity.Protect(secret);
        Check(!protectedValue.Contains(secret) && ConfigFileSecurity.Unprotect(protectedValue) == secret, "DPAPI roundtrip under current user");
        Check(ConfigFileSecurity.Protect(protectedValue) == protectedValue, "encrypted values are not encrypted twice");
        string encrypted = ConfigFileSecurity.RewriteSecrets(document, true);
        Check(!encrypted.Contains(secret) && encrypted.Contains("dpapi:v1:"), "legacy main config encrypts credentials before migration");
        bool rejected = false;
        try { ConfigFileSecurity.Unprotect("dpapi:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("invalid"))); }
        catch (CryptographicException) { rejected = true; }
        Check(rejected, "tampered DPAPI data rejected");
        File.WriteAllText(Path.Combine(source, "config.txt"), document);
        AppPaths.MigrateLegacySettings(source, destination);
        Check(!File.ReadAllText(Path.Combine(destination, "config.txt")).Contains(secret), "migrated config never stores plaintext keys");
        Check(!File.ReadAllText(Path.Combine(source, "config.txt")).Contains(secret), "legacy main config sanitized after successful migration");
    }
#if WINDOWS
    string settings = Path.Combine(temporary, "settings");
    var config = new ConfigManager(settings, source);
    config.SaveApiKeysList("Gemini", ["first-key-for-test", "second-key-for-test"]);
    string configText = File.ReadAllText(Path.Combine(settings, "config.txt"));
    Check(!configText.Contains("first-key-for-test") && configText.Contains("dpapi:v1:"), "configuration writes only encrypted credentials");
    var reopened = new ConfigManager(settings, source);
    Check(reopened.GetGeminiApiKey() == "first-key-for-test", "key-list update persists the selected first key");
    reopened.SaveTranslationAreas([], "shared");
    Check(!File.ReadAllText(Path.Combine(settings, "Profiles", "shared.txt")).Contains("api_key"), "actual profile export contains no credentials");
    string injected = Path.Combine(settings, "Profiles", "injected.txt");
    File.WriteAllText(injected, "gemini_api_key|injected-key|\nsource_language|ja|\n");
    reopened.GetTranslationAreas(injected);
    Check(reopened.GetGeminiApiKey() == "first-key-for-test", "profile import cannot replace account credentials");
    string? backup = reopened.ResetToDefaults();
    Check(backup != null && !File.ReadAllText(backup).Contains("first-key-for-test"), "reset backup retains encrypted credentials");
    var reset = new ConfigManager(settings, source);
    Check(reset.GetGeminiApiKey() == "first-key-for-test", "reset preserves usable credentials");
    Check(!reset.IsClipboardAutoTranslateEnabled(), "clipboard monitoring disabled without privacy consent");
    Check(reset.GetOcrMethod() == "Windows OCR", "default package uses Windows OCR");
#endif
    Console.WriteLine($"{passed} security checks passed. " + (OperatingSystem.IsWindows() ? "Windows checks included." : "DPAPI and WPF integration checks require Windows."));
    return 0;
}
finally { Directory.Delete(temporary, true); }
