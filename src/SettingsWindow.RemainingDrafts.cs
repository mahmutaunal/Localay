using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private SectionPreferenceDraft? _advancedDraft, _shortcutDraft;
    private bool _syncingRemainingDrafts;
    private readonly Dictionary<bool, Action> _pendingSectionActions = new();
    private readonly Dictionary<bool, string> _sectionStatuses = new();
    private readonly Dictionary<string, string> _advancedFields = new()
    {
        ["blockDetectionPowerTextBox"] = "block_detection_scale", ["lineSpacingFactorTextBox"] = "line_spacing_factor",
        ["settleTimeTextBox"] = "block_detection_settle_time", ["textSimilarThresholdTextBox"] = "textsimilar_threshold",
        ["charLevelCheckBox"] = "char_level", ["minTextFragmentSizeTextBox"] = "min_text_fragment_size",
        ["minLetterConfidenceTextBox"] = "min_letter_confidence", ["minLineConfidenceTextBox"] = "min_line_confidence",
        ["hdrSupportCheckBox"] = "hdr_support", ["maxContextPiecesTextBox"] = "max_context_pieces",
        ["minContextSizeTextBox"] = "min_context_size", ["minChatBoxTextSizeTextBox"] = "chatbox_min_text_size",
        ["gameInfoTextBox"] = "game_info", ["llmPreferSpeedCheckBox"] = "llm_prefer_speed",
        ["translationCacheCheckBox"] = "translation_cache_enabled", ["translationPrefetchCheckBox"] = "translation_prefetch"
    };
    private SectionPreferenceDraft? RemainingDraft(bool advanced) => advanced ? _advancedDraft : _shortcutDraft;
    private TabItem RemainingCategory(bool advanced) => advanced ? advancedSettingsCategory : shortcutsSettingsCategory;
    private Button RemainingSave(bool advanced) => advanced ? saveAdvancedDraftButton : saveShortcutsDraftButton;
    private Button RemainingCancel(bool advanced) => advanced ? cancelAdvancedDraftButton : cancelShortcutsDraftButton;
    private Button RemainingStay(bool advanced) => advanced ? stayWithAdvancedDraftButton : stayWithShortcutsDraftButton;
    private IReadOnlyDictionary<string, string> ReadRemaining(bool advanced) => advanced
        ? ConfigManager.Instance.ReadAdvancedPreferences() : ConfigManager.Instance.ReadShortcutPreferences();

    private void InitializeRemainingEditors()
    {
        foreach (var pair in _advancedFields)
        {
            if (FindName(pair.Key) is not Control input) continue;
            if (input is TextBox text) text.TextChanged += RemainingField_Changed;
            if (SectionPreferenceDraft.Ranges.TryGetValue(pair.Value, out var range))
                input.ToolTip = string.Format(CultureInfo.InvariantCulture, "{0}–{1}", range.Min, range.Max);
            if (input.Parent is Grid grid)
            {
                var label = grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetRow(t) == Grid.GetRow(input));
                if (label != null) System.Windows.Automation.AutomationProperties.SetLabeledBy(input, label);
            }
        }
        UpdateProfileActions();
    }
    private void RemainingField_Changed(object sender, TextChangedEventArgs e) => StageRemainingField(sender);
    private void StageRemainingField(object sender)
    {
        if (_isInitializing || _syncingRemainingDrafts || sender is not Control control) return;
        bool advanced = control != hotKeyEnableCheckBox;
        var draft = RemainingDraft(advanced);
        if (draft == null) return;
        string key = advanced ? _advancedFields[control.Name] : ConfigManager.HOT_KEY_ENABLE;
        draft.Set(key, control is CheckBox check ? (check.IsChecked == true ? "true" : "false") : ((TextBox)control).Text);
        RemainingEdited(advanced);
    }
    private void RemainingEdited(bool advanced)
    {
        _sectionStatuses.Remove(advanced); RenderRemainingDraft(advanced);
        if (RemainingDraft(advanced)?.IsDirty != true) CompleteRemainingAction(advanced);
    }
    private void RefreshRemainingDrafts()
    {
        if (!_initializeRuntime) return;
        foreach (bool advanced in new[] { true, false })
        {
            var current = ReadRemaining(advanced);
            var draft = RemainingDraft(advanced);
            if (draft == null)
            {
                draft = new(current);
                if (advanced) _advancedDraft = draft; else _shortcutDraft = draft;
            }
            else draft.Observe(current);
        }
        HydrateRemainingDrafts();
    }
    private void HydrateRemainingDrafts()
    {
        _syncingRemainingDrafts = true;
        try
        {
            if (_advancedDraft != null) foreach (var pair in _advancedFields)
            {
                if (FindName(pair.Key) is CheckBox check) check.IsChecked = _advancedDraft[pair.Value] == "true";
                else if (FindName(pair.Key) is TextBox text) text.Text = _advancedDraft[pair.Value];
            }
            if (_shortcutDraft != null)
            {
                hotKeyEnableCheckBox.IsChecked = _shortcutDraft[ConfigManager.HOT_KEY_ENABLE] == "true";
                foreach (var row in _hotkeyRows) row.Hotkey = _shortcutDraft[ConfigManager.Instance.HotkeyPreferenceKey(row.FunctionName)];
                UpdateHotkeyConflicts();
            }
            LoadIgnorePhrases();
        }
        finally { _syncingRemainingDrafts = false; }
        RenderRemainingDraft(true); RenderRemainingDraft(false);
    }
    private string? RemainingIssue(bool advanced)
    {
        var draft = RemainingDraft(advanced);
        if (draft == null) return null;
        if (!advanced)
        {
            var values = _hotkeyRows.Where(r => !string.IsNullOrEmpty(r.Hotkey)).ToArray();
            return values.Any(r => !KeyboardShortcuts.TryParseHotkey(r.Hotkey, out _, out _) ||
                values.Any(other => other != r && KeyboardShortcuts.AreSameHotkey(r.Hotkey, other.Hotkey)))
                ? UiText.Get("Draft_ShortcutInvalid") : null;
        }
        string? field = SectionPreferenceDraft.InvalidAdvancedField(draft.Values);
        if (field == null) return null;
        string name = _advancedFields.FirstOrDefault(p => p.Value == field).Key ?? "";
        string label = name.Length > 0 && FindName(name) is Control input &&
            System.Windows.Automation.AutomationProperties.GetLabeledBy(input) is TextBlock text ? text.Text : UiText.Get("Settings_Advanced");
        string range = SectionPreferenceDraft.Ranges.TryGetValue(field, out var limits)
            ? string.Format(CultureInfo.InvariantCulture, "{0}–{1}", limits.Min, limits.Max) : "";
        return range.Length > 0 ? UiText.Format("Draft_SectionInvalid", label, range) : UiText.Format("Draft_SectionTextInvalid", label);
    }
    private void RenderRemainingDraft(bool advanced)
    {
        var draft = RemainingDraft(advanced); if (draft == null) return;
        bool conflict = draft.HasConflict(ReadRemaining(advanced));
        string? issue = RemainingIssue(advanced);
        var status = advanced ? advancedDraftStatusText : shortcutsDraftStatusText;
        if (issue != null) status.Text = issue;
        else UiText.Bind(status, TextBlock.TextProperty, conflict ? "Draft_SectionConflict" : _pendingSectionActions.ContainsKey(advanced)
            ? "Draft_SectionDecision" : _sectionStatuses.GetValueOrDefault(advanced) ?? (draft.IsDirty ? "Draft_SectionDirty" : "Draft_SectionClean"));
        status.SetResourceReference(TextBlock.ForegroundProperty, issue != null || conflict ? "WarningBrush" : "MutedBrush");
        RemainingSave(advanced).IsEnabled = draft.IsDirty && !conflict && issue == null;
        RemainingCancel(advanced).IsEnabled = draft.IsDirty;
        RemainingStay(advanced).Visibility = _pendingSectionActions.ContainsKey(advanced) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SaveRemainingDraft_Click(object sender, RoutedEventArgs e)
    {
        bool advanced = ReferenceEquals(sender, saveAdvancedDraftButton);
        StopHotkeyCapture();
        var draft = RemainingDraft(advanced);
        if (draft?.IsDirty != true || RemainingIssue(advanced) != null) return;
        bool saved = ConfigManager.Instance.TrySaveSectionPreferences(advanced, draft.Baseline, draft.Values, out bool conflict);
        _sectionStatuses[advanced] = saved ? "Draft_SectionSaved" : conflict ? "Draft_SectionConflict" : "Draft_SectionFailed";
        if (saved)
        {
            draft.Reset(ReadRemaining(advanced));
            if (advanced) MainWindow.AdvancedPreferencesCommitted();
            else { KeyboardShortcuts.ReloadHotkeys(); RefreshMainWindowHotkeys(); }
        }
        RenderRemainingDraft(advanced);
        if (saved) CompleteRemainingAction(advanced);
    }
    private void CancelRemainingDraft_Click(object sender, RoutedEventArgs e)
    {
        bool advanced = ReferenceEquals(sender, cancelAdvancedDraftButton);
        StopHotkeyCapture(); RemainingDraft(advanced)?.Reset(ReadRemaining(advanced));
        _sectionStatuses[advanced] = "Draft_SectionCancelled"; HydrateRemainingDrafts(); CompleteRemainingAction(advanced);
    }
    private void StayWithRemainingDraft_Click(object sender, RoutedEventArgs e)
    {
        bool advanced = ReferenceEquals(sender, stayWithAdvancedDraftButton);
        _pendingSectionActions.Remove(advanced); RenderRemainingDraft(advanced);
    }
    private void RequestRemainingDecision(bool advanced, Action action)
    {
        if (RemainingDraft(advanced)?.IsDirty != true) { action(); return; }
        StopHotkeyCapture(); _pendingSectionActions[advanced] = action;
        SelectSettingsCategory(RemainingCategory(advanced)); RenderRemainingDraft(advanced);
        if (RemainingSave(advanced).IsEnabled) RemainingSave(advanced).Focus(); else RemainingCancel(advanced).Focus();
    }
    private void CompleteRemainingAction(bool advanced)
    {
        _pendingSectionActions.Remove(advanced, out var action); RenderRemainingDraft(advanced); action?.Invoke();
    }
}
