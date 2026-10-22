using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private CapturePreferenceDraft? _captureDraft;
    private bool _syncingCaptureDraft;
    private bool _savingCaptureDraft;
    private string? _captureScreenTopology;
    private string? _captureDraftStatus;
    private bool _captureTopologyNeedsSelection;
    private bool HasCaptureEdits => _captureDraft?.IsDirty == true || _captureTopologyNeedsSelection;
    private Action? _pendingCaptureAction;
    private bool CanEditCaptureDraft => !_isInitializing && !_syncingCaptureDraft && _captureDraft != null;
    private int[] CaptureScreenIndices => screenComboBox.Items.OfType<ComboBoxItem>().Where(item => item.Tag is int).Select(item => (int)item.Tag).ToArray();

    private void ObserveCaptureScreenTopology(string topology)
    {
        if (_captureScreenTopology != null && _captureScreenTopology != topology && _captureDraft != null)
        {
            _captureDraft.SetScreen(-1);
            _captureTopologyNeedsSelection = true;
            _captureDraftStatus = "Draft_CaptureTopologyChanged";
        }
        _captureScreenTopology = topology;
    }

    private void RefreshCaptureDraft()
    {
        if (!_initializeRuntime || _savingCaptureDraft) return;
        var current = ConfigManager.Instance.ReadCapturePreferences();
        if (_captureDraft == null) _captureDraft = new(current);
        else _captureDraft.Observe(current);
        _syncingCaptureDraft = true;
        try
        {
            ocrMethodComboBox.SelectedItem = ocrMethodComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Content?.ToString() == _captureDraft.Value.OcrMethod);
            screenComboBox.SelectedItem = screenComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is int index && index == _captureDraft.Value.ScreenIndex);
            AutoOCRCheckBox.IsChecked = _captureDraft.Value.AutoOcr;
            autoTranslateCheckBox.IsChecked = _captureDraft.Value.AutoTranslate;
            multiSelectionAreaCheckBox.IsChecked = _captureDraft.Value.MultipleAreas;
            MangaModeCheckBox.IsChecked = _captureDraft.Value.MangaMode;
            UpdateMangaModeVisibility(_captureDraft.Value.OcrMethod);
        }
        finally { _syncingCaptureDraft = false; }
        RenderCaptureDraft();
    }

    private void CaptureFieldChanged(object sender)
    {
        if (!CanEditCaptureDraft) return;
        switch (sender)
        {
            case ComboBox combo when ReferenceEquals(combo, ocrMethodComboBox):
                _captureDraft!.SetOcrMethod((combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "");
                break;
            case ComboBox combo when ReferenceEquals(combo, screenComboBox):
                _captureDraft!.SetScreen((combo.SelectedItem as ComboBoxItem)?.Tag is int index ? index : -1);
                break;
            case CheckBox check when ReferenceEquals(check, AutoOCRCheckBox): _captureDraft!.SetAutoOcr(check.IsChecked == true); break;
            case CheckBox check when ReferenceEquals(check, autoTranslateCheckBox): _captureDraft!.SetAutoTranslate(check.IsChecked == true); break;
            case CheckBox check when ReferenceEquals(check, multiSelectionAreaCheckBox): _captureDraft!.SetMultipleAreas(check.IsChecked == true); break;
            case CheckBox check when ReferenceEquals(check, MangaModeCheckBox): _captureDraft!.SetMangaMode(check.IsChecked == true); break;
            default: return;
        }
        _captureDraftStatus = null;
        RefreshCaptureDraft();
        if (!HasCaptureEdits) CompleteCaptureAction();
    }

    private void RenderCaptureDraft()
    {
        if (_captureDraft == null || captureDraftStatusText == null) return;
        bool conflict = HasCaptureEdits && _captureDraft.Baseline != ConfigManager.Instance.ReadCapturePreferences();
        string? issue = _captureDraft.Value.Validate(CaptureScreenIndices, NativeOcrAssets.IsAvailable);
        string status = conflict ? "Draft_CaptureConflict" : _pendingCaptureAction != null ? "Draft_CaptureDecision"
            : _captureDraftStatus == "Draft_CaptureTopologyChanged" ? "Draft_CaptureTopologyChanged"
            : issue ?? _captureDraftStatus ?? (HasCaptureEdits ? "Draft_CaptureDirty" : "Draft_CaptureClean");
        UiText.Bind(captureDraftStatusText, TextBlock.TextProperty, status);
        captureDraftStatusText.SetResourceReference(TextBlock.ForegroundProperty, conflict || issue != null ? "WarningBrush" : "MutedBrush");
        saveCaptureDraftButton.IsEnabled = HasCaptureEdits && !conflict && issue == null && !_savingCaptureDraft;
        cancelCaptureDraftButton.IsEnabled = HasCaptureEdits && !_savingCaptureDraft;
        stayWithCaptureDraftButton.Visibility = _pendingCaptureAction != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCaptureDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_captureDraft == null || !HasCaptureEdits || _savingCaptureDraft ||
            _captureDraft.Value.Validate(CaptureScreenIndices, NativeOcrAssets.IsAvailable) != null) return;
        bool saved = false;
        string? issue = null;
        var before = _captureDraft.Baseline;
        var edited = _captureDraft.Value;
        _savingCaptureDraft = true;
        try
        {
            saved = ConfigManager.Instance.TrySaveCapturePreferences(before, edited, _captureScreenTopology ?? "", out issue);
            if (saved)
            {
                _captureDraft.Reset(ConfigManager.Instance.ReadCapturePreferences());
                _captureDraftStatus = "Draft_CaptureSaved";
                MainWindow.CapturePreferencesCommitted(before, edited, _captureTopologyNeedsSelection);
                _captureTopologyNeedsSelection = false;
            }
            else _captureDraftStatus = issue ?? "Draft_CaptureFailed";
        }
        finally { _savingCaptureDraft = false; }
        if (issue == "Draft_CaptureTopologyChanged")
        {
            _captureTopologyNeedsSelection = true;
            _captureDraft.SetScreen(-1);
            bool initializing = _isInitializing;
            _isInitializing = true;
            try { LoadAvailableScreens(); }
            finally { _isInitializing = initializing; }
        }
        RefreshCaptureDraft();
        if (saved) CompleteCaptureAction();
    }

    private void CancelCaptureDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_captureDraft == null || _savingCaptureDraft) return;
        _captureDraft.Reset(ConfigManager.Instance.ReadCapturePreferences());
        _captureTopologyNeedsSelection = false;
        _captureDraftStatus = "Draft_CaptureCancelled";
        RefreshCaptureDraft();
        CompleteCaptureAction();
    }

    private void StayWithCaptureDraft_Click(object sender, RoutedEventArgs e)
    {
        _pendingCaptureAction = null; _captureDraftStatus = null;
        RenderCaptureDraft(); ocrMethodComboBox.Focus();
    }

    private void RequestCaptureDecision(Action action)
    {
        if (!HasCaptureEdits) { action(); return; }
        _pendingCaptureAction = action;
        SelectSettingsCategory(captureSettingsCategory);
        RenderCaptureDraft();
        if (saveCaptureDraftButton.IsEnabled) saveCaptureDraftButton.Focus();
        else cancelCaptureDraftButton.Focus();
    }

    private void CompleteCaptureAction()
    {
        var action = _pendingCaptureAction; _pendingCaptureAction = null;
        RenderCaptureDraft(); action?.Invoke();
    }
}
