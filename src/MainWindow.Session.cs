using System.Windows;
using System.Windows.Shapes;

namespace AlpWareStudio.Localay;

public partial class MainWindow
{
    // isStarted means processing is active; waiting and manual pause are separate states.
    private readonly GameSessionPolicy _gameSession = new();
    private bool _changingGameOverlay;
    private long _lastFallbackCaptureAt;
    internal static bool IsGameSessionActive => _this is { isStarted: true, _sessionShuttingDown: false };
    private bool _sessionPaused;
    private bool _sessionShuttingDown;
    private string _sessionPauseReason = "Flow_Paused";

    private bool IsSeparateResultShown => ChatBoxWindow.Instance is { IsVisible: true } window &&
        window.WindowState != WindowState.Minimized;

    private void SessionPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (_gameSession.IsArmed) PauseWorkflowSession("Flow_Paused");
        else StartWorkflowSession();
    }

    private void SessionStop_Click(object sender, RoutedEventArgs e) => StopWorkflowSession();

    private void StartWorkflowSession()
    {
        if (_gameSession.IsArmed || _sessionShuttingDown || !PrepareWorkflowStart()) return;
        bool resuming = _sessionPaused;
        if (!resuming) PipelineDiagnostics.BeginSession();
        _sessionPaused = false;
        _gameSession.Start();
        _captureTimer.Interval = TimeSpan.FromMilliseconds(33);
        isStarted = false;
        isStopOCR = true;
        SetOCRCheckIsWanted(false);
        Logic.Instance.ClearAllTextObjects();
        SetGameOverlayVisible(false);
        TranslationWarmUp.Start();
        UpdateGameSession();
        UpdateSessionStatus();
        RefreshWorkflowSummary();
        if (!resuming)
        {
            CoachMarkTour.Current?.OnTranslationStarted();
            OnboardingTips.OnTranslationStarted();
        }
    }

    private void PauseWorkflowSession(string reason)
    {
        if (!_gameSession.IsArmed && !isStarted) return;
        // Disable capture before canceling work: cancellation continuations must never re-arm OCR.
        isStarted = false;
        _gameSession.Pause();
        _sessionPaused = true;
        _sessionPauseReason = reason;
        isStopOCR = true;
        SetOCRCheckIsWanted(false);
        Logic.Instance.SuspendScreenTranslation();
        SetGameOverlayVisible(false);
        _ = PipelineDiagnostics.ExportAsync(() => AppPaths.LogDirectory);
        UpdateSessionStatus();
        RefreshWorkflowSummary();
    }

    private void StopWorkflowSession()
    {
        bool notify = isStarted || _sessionPaused || _gameSession.IsTracking;
        isStarted = false;
        _gameSession.Stop();
        _sessionPaused = false;
        isStopOCR = true;
        SetOCRCheckIsWanted(false);
        Logic.Instance.ClearAllTextObjects();
        SetGameOverlayVisible(false);
        _ = PipelineDiagnostics.ExportAsync(() => AppPaths.LogDirectory);
        if (ChatBoxWindow.Instance != null && ConfigManager.Instance.IsAutoClearChatboxHistoryEnabled())
            ChatBoxWindow.Instance.chatHistoryText.Document.Blocks.Clear();
        UpdateSessionStatus();
        RefreshWorkflowSummary();
        if (notify)
            ShowFastNotification(LocalizationManager.Instance["NotificationTitle_TranslationStopped"],
                LocalizationManager.Instance["NotificationMessage_TranslationStopped_Details"]);
    }

    // Called before capture, including while waiting or manually paused. Never rebind a lost HWND.
    private void UpdateGameSession()
    {
        if (_sessionShuttingDown || !_gameSession.IsTracking) return;
        var observation = GameWindowObserver.Read(_gameSession.Selected);
        if (_graphicsCaptureService?.IsTargetClosed == true) observation = observation with { Exists = false };
        var action = _gameSession.Observe(observation);
        if (action == GameSessionAction.Stop)
        {
            StopWindowCapture(windowLost: true);
            return;
        }
        if (action == GameSessionAction.Suspend)
        {
            isStarted = false;
            isStopOCR = true;
            SetOCRCheckIsWanted(false);
            Logic.Instance.SuspendScreenTranslation();
            SetGameOverlayVisible(false);
        }
        else if (action == GameSessionAction.Activate)
        {
            // Old OCR leases, text and cached capture pixels cannot cross a foreground transition.
            Logic.Instance.ResetHash();
            _graphicsCaptureService?.DiscardCachedFrames();
            _lastOcrStartTime = DateTime.MinValue;
            _lastFallbackCaptureAt = 0;
            isStopOCR = false;
            isStarted = true;
            UpdateCaptureRect();
            try { SetGameOverlayVisible(true); }
            catch (Exception)
            {
                PauseWorkflowSession("Flow_OutputFailed");
                return;
            }
            SetOCRCheckIsWanted(true);
        }
        if (action != GameSessionAction.None)
        {
            UpdateSessionStatus();
            RefreshWorkflowSummary();
        }
    }

    private void SetGameOverlayVisible(bool visible)
    {
        _changingGameOverlay = true;
        try
        {
            if (!visible) { MonitorWindow.HideForGameSessionIfCreated(); return; }
            var overlay = MonitorWindow.Instance;
            overlay.WindowState = WindowState.Normal;
            overlay.imageScrollViewer.Visibility = Visibility.Visible;
            overlay.ShowOverlay();
            if (!overlay.IsVisible) overlay.Show();
            UpdateMonitorWindowPosition();
            overlay.RefreshOverlays();
        }
        finally { _changingGameOverlay = false; }
    }

    private void UpdateSessionStatus()
    {
        StartStatusEllipse.SetResourceReference(Shape.FillProperty,
            isStarted ? "SuccessBrush" : _sessionPaused || _gameSession.IsArmed ? "WarningBrush" : "MutedBrush");
        StartStatusText.Text = LocalizationManager.Instance[isStarted ? "Btn_On" : _gameSession.IsArmed ? "Flow_WaitingForGame" : _sessionPaused ? "Flow_Pause" : "Btn_Off"];
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
        if (topologyChanged || before.ScreenIndex != edited.ScreenIndex) main._workflowCaptureNeedsSelection = true;
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
        if (_changingGameOverlay || _sessionShuttingDown || !isStarted || IsTranslationResultVisible) return;
        PauseWorkflowSession("Flow_PausedNoOutput");
    }
}
