using AlpWareStudio.Localay;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    passed++; Console.WriteLine("PASS: " + message);
}
Check(OcrLanguageSetup.Evaluate("en", ["tr-TR", "en-US"]).RecognizerTag == "en-US", "English OCR is independent of the Windows user's Turkish language");
Check(OcrLanguageSetup.Evaluate("en", ["en-GB"]).IsReady, "installed British English satisfies the generic English source");
Check(OcrLanguageSetup.Evaluate("en", ["en-GB", "en-US"]).RecognizerTag == "en-US", "the preferred exact recognizer wins deterministically");
Check(OcrLanguageSetup.Evaluate("en-US", ["en-GB"]).State == OcrLanguageState.Missing, "an explicitly regional source remains explicit");
Check(!OcrLanguageSetup.Evaluate("ja", ["en-US"]).IsReady, "an unrelated installed language is never used for OCR");
Check(OcrLanguageSetup.Evaluate("pt", ["pt-PT"]).IsReady, "Portuguese accepts an installed regional variant");
Check(OcrLanguageSetup.Evaluate("ch_sim", ["zh-Hans-CN"]).IsReady, "Simplified Chinese recognizes the WinRT script tag");
Check(OcrLanguageSetup.Evaluate("ch_tra", ["zh-Hant-TW"]).IsReady, "Traditional Chinese recognizes the WinRT script tag");
Check(!OcrLanguageSetup.Evaluate("ch_sim", ["zh-Hant-TW"]).IsReady && !OcrLanguageSetup.Evaluate("ch_tra", ["zh-Hans-CN"]).IsReady, "Chinese scripts never silently substitute for one another");
Check(OcrLanguageSetup.CapabilityTag("ch_sim") == "zh-CN" && OcrLanguageSetup.CapabilityTag("ch_tra") == "zh-TW", "Chinese capabilities use their DISM names rather than recognizer aliases");
Check(OcrLanguageSetup.Evaluate("tr", ["tr-TR"]).IsReady, "a Turkish source can use installed Turkish OCR");
Check(OcrLanguageSetup.Evaluate("en", [], querySucceeded: false).State == OcrLanguageState.Unavailable, "native OCR query failures are distinct from missing packages");
Check(OcrLanguageSetup.Evaluate("en", []).State == OcrLanguageState.Missing, "a successful empty recognizer query reports missing OCR");
Check(OcrLanguageSetup.Evaluate("unknown-source", ["en-US"]).State == OcrLanguageState.Unsupported, "unknown source codes do not guess a package");
Check(OcrLanguageSetup.Evaluate("en", ["en-US", "EN-us", " "]).InstalledTags.Count == 1, "installed recognizer tags are normalized for display");
foreach (string tag in new[] { "en-US", "en-GB", "zh-CN", "zh-TW", "ja-JP", "tr-TR" })
{
    string script = OcrCapabilityInstaller.BuildScript(tag);
    Check(script.Contains("Language.Basic~~~" + tag + "~*") && script.Contains("Language.OCR~~~" + tag + "~*"), "only matching Basic/OCR capabilities are queried: " + tag);
}
foreach (string unsafeTag in new[] { "en-US'; Start-Process evil; #", "*", "en-US\nexit 0", "Language.Basic~~~en-US~0.0.1.0", "en_US" })
{
    try { OcrCapabilityInstaller.BuildScript(unsafeTag); throw new Exception("unsafe tag accepted"); }
    catch (ArgumentException) { Check(true, "unrecognized or injected capability input is rejected"); }
}
var start = OcrCapabilityInstaller.CreateStartInfo(Path.GetTempPath(), "en-US");
Check(start.FileName == Path.Combine(Path.GetTempPath(), "WindowsPowerShell", "v1.0", "powershell.exe"), "installer uses a system-rooted executable instead of PATH lookup");
Check(start.UseShellExecute && start.Verb == "runas", "only the explicitly launched Windows feature installer requests UAC");
Check(start.ArgumentList.SequenceEqual(new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", OcrCapabilityInstaller.BuildScript("en-US") }), "the reviewed command is passed as one argument without a command shell");
try { OcrCapabilityInstaller.CreateStartInfo("relative", "en-US"); throw new Exception("relative system path accepted"); }
catch (ArgumentException) { Check(true, "relative executable roots are rejected"); }
Check(OcrCapabilityInstaller.FromExitCode(0) == OcrInstallOutcome.Completed, "only exit zero reports completed installation");
Check(OcrCapabilityInstaller.FromExitCode(10) == OcrInstallOutcome.RestartRequired && OcrCapabilityInstaller.FromExitCode(14) == OcrInstallOutcome.Pending, "restart-required and Install Pending remain distinct from success");
Check(OcrCapabilityInstaller.FromExitCode(11) == OcrInstallOutcome.Unsupported && OcrCapabilityInstaller.FromExitCode(12) == OcrInstallOutcome.Failed && OcrCapabilityInstaller.FromExitCode(99) == OcrInstallOutcome.Failed, "missing OS support and unexpected failures never report success");
Check(!OcrCapabilityInstaller.BuildScript("en-US", exitProcess: false).Contains("exit $localayResult") && OcrCapabilityInstaller.BuildScript("en-US").Contains("exit $localayResult"), "copied commands show their result without closing a user's PowerShell");
string? root = AppContext.BaseDirectory;
while (root != null && !File.Exists(Path.Combine(root, "Localay.csproj"))) root = Directory.GetParent(root)?.FullName;
if (root == null) throw new Exception("Repository root not found.");
var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "SettingsWindow.xaml"));
System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
var selector = xaml.Descendants(wpf + "ComboBox").Single(e => (string?)e.Attribute(x + "Name") == "sourceLanguageComboBox");
foreach (var item in selector.Elements(wpf + "ComboBoxItem"))
{
    string code = (string?)item.Attribute("Content") ?? "";
    Check(OcrLanguageSetup.CapabilityTag(code) != null, "offered source language has a Windows capability query mapping: " + code);
}
foreach (string language in new[] { "en", "tr" })
{
    using var resource = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "app", "Languages", language + ".json")));
    foreach (string key in Enum.GetNames<OcrLanguageState>().Select(state => "OcrSetup_" + state)
        .Concat(Enum.GetNames<OcrInstallOutcome>().Select(outcome => "OcrSetup_Result_" + outcome)))
        Check(resource.RootElement.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.GetString()),
            "dynamic OCR guide state is localized: " + language + ":" + key);
}
if (args.Length == 2 && args[0] == "--script-path")
    File.WriteAllText(args[1], OcrCapabilityInstaller.BuildScript("en-US", exitProcess: false));
Console.WriteLine($"{passed} OCR setup checks passed. No Windows features, processes or user preferences were changed.");
