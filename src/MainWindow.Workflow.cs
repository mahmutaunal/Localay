using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class MainWindow
{
    private bool _workflowCaptureNeedsSelection;
    private bool _refreshingWorkflow;
    private bool HasWorkflowGame => isCapturingWindow && capturedWindowHandle != IntPtr.Zero;

    private void InitializeWorkflow() => RefreshWorkflowSummary();

    private string? WorkflowLanguageIssue()
    {
        if (sourceLanguageComboBox.SelectedItem is not ComboBoxItem source ||
            targetLanguageComboBox.SelectedItem is not ComboBoxItem target)
            return "Flow_SelectLanguages";
        string sourceCode = source.Content?.ToString() ?? "";
        if (sourceCode == target.Content?.ToString()) return "Flow_DifferentLanguages";
        var config = ConfigManager.Instance;
        if (HyMt2Profile.IsSelected(config.GetCurrentTranslationService(), config.GetOllamaModel()) &&
            !HyMt2Profile.SupportsPair(sourceCode, target.Content?.ToString()))
            return "Flow_LocalLanguageUnsupported";
        if (!_initializeRuntime) return null;
        string method = ConfigManager.Instance.GetOcrMethod();
        if (method == "Windows OCR")
        {
            try
            {
                var status = WindowsOCRManager.Instance.GetLanguageStatus(sourceCode);
                if (!status.IsReady) return "Flow_LanguageRequired";
            }
            catch { return "Flow_LanguageUnavailable"; }
        }
        else if (method != "OneOCR" || !NativeOcrAssets.IsAvailable) return "Flow_RecognitionUnavailable";
        return null;
    }

    private bool WorkflowServiceConfigured()
    {
        var config = ConfigManager.Instance;
        return GameTranslationProfile.IsConfigured(config.GetCurrentTranslationService(),
            config.GetGoogleTranslateUseCloudApi(), config.GetOllamaModel());
    }

    private string? WorkflowStartIssue() => GameWorkflowReadiness.StartIssue(
        HasWorkflowGame, _workflowCaptureNeedsSelection, WorkflowLanguageIssue(),
        IsOverlayKeptOutOfCapture,
        isSelectingTranslationArea || isSelectingChatBoxArea || isSelectingExcludeRegion,
        WorkflowServiceConfigured());

    private void RefreshWorkflowControls(bool hasGame)
    {
        if (_refreshingWorkflow || toggleButton == null) return;
        _refreshingWorkflow = true;
        try
        {
            var text = LocalizationManager.Instance;
            var config = ConfigManager.Instance;
            string engine = HyMt2Profile.IsSelected(config.GetCurrentTranslationService(), config.GetOllamaModel())
                ? "Local" : WorkflowServiceConfigured() ? "Web" : "Unsupported";
            UiText.Bind(EngineSummaryText, TextBlock.TextProperty, engine == "Local" ? "Flow_EngineLocal" : engine == "Web" ? "Flow_EngineWeb" : "Flow_EngineUnsupported");
            UiText.Bind(EngineHintText, TextBlock.TextProperty, engine == "Local" ? "Flow_EngineLocalHint" : engine == "Web" ? "Flow_EngineWebHint" : "Flow_EngineUnsupportedHint");
            // Keep a saved unsupported selection visible so it can be corrected, but
            // do not offer unsupported languages as new choices for the local profile.
            foreach (var combo in new[] { sourceLanguageComboBox, targetLanguageComboBox })
                foreach (var item in combo.Items.OfType<ComboBoxItem>())
                {
                    item.IsEnabled = engine != "Local" || HyMt2Profile.LanguageName(item.Content?.ToString()) != null;
                    ToolTipService.SetShowOnDisabled(item, true);
                    if (item.IsEnabled) item.ClearValue(ToolTipProperty);
                    else UiText.Bind(item, ToolTipProperty, "Flow_LocalLanguageUnsupported");
                }
            string? languageIssue = WorkflowLanguageIssue();
            sourceLanguageComboBox.IsEnabled = !_gameSession.IsArmed;
            targetLanguageComboBox.IsEnabled = !_gameSession.IsArmed;
            swapLanguagesButton.IsEnabled = !_gameSession.IsArmed;
            LanguageReadinessText.Text = text[languageIssue ?? "Flow_LanguageReady"];
            ocrSetupButton.Visibility = languageIssue is "Flow_LanguageRequired" or "Flow_LanguageUnavailable" ? Visibility.Visible : Visibility.Collapsed;
            selectWindowButton.IsEnabled = !_gameSession.IsArmed;
            UiText.Bind(selectWindowButton, ContentControl.ContentProperty, hasGame ? "Flow_ChangeGame" : "Flow_SelectGame");
            UiText.Bind(WorkflowHeading, TextBlock.TextProperty, _gameSession.IsTracking || _sessionPaused ? "Flow_Session" : "Flow_Prepare");
            string? issue = WorkflowStartIssue();
            WorkflowStatusText.Text = isStarted ? text["Flow_Running"]
                : _gameSession.State == GameSessionState.Waiting ? text["Flow_WaitingForGame"]
                : _sessionPaused ? text[_sessionPauseReason] + (issue == null ? "" : " " + text[issue])
                : text[issue ?? "Flow_GameReady"];
            UiText.Bind(toggleButton, ContentControl.ContentProperty,
                _gameSession.IsArmed ? "Flow_Pause" : _sessionPaused ? "Flow_Resume" : "Main_Start");
            stopSessionButton.Visibility = _gameSession.IsTracking || _sessionPaused ? Visibility.Visible : Visibility.Collapsed;
            toggleButton.IsEnabled = _gameSession.IsArmed || issue == null;
            trayStartStopItem.IsEnabled = toggleButton.IsEnabled;
            UiText.Bind(trayStartStopItem, HeaderedItemsControl.HeaderProperty, _gameSession.IsArmed ? "Main_Stop" : _sessionPaused ? "Flow_Resume" : "Main_Start");
        }
        finally { _refreshingWorkflow = false; }
    }

    private void InvalidateWorkflowLanguages()
    {
        if (_isInitializing) return;
        if (_gameSession.IsArmed) PauseWorkflowSession("Flow_Paused");
        RefreshWorkflowSummary();
    }

    private bool CanChooseWorkflowCapture()
    {
        if (!_gameSession.IsArmed && !isSelectingTranslationArea && !isSelectingChatBoxArea && !isSelectingExcludeRegion) return true;
        RefreshWorkflowSummary();
        return false;
    }

    private bool PrepareWorkflowStart()
    {
        string? issue = WorkflowStartIssue();
        if (issue != null) { WorkflowStatusText.Text = UiText.Get(issue); return false; }
        // Output is opened only after the selected game becomes foreground.
        return true;
    }

    private void WorkflowOcrSetup_Click(object sender, RoutedEventArgs e)
    {
        OcrLanguageSetupWindow.ShowFor(this, ConfigManager.Instance.GetSourceLanguage());
        RefreshWorkflowSummary();
    }

    internal void OpenDiagnostics() => ToggleConsoleWindow();
    private void EngineSettings_Click(object sender, RoutedEventArgs e)
    {
        try { SettingsWindow.Instance.ShowTranslationFor(this); }
        catch (Exception ex) { SettingsWindowError.Show(this, ex); }
    }
    internal void OpenAbout() => AboutButton_Click(this, new RoutedEventArgs());
    internal void ResetFromSettings() => ResetSettingsButton_Click(this, new RoutedEventArgs());
}
