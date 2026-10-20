using System.Xml;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = new DirectoryInfo(args.FirstOrDefault() ?? Environment.CurrentDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "Localay.csproj"))) root = root.Parent;
if (root == null) throw new Exception("Run from the repository or pass its path.");
string repo = root.FullName;
int checks = 0;
var failures = new List<string>();
void Check(bool condition, string message) { checks++; if (!condition) failures.Add(message); }
Dictionary<string, string> Read(string language)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "app", "Languages", language + ".json")));
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var entry in doc.RootElement.EnumerateObject())
    {
        Check(result.TryAdd(entry.Name, entry.Value.GetString()!), $"Duplicate {language} key: {entry.Name}");
        Check(!string.IsNullOrWhiteSpace(result[entry.Name]), $"Empty {language} value: {entry.Name}");
    }
    return result;
}
var en = Read("en");
var tr = Read("tr");
Check(en.Keys.ToHashSet().SetEquals(tr.Keys), "English and Turkish keys differ.");
foreach (var (key, value) in en)
{
    var a = CompositeFormat(value);
    var b = CompositeFormat(tr[key]);
    Check(a.SequenceEqual(b), $"Format placeholders differ: {key}");
    int count = a.DefaultIfEmpty(-1).Max() + 1;
    foreach (var culture in new[] { "tr-TR", "en-US" })
    {
        var values = Enumerable.Range(0, count).Select(i => (object)(i + 1)).ToArray();
        _ = string.Format(CultureInfo.GetCultureInfo(culture), value, values);
        _ = string.Format(CultureInfo.GetCultureInfo(culture), tr[key], values);
    }
    Check(value != tr[key] || key == "Lbl_Model", $"Untranslated Turkish value: {key}");
}
Check(tr["Main_Start"] == "Çeviriyi başlat" && tr["Language_en"] == "İngilizce", "Turkish smoke labels changed.");

// Data identifiers are deliberately not translated. Keep this list explicit: new English UI copy must use a resource.
var identifiers = new HashSet<string>(StringComparer.Ordinal)
{
    "X", "Localay", "Windows OCR", "OneOCR", "Gemini", "Groq", "Ollama", "ChatGPT", "Mistral", "Google Translate", "Yandex", "LM Studio", "Microsoft",
    "gemini-flash-lite-latest", "gemini-flash-latest", "gemini-3.5-flash-lite", "gemini-3.8-flash", "gemma-3-12b-it", "gemma-3-27b-it",
    "qwen/qwen3-32b", "openai/gpt-oss-20b", "openai/gpt-oss-120b", "moonshotai/kimi-k2-instruct", "moonshotai/kimi-k2-instruct-0905",
    "meta-llama/llama-prompt-guard-2-86m", "meta-llama/llama-prompt-guard-2-22m", "meta-llama/llama-guard-4-12b",
    "mistral-medium", "open-mistral-nemo", "mistral-small-2503", "GPT-4.1", "GPT-5.1", "GPT-4.1 Nano", "GPT-4.1 Mini", "GPT-5-Mini", "llama3", "http://localhost"
};
var displayedIdentifiers = new Dictionary<string, string> { ["Custom API"] = "Service_CustomApi", ["Free Web Service"] = "Google_FreeService", ["Cloud API (paid)"] = "Google_CloudService" };
var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
foreach (string path in Directory.GetFiles(Path.Combine(repo, "src"), "*.xaml"))
{
    string source = File.ReadAllText(path);
    foreach (Match match in Regex.Matches(source, @"Strings\[([^\]]+)\]"))
        Check(en.ContainsKey(match.Groups[1].Value), $"Missing binding key in {Path.GetFileName(path)}: {match.Groups[1].Value}");
    var document = XDocument.Parse(source, LoadOptions.SetLineInfo);
    foreach (var element in document.Descendants())
    {
        foreach (var attribute in element.Attributes().Where(a => new[] { "Text", "Content", "Header", "Title", "ToolTip", "ToolTipText" }.Contains(a.Name.LocalName)))
        {
            string value = attribute.Value;
            if (value.StartsWith('{') || !Regex.IsMatch(value, "[A-Za-z]")) continue;
            bool code = element.Name.LocalName == "ComboBoxItem" && en.ContainsKey("Language_" + value);
            bool template = displayedIdentifiers.TryGetValue(value, out var key) && element.Descendants().Attributes().Any(a => a.Value.Contains($"Strings[{key}]"));
            Check(code || template || identifiers.Contains(value) || Regex.IsMatch(value, @"^ALT \+ ([A-Z]|[1-5])$") || Regex.IsMatch(value, @"^#[0-9A-Fa-f]{8}$"),
                $"Hardcoded XAML UI text: {Path.GetFileName(path)}:{((IXmlLineInfo)attribute).LineNumber}: {value}");
        }
        if (element.Name.Namespace != xamlNamespace || !new[] { "TextBlock", "Run", "Button", "MenuItem", "Label", "ComboBoxItem", "CheckBox" }.Contains(element.Name.LocalName)) continue;
        foreach (var text in element.Nodes().OfType<XText>().Select(t => t.Value.Trim()).Where(t => Regex.IsMatch(t, "[A-Za-z]")))
            Check(identifiers.Contains(text) || Uri.TryCreate(text, UriKind.Absolute, out _), $"Hardcoded XAML text: {Path.GetFileName(path)}: {text}");
    }
    // Every offered language must have an explicit localized name, including Chinese's app-specific codes.
    foreach (var combo in document.Descendants().Where(e => e.Name.LocalName == "ComboBox" && e.Attributes().Any(a => a.Name.LocalName == "Name" && (a.Value == "sourceLanguageComboBox" || a.Value == "targetLanguageComboBox"))))
        foreach (var item in combo.Elements().Where(e => e.Name.LocalName == "ComboBoxItem"))
            Check(en.ContainsKey("Language_" + item.Attribute("Content")!.Value), "Missing language name.");
}

