using System;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private GeneralPreferenceDraft? _generalDraft;
    private bool _savingGeneralDraft;
    private bool _changingSettingsCategory;
    private Action? _pendingGeneralAction;
    private string? _generalDraftStatus;

    private GeneralPreferenceValues CurrentGeneralPreferences() => ConfigManager.Instance.ReadGeneralPreferences();

    private void RefreshGeneralDraft()
    {
        if (_savingGeneralDraft) return;
        var current = CurrentGeneralPreferences();
        if (_generalDraft == null) _generalDraft = new(current);
        else _generalDraft.Observe(current);
        _syncingGeneralPreferences = true;
        try
        {
            InterfaceLanguageOptions.SelectPreference(interfaceLanguageComboBox, _generalDraft.Value.Language);
            SelectComboBoxItemByTag(appearanceComboBox, _generalDraft.Value.Theme);
            clipboardAutoTranslateCheckBox.IsChecked = _generalDraft.Value.ClipboardEnabled;
            clipboardCopyResultCheckBox.IsChecked = _generalDraft.Value.ClipboardCopyResult;
            clipboardConsentCheckBox.IsChecked = _generalDraft.Value.ClipboardConsent;
            clipboardDebounceTextBox.Text = _generalDraft.Value.ClipboardDebounce;
            clipboardMaxCharsTextBox.Text = _generalDraft.Value.ClipboardMaxChars;
        }
        finally { _syncingGeneralPreferences = false; }
        RenderGeneralDraft();
    }

    private void RenderGeneralDraft()
    {
        if (_generalDraft == null || generalDraftStatusText == null) return;
        bool dirty = _generalDraft.IsDirty;
        bool conflict = _generalDraft.HasConflict(CurrentGeneralPreferences());
        string? issue = _generalDraft.Value.ValidationIssue;
        UiText.Bind(clipboardValidationText, TextBlock.TextProperty, issue ?? "Draft_ClipboardLimitsHint");
        clipboardValidationText.SetResourceReference(TextBlock.ForegroundProperty, issue != null ? "DangerBrush" : "MutedBrush");
        string key = conflict ? "Draft_GeneralConflict"
            : _pendingGeneralAction != null ? "Draft_GeneralDecision"
            : issue ?? _generalDraftStatus ?? (dirty ? "Draft_GeneralDirty" : "Draft_GeneralClean");
        UiText.Bind(generalDraftStatusText, TextBlock.TextProperty, key);
        generalDraftStatusText.SetResourceReference(TextBlock.ForegroundProperty,
            conflict || _pendingGeneralAction != null ? "WarningBrush" : "MutedBrush");
        saveGeneralDraftButton.IsEnabled = dirty && !conflict && issue == null && !_savingGeneralDraft;
        cancelGeneralDraftButton.IsEnabled = dirty && !_savingGeneralDraft;
        stayWithGeneralDraftButton.Visibility = _pendingGeneralAction != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GeneralDraftEdited()
    {
        _generalDraftStatus = null;
        RenderGeneralDraft();
        // Returning all fields to their saved values also resolves a pending leave request.
        if (_generalDraft?.IsDirty == false) CompleteGeneralAction();
    }

    private void SaveGeneralDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_generalDraft == null || !_generalDraft.IsDirty || _savingGeneralDraft || _generalDraft.Value.ValidationIssue != null) return;
        bool saved = false;
        bool conflict = false;
        _savingGeneralDraft = true;
        try
        {
            saved = ApplicationPreferences.SaveGeneralDraft(_generalDraft.Baseline, _generalDraft.Value, out conflict);
            if (saved)
            {
                _generalDraft.Reset(CurrentGeneralPreferences());
                _generalDraftStatus = "Draft_GeneralSaved";
            }
            else _generalDraftStatus = conflict ? "Draft_GeneralConflict" : "Draft_GeneralFailed";
        }
        finally
        {
            _savingGeneralDraft = false;
            RefreshGeneralDraft();
        }
        if (saved) CompleteGeneralAction();
    }

    private void CancelGeneralDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_generalDraft == null || _savingGeneralDraft) return;
        _generalDraft.Reset(CurrentGeneralPreferences());
        _generalDraftStatus = "Draft_GeneralCancelled";
        RefreshGeneralDraft();
        CompleteGeneralAction();
    }

    private void StayWithGeneralDraft_Click(object sender, RoutedEventArgs e)
    {
        _pendingGeneralAction = null;
        _generalDraftStatus = null;
        RenderGeneralDraft();
        interfaceLanguageComboBox.Focus();
    }

    private void CompleteGeneralAction()
    {
        var action = _pendingGeneralAction;
        _pendingGeneralAction = null;
        RenderGeneralDraft();
        action?.Invoke();
    }

    private void RequestGeneralDecision(Action action)
    {
        if (_generalDraft?.IsDirty != true) { action(); return; }
        _pendingGeneralAction = action;
        _generalDraftStatus = null;
        SelectSettingsCategory(generalSettingsCategory);
        RenderGeneralDraft();
        generalDraftStatusText.BringIntoView();
        if (saveGeneralDraftButton.IsEnabled) saveGeneralDraftButton.Focus();
        else cancelGeneralDraftButton.Focus();
    }

    private void SelectSettingsCategory(TabItem category)
    {
        _changingSettingsCategory = true;
        try { settingsCategories.SelectedItem = category; }
        finally { _changingSettingsCategory = false; }
    }

    private void SettingsCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, settingsCategories) || _changingSettingsCategory || !_initializeRuntime ||
            settingsCategories.SelectedItem is not TabItem target) return;
        if (_generalDraft?.IsDirty == true && !ReferenceEquals(target, generalSettingsCategory))
            RequestGeneralDecision(() => SelectSettingsCategory(target));
        else if (_serviceDraft?.IsDirty == true && !ReferenceEquals(target, serviceSettingsCategory))
            RequestServiceDecision(() => SelectSettingsCategory(target));
        else if (HasCaptureEdits && !ReferenceEquals(target, captureSettingsCategory))
            RequestCaptureDecision(() => SelectSettingsCategory(target));
        else if (_resultDraft?.IsDirty == true && !ReferenceEquals(target, resultSettingsCategory))
            RequestResultDecision(() => SelectSettingsCategory(target));
        else if (_advancedDraft?.IsDirty == true && !ReferenceEquals(target, advancedSettingsCategory))
            RequestRemainingDecision(true, () => SelectSettingsCategory(target));
        else if (_shortcutDraft?.IsDirty == true && !ReferenceEquals(target, shortcutsSettingsCategory))
            RequestRemainingDecision(false, () => SelectSettingsCategory(target));
    }

    private bool CanEditGeneralDraft => !_isInitializing && !_syncingGeneralPreferences && _generalDraft != null;

    private void ClipboardAutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!CanEditGeneralDraft) return;
        _generalDraft!.SetClipboardEnabled(clipboardAutoTranslateCheckBox.IsChecked == true);
        _syncingGeneralPreferences = true;
        try { clipboardConsentCheckBox.IsChecked = _generalDraft.Value.ClipboardConsent; }
        finally { _syncingGeneralPreferences = false; }
        GeneralDraftEdited();
    }

    private void ClipboardCopyResultCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!CanEditGeneralDraft) return;
        _generalDraft!.SetClipboardCopyResult(clipboardCopyResultCheckBox.IsChecked == true);
        GeneralDraftEdited();
    }

    private void ClipboardConsentCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!CanEditGeneralDraft) return;
        _generalDraft!.SetClipboardConsent(clipboardConsentCheckBox.IsChecked == true);
        GeneralDraftEdited();
    }

    private void ClipboardLimits_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!CanEditGeneralDraft) return;
        _generalDraft!.SetClipboardDebounce(clipboardDebounceTextBox.Text);
        _generalDraft.SetClipboardMaxChars(clipboardMaxCharsTextBox.Text);
        GeneralDraftEdited();
    }

    internal void RequestDismiss() => RequestSettingsDecision(Hide);
}
