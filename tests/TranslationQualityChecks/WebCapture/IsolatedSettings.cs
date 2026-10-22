namespace AlpWareStudio.Localay;

// Harness-only settings: never read user credentials or enable billing. The provider,
// batching, response parser and HTTP client are production sources, not copies.
internal sealed class ConfigManager
{
    internal static ConfigManager Instance { get; } = new();
    internal string GetGoogleTranslateApiKey() => "";
    internal bool GetGoogleTranslateUseCloudApi() => false;
    internal bool GetGoogleTranslateAutoMapLanguages() => true;
    internal string GetGoogleFreeFallbackService() => "Microsoft"; // Must still be ignored for ID batches.
    internal string GetSourceLanguage() => "en";
    internal string GetTargetLanguage() => "tr";
}
internal static class TranslationServiceFactory
{
    internal static int Attempts;
    internal static ITranslationService CreateService(string name)
    {
        Attempts++;
        throw new InvalidOperationException("A second provider is forbidden in this experiment.");
    }
}
internal static class TranslationWarmUp
{
    internal static Task OpenConnectionsAsync(HttpClient client, string service, params string[] urls) =>
        throw new InvalidOperationException("Warm-up is outside this experiment.");
}
