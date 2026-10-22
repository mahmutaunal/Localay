namespace AlpWareStudio.Localay;

// Shared by the UI and portable checks. Readiness needs a game, not wizard confirmations
// or saved screen-area/ChatBox geometry. This policy performs no capture or OS calls.
internal static class GameWorkflowReadiness
{
    internal static string? StartIssue(bool hasGame, bool needsSelection, string? languageIssue,
        bool overlaySafe, bool selectionBusy, bool serviceConfigured)
    {
        if (!hasGame || needsSelection) return "Flow_GameRequired";
        if (languageIssue != null) return languageIssue;
        if (!overlaySafe) return "Flow_OverlayUnavailable";
        if (selectionBusy) return "Flow_FinishSelection";
        if (!serviceConfigured) return "Flow_ServiceRequired";
        return null;
    }
}
