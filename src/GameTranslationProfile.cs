namespace AlpWareStudio.Localay;

// One selected engine per session. Local AI uses the exact translation profile;
// the existing keyless web path remains available without cross-provider fallback.
internal static class GameTranslationProfile
{
    internal const string Service = "Google Translate";
    internal const string Model = "nmt";
    internal const string CloudIdentity = "google-cloud-basic-v2:nmt:text-v1";

    internal static bool IsConfigured(string service, bool cloudEnabled, string? localModel = null) =>
        (service == Service && !cloudEnabled) || HyMt2Profile.IsSelected(service, localModel);

    internal static bool AllowsDispatch(bool cloudEnabled, bool managedBlocks) => !cloudEnabled || !managedBlocks;
    internal static bool AllowsFallback(bool managedBlocks) => !managedBlocks;
    internal static int BatchSize(string service, string? localModel) => HyMt2Profile.IsSelected(service, localModel) ? 1 : 15;

    internal static string CacheIdentity(bool cloudEnabled, bool mapLanguages) => cloudEnabled
        ? CloudIdentity : "google-web:map=" + mapLanguages;

    // The Google adapters do not consume conversation history. Changing history must not
    // evict menu/HUD translations that the same engine would translate identically.
    internal static object MeaningContext(string service, object previousContext, string? localModel = null) =>
        service == Service || HyMt2Profile.IsSelected(service, localModel) ? System.Array.Empty<string>() : previousContext;
}
