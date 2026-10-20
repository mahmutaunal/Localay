using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using AlpWareStudio.Localay;

var root = new DirectoryInfo(args.FirstOrDefault() ?? Environment.CurrentDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "Localay.csproj"))) root = root.Parent;
if (root == null) throw new Exception("Run from the repository or pass its path.");
string repo = root.FullName;
int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}
Check(AppIdentity.Publisher == "AlpWare Studio", "publisher");
Check(AppIdentity.ContactEmail == "contact@alpwarestudio.com" && AppIdentity.ContactUrl == "mailto:contact@alpwarestudio.com", "contact destination");
Check(AppIdentity.ProjectUrl == "https://github.com/mahmutaunal/Localay", "project destination");
Check(AppIdentity.ReleasesUrl == AppIdentity.ProjectUrl + "/releases", "releases destination");
Check(AppIdentity.IssuesUrl == AppIdentity.ProjectUrl + "/issues", "issues destination");
string[] supported = ["en", "tr", "vi", "zh_cn", "ja", "ko", "ru", "pt_br"];
foreach (string? language in supported.Cast<string?>().Append(null).Append("system").Append("unknown").Append("TR"))
{
    string expected = language is "tr" or "TR" ? "https://www.alpwarestudio.com/tr/products/" : "https://www.alpwarestudio.com/en/products/";
    Check(AppIdentity.ProductsUrl(language) == expected, "products routing: " + (language ?? "null"));
}
foreach (var (system, expected) in new[] { ("tr-TR", "/tr/products/"), ("en-US", "/en/products/"), ("de-DE", "/en/products/"), ("ja-JP", "/en/products/") })
{
    string effective = PreferenceRules.ResolveLanguage("system", [system], supported);
    Check(AppIdentity.ProductsUrl(effective).EndsWith(expected, StringComparison.Ordinal), "system preference routing: " + system);
}
var project = XDocument.Load(Path.Combine(repo, "Localay.csproj"));
Check(project.Descendants("Company").Single().Value == AppIdentity.Publisher && project.Descendants("Authors").Single().Value == AppIdentity.Publisher, "assembly publisher and author metadata");
Check(project.Descendants("Product").Single().Value == AppIdentity.Product, "assembly product metadata");
string source = File.ReadAllText(Path.Combine(repo, "src", "AboutWindow.xaml.cs"));
Check(source.Contains("AppIdentity.ProductsUrl(ApplicationPreferences.EffectiveLanguage)"), "product link reads current effective language at click time");
Check(!source.Contains("GetLanguageInterface()"), "saved system token is not used as display language");
string main = File.ReadAllText(Path.Combine(repo, "src", "MainWindow.xaml.cs"));
Check(!main.Contains("DiscordButton_Click") && !main.Contains("DonateButton_Click") && !main.Contains("buymeacoffee.com") && !main.Contains("discord.gg"), "retired support handlers removed");
string splash = File.ReadAllText(Path.Combine(repo, "src", "SplashManager.cs"));
Check(splash.Contains("AppIdentity.Product") && splash.Contains("AppIdentity.Publisher") && !splash.Contains("Thanh Pham"), "splash uses current identity");
string notices = File.ReadAllText(Path.Combine(repo, "THIRD_PARTY_NOTICES.md"));
Check(notices.Contains("Thanh Pham") && notices.Contains(AppIdentity.UpstreamUrl), "original developer and source retained in notices");
string license = File.ReadAllText(Path.Combine(repo, "LICENSE.md"));
Check(license.Contains("GNU GENERAL PUBLIC LICENSE") && license.Contains("Version 3, 29 June 2007"), "original GPL license retained");
var mainXaml = XDocument.Load(Path.Combine(repo, "src", "MainWindow.xaml"));
Check(mainXaml.Descendants().Any(e => e.Name.LocalName == "MenuItem" && e.Attribute("Click")?.Value == "AboutButton_Click"), "About reachable from main menu");
var aboutXaml = XDocument.Load(Path.Combine(repo, "src", "AboutWindow.xaml"));
Check(aboutXaml.Descendants().Where(e => e.Name.LocalName == "Expander").All(e => e.Attribute("IsExpanded")?.Value == "False"), "legal documents collapsed initially");

// Inspect the actual Windows assembly without loading WPF or executing it on macOS.
string assemblyPath = args.Length > 1 ? args[1] : Path.Combine(repo, "bin", "Release", "Localay.dll");
using var stream = File.OpenRead(assemblyPath);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var resources = metadata.ManifestResources.Select(h => metadata.GetManifestResource(h)).ToDictionary(r => metadata.GetString(r.Name));
foreach (string file in new[] { "LICENSE.md", "THIRD_PARTY_NOTICES.md" })
{
    string name = "Localay.Licenses." + file;
    Check(resources.TryGetValue(name, out var resource) && resource.Implementation.IsNil, "embedded document exists: " + file);
    var block = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
    int offset = checked((int)resource.Offset);
    int length = BinaryPrimitives.ReadInt32LittleEndian(block.GetContent(offset, 4).AsSpan());
    byte[] actual = block.GetContent(offset + 4, length).ToArray();
    Check(actual.SequenceEqual(File.ReadAllBytes(Path.Combine(repo, file))), "embedded document matches original bytes: " + file);
}
Console.WriteLine($"{passed} brand, language routing and bundled-license checks passed. Browser/mail interaction and WPF require Windows.");
