namespace AlpWareStudio.Localay;

// Shared product identity and destinations. Values are independent of WPF and saved preferences.
public static class AppIdentity
{
    public const string Product = "Localay";
    public const string Publisher = "AlpWare Studio";
    public const string ContactEmail = "contact@alpwarestudio.com";
    public const string ProjectUrl = "https://github.com/mahmutaunal/Localay";
    public const string ReleasesUrl = ProjectUrl + "/releases";
    public const string IssuesUrl = ProjectUrl + "/issues";
    public const string ContactUrl = "mailto:" + ContactEmail;
    public const string UpstreamUrl = "https://github.com/thanhkeke97/RSTGameTranslation";

    // Call with the resolved UI language, including when the saved preference is "system".
    public static string ProductsUrl(string? effectiveLanguage) =>
        string.Equals(effectiveLanguage, "tr", StringComparison.OrdinalIgnoreCase)
            ? "https://www.alpwarestudio.com/tr/products/"
            : "https://www.alpwarestudio.com/en/products/";
}
