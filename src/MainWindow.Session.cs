using System.Windows;
using System.Windows.Shapes;

namespace AlpWareStudio.Localay;

public partial class MainWindow
{
    // isStarted means processing is active; paused sessions keep their visible results.
    private bool _sessionPaused;
    private bool _sessionShuttingDown;
    private string _sessionPauseReason = "Flow_Paused";

    private bool IsSeparateResultShown => ChatBoxWindow.Instance is { IsVisible: true } window &&
        window.WindowState != WindowState.Minimized;

    private void SessionPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (isStarted) PauseWorkflowSession("Flow_Paused");
        else StartWorkflowSession();
    }

    private void SessionStop_Click(object sender, RoutedEventArgs e) => StopWorkflowSession();

    private void StartWorkflowSession()
    {
        if (isStarted || _sessionShuttingDown || !PrepareWorkflowStart()) return;
        bool resuming = _sessionPaused;
        if (!resuming) PipelineDiagnostics.BeginSession();
        if (resuming) Logic.Instance.PrepareScreenTranslationResume();
        else Logic.Instance.ResetHash();
        _sessionPaused = false;
        isStopOCR = false;
        _lastOcrStartTime = DateTime.MinValue;
        isStarted = true;
        UpdateCaptureRect();
        SetOCRCheckIsWanted(true);
        TranslationWarmUp.Start();
        UpdateSessionStatus();
        RefreshWorkflowSummary();
        if (!resuming)
        {
            ShowFastNotification(LocalizationManager.Instance["NotificationTitle_TranslationStarted"],
                LocalizationManager.Instance["NotificationMessage_TranslationStarted_Details"]);
            CoachMarkTour.Current?.OnTranslationStarted();
            OnboardingTips.OnTranslationStarted(hasSelectedTranslationArea);
        }
    }

    private void PauseWorkflowSession(string reason)
    {
        if (!isStarted) return;
        // Disable capture before canceling work: cancellation continuations must never re-arm OCR.
        isStarted = false;
        _sessionPaused = true;
        _sessionPauseReason = reason;
        isStopOCR = true;
        SetOCRCheckIsWanted(false);
        Logic.Instance.SuspendScreenTranslation();
        _ = PipelineDiagnostics.ExportAsync(() => AppPaths.LogDirectory);
        UpdateSessionStatus();
        RefreshWorkflowSummary();
    }

    private void StopWorkflowSession()
    {
        if (!isStarted && !_sessionPaused) return;
        isStarted = false;
        _sessionPaused = false;
        isStopOCR = true;
        SetOCRCheckIsWanted(false);
        Logic.Instance.ClearAllTextObjects();
        _ = PipelineDiagnostics.ExportAsync(() => AppPaths.LogDirectory);
        if (ChatBoxWindow.Instance != null && ConfigManager.Instance.IsAutoClearChatboxHistoryEnabled())
            ChatBoxWindow.Instance.chatHistoryText.Document.Blocks.Clear();
        UpdateSessionStatus();
        RefreshWorkflowSummary();
        ShowFastNotification(LocalizationManager.Instance["NotificationTitle_TranslationStopped"],
            LocalizationManager.Instance["NotificationMessage_TranslationStopped_Details"]);
    }

    private void UpdateSessionStatus()
    {
        StartStatusEllipse.SetResourceReference(Shape.FillProperty,
            isStarted ? "SuccessBrush" : _sessionPaused ? "WarningBrush" : "MutedBrush");
        StartStatusText.Text = LocalizationManager.Instance[isStarted ? "Btn_On" : _sessionPaused ? "Flow_Pause" : "Btn_Off"];
    }

    internal static void CapturePreferencesCommitted(CapturePreferenceValues before, CapturePreferenceValues edited, bool topologyChanged)
    {
        var main = _this;
        if (main == null || !main._initializeRuntime || main._sessionShuttingDown) return;
        main.PauseWorkflowSession("Draft_CaptureSessionPaused");
        if (main._sessionPaused) main._sessionPauseReason = "Draft_CaptureSessionPaused";
        Logic.Instance.ClearAllTextObjects();
        main.selectedOcrMethod = edited.OcrMethod;
        main.isAutoTranslateEnabled = edited.AutoTranslate;
        main.isStopOCR = true;
        main.SetOCRCheckIsWanted(false);
        main.SetOcrStatus(edited.OcrMethod);
        if (before.OcrMethod != edited.OcrMethod) main._workflowLanguagesConfirmed = false;
        if (topologyChanged || before.ScreenIndex != edited.ScreenIndex) main._workflowCaptureNeedsSelection = true;
        main._workflowCaptureConfirmed = false;
        main._workflowResultConfirmed = false;
        main._workflowStep = main._workflowLanguagesConfirmed ? 1 : 0;
        bool initializing = _isInitializing;
        _isInitializing = true;
        try { main.LoadScreenList(); }
        finally { _isInitializing = initializing; }
        MonitorWindow.ApplyCapturePreferencesIfCreated(edited);
        main.RefreshWorkflowSummary();
    }

    internal static void ResultPreferencesCommitted(System.Collections.Generic.IReadOnlyDictionary<string, string> before,
        System.Collections.Generic.IReadOnlyDictionary<string, string> edited)
    {
        var main = _this;
        if (main == null || !main._initializeRuntime || main._sessionShuttingDown) return;
        bool processingChanged = new[] { "AutoMergeOverlappingText", "leave_translation_onscreen", "IsAutoSetOverlayBackground" }
            .Any(key => before[key] != edited[key]);
        if (processingChanged)
        {
            main.PauseWorkflowSession("Draft_ResultSessionPaused");
            if (main._sessionPaused) main._sessionPauseReason = "Draft_ResultSessionPaused";
        }
        TextObject.ClearCache();
        bool preserveSamples = before["IsAutoSetOverlayBackground"] == "true" && edited["IsAutoSetOverlayBackground"] == "true";
        foreach (var text in Logic.Instance.GetTextObjects()) text.ApplySavedResultAppearance(preserveSamples);
        MonitorWindow.RefreshResultAppearanceIfCreated();
        ChatBoxWindow.Instance?.ApplyConfigurationStyling();
        if (edited["show_icon_signal"] != "true") ChatBoxWindow.Instance?.HideTranslationStatus();
        main.RefreshWorkflowSummary();
    }

    internal static void AdvancedPreferencesCommitted()
    {
        var main = _this;
        if (main == null || !main._initializeRuntime || main._sessionShuttingDown) return;
        main.PauseWorkflowSession("Draft_AdvancedSessionPaused");
        if (main._sessionPaused) main._sessionPauseReason = "Draft_AdvancedSessionPaused";
        main._clipboardPreferenceLifetime.Invalidate();
        BlockDetectionManager.RefreshSavedPreferencesIfCreated();
        MonitorWindow.RefreshAdvancedPreferencesIfCreated();
        main.RefreshWorkflowSummary();
    }

    internal static void ServicePreferencesCommitted()
    {
        var main = _this;
        if (main == null || !main._initializeRuntime || main._sessionShuttingDown) return;
        main.PauseWorkflowSession("Draft_ServiceSessionPaused");
        if (main._sessionPaused) main._sessionPauseReason = "Draft_ServiceSessionPaused";
        main._clipboardPreferenceLifetime.Invalidate();
        Logic.Instance.ClearAllTextObjects();
        main.RefreshWorkflowSummary();
    }

    // Output windows notify without creating MainWindow or a hidden output singleton.
    internal static void NotifyResultSurfaceChanged()
    {
        var main = _this;
        if (main == null || !main._initializeRuntime || main._sessionShuttingDown) return;
        if (main.Dispatcher.CheckAccess()) main.ResultSurfaceVisibilityChanged();
        else main.Dispatcher.Invoke(() => main.ResultSurfaceVisibilityChanged());
    }

    private void ResultSurfaceVisibilityChanged()
    {
        if (_sessionShuttingDown || !isStarted || IsTranslationResultVisible) return;
        PauseWorkflowSession("Flow_PausedNoOutput");
    }
}