// Parse C# rather than matching comments/logs. Check UI entry points and UI object initializers.
var uiCalls = new[] { "MessageBox.Show", "ShowFastNotification", "UpdateStatus", "SetStatus", "ShowModelStatusWindow", "UpdateModelStatus" };
var uiProperties = new[] { "Text", "Content", "Header", "Title", "ToolTip" };
var uiTypes = new[] { "TextBlock", "Button", "MenuItem", "Window", "ComboBoxItem", "TextBox" };
foreach (string path in Directory.GetFiles(Path.Combine(repo, "src"), "*.cs"))
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
    var syntax = tree.GetRoot();
    foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        string method = invocation.Expression.ToString();
        // Validate literal resource references, including format argument counts.
        if (method is "UiText.Get" or "UiText.Format" or "UiText.Bind")
        {
            int index = method == "UiText.Bind" ? 2 : 0;
            if (invocation.ArgumentList.Arguments.Count > index && invocation.ArgumentList.Arguments[index].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                string key = literal.Token.ValueText;
                Check(en.ContainsKey(key), $"Missing code resource: {key}");
                if (method == "UiText.Format") Check(invocation.ArgumentList.Arguments.Count - 1 > CompositeFormat(en[key]).DefaultIfEmpty(-1).Max(), $"Too few format arguments: {key}");
            }
        }
    }
    foreach (var access in syntax.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Where(a => a.Expression.ToString().Contains("LocalizationManager") || a.Expression.ToString() == "strings"))
        foreach (var literal in access.ArgumentList.Arguments.Select(a => a.Expression).OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
            Check(en.ContainsKey(literal.Token.ValueText), $"Missing indexed resource: {literal.Token.ValueText}");
    foreach (var node in syntax.DescendantNodes())
    {
        bool call = node is InvocationExpressionSyntax i && uiCalls.Any(x => i.Expression.ToString().EndsWith(x, StringComparison.Ordinal));
        bool assignment = node is AssignmentExpressionSyntax a && uiProperties.Any(x => a.Left.ToString().EndsWith("." + x, StringComparison.Ordinal));
        bool initializer = node is AssignmentExpressionSyntax init && uiProperties.Contains(init.Left.ToString()) && init.Parent?.Parent is ObjectCreationExpressionSyntax creation && uiTypes.Contains(creation.Type.ToString());
        if (!call && !assignment && !initializer) continue;
        foreach (var literal in node.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            string text = literal.Token.ValueText;
            if (!Regex.IsMatch(text, "[A-Za-z]")) continue;
            // Switch patterns/config keys/format specifiers are inputs, not displayed text.
            bool data = literal.Parent is ConstantPatternSyntax || literal.Ancestors().OfType<InvocationExpressionSyntax>().TakeWhile(n => n != node).Any(n =>
                n.Expression.ToString().EndsWith(".ToString") || n.Expression.ToString().Contains("config.GetHotKey") || n.Expression.ToString().Contains("ConfigManager.Instance.Get"));
            Check(data || en.ContainsKey(text) || identifiers.Contains(text) || text is "F2" or "application/json" or "crash_log.txt" or "Localay.Licenses.LICENSE.md" or "Localay.Licenses.THIRD_PARTY_NOTICES.md",
                $"Hardcoded C# UI text: {Path.GetFileName(path)}:{tree.GetLineSpan(literal.Span).StartLinePosition.Line + 1}: {text}");
        }
        foreach (var text in node.DescendantNodes().OfType<InterpolatedStringTextSyntax>())
            Check(!Regex.IsMatch(text.TextToken.ValueText, "[A-Za-z]"), $"Hardcoded interpolated UI text: {Path.GetFileName(path)}:{tree.GetLineSpan(text.Span).StartLinePosition.Line + 1}: {text}");
    }
}
if (failures.Count != 0) throw new Exception(string.Join(Environment.NewLine, failures));
Console.WriteLine($"PASS: {checks} localization assertions; {en.Count} English/Turkish keys, placeholders, UI references, language names and source text policy.");

static int[] CompositeFormat(string value)
{
    // Parse first so stray braces/invalid specifiers fail, even if the regex would overlook them.
    _ = System.Text.CompositeFormat.Parse(value);
    return Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,}:])").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();
}
