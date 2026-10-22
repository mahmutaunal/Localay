using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class MainWindow
{
    private bool _workflowLanguagesConfirmed;
    private bool _workflowCaptureConfirmed;
    private bool _workflowResultConfirmed;
    private bool _workflowCaptureNeedsSelection;
    private bool _refreshingWorkflow;
    private int _workflowStep;

    private bool WantsOverlay => overlayChoice.IsChecked == true || bothChoice.IsChecked == true;
    private bool WantsSeparate => separateChoice.IsChecked == true || bothChoice.IsChecked == true;

    private void InitializeWorkflow()
    {
        RefreshWorkflowControls(hasSelectedTranslationArea || isCapturingWindow);
    }

    private string? WorkflowLanguageIssue()
    {
        if (sourceLanguageComboBox.SelectedItem is not ComboBoxItem source ||
            targetLanguageComboBox.SelectedItem is not ComboBoxItem target)
            return "Flow_FirstLanguages";
        string sourceCode = source.Content?.ToString() ?? "";
        if (sourceCode == target.Content?.ToString()) return "Flow_DifferentLanguages";
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

    private bool HasWorkflowPosition => isChatBoxVisible ||
        (ConfigManager.Instance.GetChatboxPositionWidth() > 0 && ConfigManager.Instance.GetChatboxPositionHeight() > 0 &&
         (ConfigManager.Instance.GetBoolValue("chatbox_position_set") ||
          ConfigManager.Instance.GetChatboxPositionLeft() != 0 || ConfigManager.Instance.GetChatboxPositionTop() != 0));

    private string? WorkflowResultIssue()
    {
        if (WantsOverlay && !IsOverlayKeptOutOfCapture) return "Flow_OverlayUnavailable";
        if (WantsSeparate && !HasWorkflowPosition) return "Flow_PositionRequired";
        return null;
    }

    private bool WorkflowServiceConfigured()
    {
        var config = ConfigManager.Instance;
        string service = config.GetCurrentTranslationService();
        return service switch
        {
            "Google Translate" => !config.GetGoogleTranslateUseCloudApi() || !string.IsNullOrWhiteSpace(config.GetGoogleTranslateApiKey()),
            "Ollama" => !string.IsNullOrWhiteSpace(config.GetOllamaModel()),
            "LM Studio" => !string.IsNullOrWhiteSpace(config.GetLMStudioModel()),
            "Custom API" => Uri.TryCreate(config.GetCustomApiUrl(), UriKind.Absolute, out var endpoint) &&
                endpoint.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(config.GetCustomApiModel()),
            "Gemini" or "Groq" or "Mistral" or "ChatGPT" or "Microsoft" => config.GetApiKeysList(service).Any(key => !string.IsNullOrWhiteSpace(key)),
            _ => true
        };
    }

    private string? WorkflowStartIssue()
    {
        if (!_workflowLanguagesConfirmed) return "Flow_FirstLanguages";
        string? language = WorkflowLanguageIssue();
        if (language != null) return language;
        if (_workflowCaptureNeedsSelection) return "Draft_CaptureSelectSource";
        if (!_workflowCaptureConfirmed || (!hasSelectedTranslationArea && !isCapturingWindow)) return "Flow_FirstCapture";
        if (!_workflowResultConfirmed) return "Flow_FirstResult";
        string? result = WorkflowResultIssue();
        if (result != null) return result;
        if (isSelectingTranslationArea || isSelectingChatBoxArea || isSelectingExcludeRegion) return "Flow_FinishSelection";
        if (!WorkflowServiceConfigured()) return "Flow_ServiceRequired";
        return null;
    }

    private void RefreshWorkflowControls(bool hasCapture)
    {
        if (_refreshingWorkflow || confirmLanguagesButton == null || toggleButton == null) return;
        _refreshingWorkflow = true;
        try
        {
            var text = LocalizationManager.Instance;
            string? languageIssue = WorkflowLanguageIssue();
            if (languageIssue != null) { _workflowLanguagesConfirmed = false; _workflowStep = 0; }
            if (!hasCapture) _workflowCaptureConfirmed = false;
            if (WorkflowResultIssue() != null) _workflowResultConfirmed = false;
            bool showLanguages = !isStarted && _workflowStep == 0;
            bool showCapture = !isStarted && _workflowStep == 1 && _workflowLanguagesConfirmed;
            bool showResult = !isStarted && _workflowStep == 2 && _workflowCaptureConfirmed;
            LanguagesStepContent.Visibility = showLanguages ? Visibility.Visible : Visibility.Collapsed;
            CaptureStepContent.Visibility = showCapture ? Visibility.Visible : Visibility.Collapsed;
            ResultStepContent.Visibility = showResult ? Visibility.Visible : Visibility.Collapsed;
            editLanguagesButton.Visibility = _workflowLanguagesConfirmed && !isStarted && !showLanguages ? Visibility.Visible : Visibility.Collapsed;
            editCaptureButton.Visibility = _workflowCaptureConfirmed && !isStarted && !showCapture ? Visibility.Visible : Visibility.Collapsed;
            editResultButton.Visibility = _workflowResultConfirmed && !isStarted && !showResult ? Visibility.Visible : Visibility.Collapsed;
            LanguageSummary.Visibility = showLanguages ? Visibility.Collapsed : Visibility.Visible;
            LanguageSummary.Text = _workflowLanguagesConfirmed
                ? LanguageDisplay.GetShortName(ConfigManager.Instance.GetSourceLanguage()) + " → " + LanguageDisplay.GetShortName(ConfigManager.Instance.GetTargetLanguage())
                : text["Flow_FirstLanguages"];
            LanguageReadinessText.Text = text[languageIssue ?? "Flow_LanguageReady"];
            confirmLanguagesButton.IsEnabled = languageIssue == null;
            ocrSetupButton.Visibility = languageIssue is "Flow_LanguageRequired" or "Flow_LanguageUnavailable" ? Visibility.Visible : Visibility.Collapsed;
            selectWindowButton.IsEnabled = _workflowLanguagesConfirmed && !isStarted;
            selectAreaButton.IsEnabled = _workflowLanguagesConfirmed && !isStarted;
            confirmCaptureButton.IsEnabled = _workflowLanguagesConfirmed && hasCapture && !_workflowCaptureNeedsSelection;
            overlayChoice.IsEnabled = IsOverlayKeptOutOfCapture;
            bothChoice.IsEnabled = IsOverlayKeptOutOfCapture;
            placeChatBoxButton.Visibility = WantsSeparate ? Visibility.Visible : Visibility.Collapsed;
            string? resultIssue = WorkflowResultIssue();
            ResultReadinessText.Text = text[resultIssue ?? "Flow_ResultReady"];
            confirmResultButton.IsEnabled = resultIssue == null;
            ResultSummary.Text = _workflowCaptureConfirmed
                ? text[WantsOverlay && WantsSeparate ? "Flow_Both" : WantsSeparate ? "Btn_ChatBox" : "Btn_Overlay"]
                : text["Flow_FirstCapture"];
            SessionViewControls.Visibility = isStarted || _sessionPaused ? Visibility.Visible : Visibility.Collapsed;
            UiText.Bind(WorkflowHeading, TextBlock.TextProperty, isStarted || _sessionPaused ? "Flow_Session" : "Flow_Prepare");
            string? issue = WorkflowStartIssue();
            WorkflowStatusText.Text = isStarted ? text["Flow_Running"]
                : _sessionPaused ? text[_sessionPauseReason] + (issue == null ? "" : " " + text[issue])
                : text[issue ?? "Flow_Ready"];
            UiText.Bind(toggleButton, ContentControl.ContentProperty,
                isStarted ? "Flow_Pause" : _sessionPaused ? "Flow_Resume" : "Main_Start");
            stopSessionButton.Visibility = isStarted || _sessionPaused ? Visibility.Visible : Visibility.Collapsed;
            toggleButton.IsEnabled = isStarted || issue == null;
            trayStartStopItem.IsEnabled = toggleButton.IsEnabled;
            UiText.Bind(trayStartStopItem, HeaderedItemsControl.HeaderProperty, isStarted ? "Main_Stop" : _sessionPaused ? "Flow_Resume" : "Main_Start");
        }
        finally { _refreshingWorkflow = false; }
    }

    private void ConfirmLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (WorkflowLanguageIssue() != null) return;
        _workflowLanguagesConfirmed = true;
        _workflowStep = 1;
        RefreshWorkflowSummary();
    }

    private void ConfirmCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowCaptureNeedsSelection || !_workflowLanguagesConfirmed || (!hasSelectedTranslationArea && !isCapturingWindow)) return;
        _workflowCaptureConfirmed = true;
        _workflowStep = 2;
        RefreshWorkflowSummary();
    }

    private void ConfirmResult_Click(object sender, RoutedEventArgs e)
    {
        if (!_workflowCaptureConfirmed || WorkflowResultIssue() != null) return;
        _workflowResultConfirmed = true;
        _workflowStep = 3;
        RefreshWorkflowSummary();
    }

    private void EditLanguages_Click(object sender, RoutedEventArgs e) { _workflowStep = 0; _workflowLanguagesConfirmed = false; RefreshWorkflowSummary(); }
    private void EditCapture_Click(object sender, RoutedEventArgs e) { _workflowStep = 1; _workflowCaptureConfirmed = false; RefreshWorkflowSummary(); }
    private void EditResult_Click(object sender, RoutedEventArgs e) { _workflowStep = 2; _workflowResultConfirmed = false; RefreshWorkflowSummary(); }

    private void InvalidateWorkflowLanguages()
    {
        if (_isInitializing) return;
        _workflowLanguagesConfirmed = false;
        _workflowStep = 0;
        if (isStarted) OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
        RefreshWorkflowSummary();
    }

    private bool CanChooseWorkflowCapture()
    {
        if (_workflowLanguagesConfirmed && !isStarted && !isSelectingChatBoxArea && !isSelectingExcludeRegion) return true;
        RefreshWorkflowSummary();
        return false;
    }

    private bool PrepareWorkflowStart()
    {
        string? issue = WorkflowStartIssue();
        if (issue != null) { WorkflowStatusText.Text = UiText.Get(issue); return false; }
        if (WantsOverlay)
        {
            // Restore an existing minimized/hidden surface only after an explicit start/resume.
            var overlay = MonitorWindow.Instance;
            overlay.WindowState = WindowState.Normal;
            if (!IsOverlayShown) ToggleMonitorWindow();
        }
        if (WantsSeparate)
        {
            if (ChatBoxWindow.Instance is { IsVisible: true } separate)
                separate.WindowState = WindowState.Normal;
            if (!IsSeparateResultShown) ToggleChatBox();
        }
        if ((WantsOverlay && !IsOverlayShown) || (WantsSeparate && !IsSeparateResultShown))
        {
            WorkflowStatusText.Text = UiText.Get("Flow_OutputFailed");
            return false;
        }
        return true;
    }

    private void ResultChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (confirmResultButton == null || _refreshingWorkflow) return;
        _workflowResultConfirmed = false;
        RefreshWorkflowSummary();
    }

    private void PlaceChatBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_workflowCaptureConfirmed || isSelectingChatBoxArea) return;
        ShowChatBoxSelector();
    }

    private void WorkflowOcrSetup_Click(object sender, RoutedEventArgs e)
    {
        OcrLanguageSetupWindow.ShowFor(this, ConfigManager.Instance.GetSourceLanguage());
        RefreshWorkflowSummary();
    }

    internal void OpenDiagnostics() => ToggleConsoleWindow();
    internal void OpenAbout() => AboutButton_Click(this, new RoutedEventArgs());
    internal void ResetFromSettings() => ResetSettingsButton_Click(this, new RoutedEventArgs());
}
