"""Static design integration checks. Does not compile, load or run Localay/WPF."""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1]
wpf = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
xaml = '{http://schemas.microsoft.com/winfx/2006/xaml}'
sources = {p: p.read_text() for p in (root / 'src').glob('*.cs')}
all_code = '\n'.join(sources.values())
documents = {p: ET.parse(p).getroot() for p in (root / 'src').glob('*.xaml')}
events = {'Click', 'Checked', 'Unchecked', 'SelectionChanged', 'TextChanged', 'Loaded',
          'LostFocus', 'GotFocus', 'LostKeyboardFocus', 'GotKeyboardFocus', 'KeyDown',
          'PreviewKeyDown', 'MouseLeftButtonDown', 'MouseDoubleClick', 'ValueChanged',
          'Closing', 'Closed', 'SizeChanged', 'TrayMouseDoubleClick'}
resources = {el.get(xaml + 'Key') for doc in documents.values() for el in doc.iter() if el.get(xaml + 'Key')}
english = json.loads((root / 'app/Languages/en.json').read_text())
turkish = json.loads((root / 'app/Languages/tr.json').read_text())
linked_events = 0
for path, doc in documents.items():
    text = path.read_text()
    for el in doc.iter():
        for key, value in el.attrib.items():
            if key in events and not value.startswith('{'):
                assert re.search(r'\b' + re.escape(value) + r'\s*\(', all_code), (path.name, key, value)
                linked_events += 1
    for key in re.findall(r'\{StaticResource ([A-Za-z]\w*)\}', text):
        assert key in resources, (path.name, key)
    for key in re.findall(r'Strings\[((?:Flow|Draft)_\w+)\]', text):
        assert key in english and key in turkish, (path.name, key)
for key in set(re.findall(r'"((?:Flow|Draft)_\w+)"', all_code)):
    assert key in english and key in turkish, key

main = documents[root / 'src/MainWindow.xaml']
names = [el.get(xaml+'Name') or el.get('Name') for el in main.iter()]
names = [name for name in names if name]
assert len(names) == len(set(names)), 'Duplicate names in the main window'
for removed in ['languageSelector', 'themeToggleButton', 'AppVersion', 'logButton', 'resetSettingsButton']:
    assert removed not in names, removed
for required in ['LanguagesStepContent', 'CaptureStepContent', 'ResultStepContent', 'confirmLanguagesButton',
                 'confirmCaptureButton', 'confirmResultButton', 'WorkflowStatusText', 'toggleButton']:
    assert required in names, required
toggle = next(el for el in main.iter() if el.get(xaml+'Name') == 'toggleButton')
assert toggle.get('IsEnabled') == 'False', 'Start must be unavailable before readiness is evaluated'
settings = documents[root / 'src/SettingsWindow.xaml']
categories = next(el for el in settings.iter(wpf+'TabControl') if el.get(xaml+'Name') == 'settingsCategories')
assert len(list(categories)) == 7, 'Expected seven Settings sections'
assert categories.get('TabStripPlacement') == 'Left'

tokens = json.loads((root / 'docs/design/visual-tokens.json').read_text())
generated = (root / 'src/DesignTokens.cs').read_text()
entries = re.findall(r'\("(\w+)", "(#[0-9A-F]+)", "(#[0-9A-F]+)"\)', generated)
for _, light, dark in entries:
    assert light in tokens['themes']['light'].values()
    assert dark in tokens['themes']['dark'].values()
assert len(entries) == 22
assert 'foreach (var (name, light, dark) in DesignTokens.Palette)' in (root/'src/ThemeManager.cs').read_text()
workflow = (root/'src/MainWindow.Workflow.cs').read_text()
assert '_workflowLanguagesConfirmed' in workflow and '_workflowCaptureConfirmed' in workflow and '_workflowResultConfirmed' in workflow
assert 'PrepareWorkflowStart()' in (root/'src/MainWindow.Session.cs').read_text()
print(f'Static design source checks passed: {len(documents)} XAML files, {linked_events} event links, 7 Settings sections, 22 palette roles.')
print('No C# compilation, WPF loading, application execution or Windows behavior validation was performed.')

# Structural lifecycle wiring checks. These assert source contracts, not runtime behavior.
session = (root/'src/MainWindow.Session.cs').read_text()
logic = (root/'src/Logic.cs').read_text()
main_code = (root/'src/MainWindow.xaml.cs').read_text()
assert toggle.get('Click') == 'SessionPrimary_Click'
for required in ['stopSessionButton', 'trayStartStopItem']:
    assert required in names, required
pause = session.split('private void PauseWorkflowSession(string reason)', 1)[1].split('private void StopWorkflowSession()', 1)[0]
assert pause.index('isStarted = false;') < pause.index('SuspendScreenTranslation();')
assert 'ClearAllTextObjects' not in pause
suspend = logic.split('internal void SuspendScreenTranslation()', 1)[1].split('internal void PrepareScreenTranslationResume()', 1)[0]
assert 'InvalidatePendingWork();' in suspend and 'ClearRenderedTextObjects' not in suspend
invalidate = logic.split('private void InvalidatePendingWork(', 1)[1].split('public void ResetHash()', 1)[0]
assert '_captureLifetime.Invalidate()' in invalidate and '_pipelineLifetime.Invalidate()' in invalidate
assert 'IsTranslationResultVisible) return;' in session
assert 'PauseWorkflowSession("Flow_PausedNoOutput")' in session
for output in ['MonitorWindow.xaml.cs', 'ChatBoxWindow.xaml.cs']:
    code = (root/'src'/output).read_text()
    assert 'IsVisibleChanged += (_, _) => MainWindow.NotifyResultSurfaceChanged();' in code
    assert 'StateChanged += (_, _) => MainWindow.NotifyResultSurfaceChanged();' in code
for manager in ['WindowsOCRManager.cs', 'OneOCRManager.cs']:
    code = (root/'src'/manager).read_text()
    callback = code.split('Application.Current.Dispatcher.Invoke((Action)(() => {', 1)[1]
    assert callback.index('if (canProcess != null && !canProcess()) return;') < callback.index('Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);')
assert logic.count('() => operation.IsCurrent && MainWindow.Instance.GetIsStarted()') == 2
assert 'if (isStarted) StopWorkflowSession();' in main_code
assert 'Dispatcher.Invoke(() => PauseWorkflowSession("Flow_PausedSource"));' in main_code
print('Static session wiring checks passed: pause/stop commands, output visibility hooks, lease invalidation, OCR dispatch guards.')

# Source contracts for the scoped language/theme draft (no WPF or C# execution).
settings_code = (root/'src/SettingsWindow.xaml.cs').read_text()
draft_code = (root/'src/SettingsWindow.GeneralDraft.cs').read_text()
commit_code = (root/'src/PreferenceCommit.cs').read_text()
config_code = (root/'src/ConfigManager.cs').read_text()
def window_names(element):
    # Templates/resources own independent XAML name scopes.
    if element.tag in {wpf+'ControlTemplate', wpf+'DataTemplate', wpf+'Window.Resources'}:
        return []
    own = element.get(xaml+'Name') or element.get('Name')
    return ([own] if own else []) + [name for child in element for name in window_names(child)]
settings_names = window_names(settings)
assert len(settings_names) == len(set(settings_names))
for required in ['generalSettingsCategory', 'generalDraftStatusText', 'saveGeneralDraftButton',
                 'cancelGeneralDraftButton', 'stayWithGeneralDraftButton']:
    assert required in settings_names, required
assert categories.get('SelectionChanged') == 'SettingsCategory_SelectionChanged'
for handler, following in [('InterfaceLanguagePreference_SelectionChanged', 'AppearancePreference_SelectionChanged'),
                           ('AppearancePreference_SelectionChanged', 'QuickStartFromSettings_Click')]:
    body = settings_code.split('private void '+handler+'(', 1)[1].split('private void '+following+'(', 1)[0]
    assert '_generalDraft.Set' in body
    assert 'SetLanguagePreference(' not in body and 'ApplyThemePreference(' not in body
assert 'window.RequestDismiss();' in main_code
closing = settings_code.split('protected override void OnClosing(', 1)[1].split('protected override void OnClosed(', 1)[0]
assert 'RequestDismiss();' in closing and 'Hide();' not in closing
assert '_pendingGeneralAction = action;' in draft_code and 'HasConflict(CurrentGeneralPreferences())' in draft_code
batch = commit_code.split('internal static bool TryUpdate(', 1)[1]
assert batch.index('try { persist(candidate); }') < batch.index('values[entry.Key] = entry.Value;')
api = config_code.split('internal bool TrySaveGeneralPreferences(', 1)[1].split('// Compatibility helpers:', 1)[0]
assert api.count('WriteConfig("Default", candidate)') == 1
assert api.index('if (conflict) return false;') < api.index('PreferenceCommit.TryUpdate(')
assert 'Draft_ClipboardImmediate' not in (root/'src/SettingsWindow.xaml').read_text()
print('Static interface draft checks passed: scoped actions, dismissal/navigation guards, conflict detection and batch persistence order.')

# Clipboard edits must be draft-only; validation and consent precede the single write.
general_model = (root/'src/GeneralPreferenceDraft.cs').read_text()
clipboard_monitor = (root/'src/ClipboardMonitor.cs').read_text()
assert 'edited.ValidationIssue != null' in api
assert api.index('edited.ValidationIssue != null') < api.index('PreferenceCommit.TryUpdate(')
for key in ['CLIPBOARD_AUTO_TRANSLATE_ENABLED', 'CLIPBOARD_AUTO_TRANSLATE_COPY_RESULT',
            'CLIPBOARD_AUTO_TRANSLATE_DEBOUNCE_MS', 'CLIPBOARD_AUTO_TRANSLATE_MAX_CHARS', 'clipboard_privacy_consent_v1']:
    assert key in api, key
for handler in ['ClipboardAutoTranslateCheckBox_CheckedChanged', 'ClipboardCopyResultCheckBox_CheckedChanged',
                'ClipboardConsentCheckBox_Changed', 'ClipboardLimits_TextChanged']:
    body = draft_code.split('private void '+handler+'(', 1)[1].split('\n    }', 1)[0]
    assert 'ConfigManager.' not in body and 'UpdateClipboardMonitorState(' not in body, handler
    assert 'GeneralDraftEdited();' in body, handler
assert 'TextChanged="ClipboardLimits_TextChanged"' in (root/'src/SettingsWindow.xaml').read_text()
assert 'ClipboardDebounceTextBox_LostFocus' not in all_code
assert 'clipboardDebounce >= 100 && clipboardDebounce <= 2000' in config_code
assert 'ClipboardEnabled && !ClipboardConsent' in general_model
assert 'clipboardOperation.ThrowIfStale();' in main_code
assert 'CreateLinkedTokenSource(operation.Token, cancellationToken)' in logic
stop_clipboard = clipboard_monitor.split('public void StopListening()', 1)[1].split('public void SetClipboardTextSilently', 1)[0]
assert stop_clipboard.index('_pendingText = null;') < stop_clipboard.index('if (!_isListening) return;')
app_preferences = (root/'src/ApplicationPreferences.cs').read_text().split('internal static bool SaveGeneralDraft(', 1)[1]
assert app_preferences.index('TrySaveGeneralPreferences(') < app_preferences.index('MainWindow.ApplySavedClipboardPreferences();')
print('Static General/clipboard checks passed: consent, validation, draft-only events, combined persistence and stale request guards.')

# The General action row must remain outside the scrolling form.
parents = {child: parent for parent in settings.iter() for child in parent}
save_general = next(el for el in settings.iter() if el.get(xaml+'Name') == 'saveGeneralDraftButton')
ancestor = parents[save_general]
while ancestor is not settings:
    assert ancestor.tag != wpf+'ScrollViewer', 'General actions must stay visible while scrolling'
    ancestor = parents[ancestor]
assert 'CancelDeferredStart();' in stop_clipboard
print('General fixed action area and deferred clipboard startup cancellation are connected.')

# Service page: no direct persistence from edits, and credentials/prompts share one commit.
service_editor = (root/'src/SettingsWindow.ServiceDraft.cs').read_text()
service_model = (root/'src/ServicePreferenceDraft.cs').read_text()
service_api = config_code.split('internal bool TrySaveServicePreferences(', 1)[1].split('// Profiles carry game processing', 1)[0]
assert 'ServicePreferenceDraft.Validate(edited, expected)' in service_api
assert service_api.count('WriteConfig("Default", candidate)') == 1
assert service_api.index('PreferenceCommit.TryUpdate(') < service_api.index('_currentTranslationService = edited[TRANSLATION_SERVICE]')
assert 'current[p.Key] != p.Value' in service_api, 'Unedited legacy prompts must remain fallback files'
for field in ['serviceSettingsCategory', 'serviceDraftStatusText', 'saveServiceDraftButton', 'cancelServiceDraftButton', 'stayWithServiceDraftButton']:
    assert field in settings_names, field
save_service = next(el for el in settings.iter() if el.get(xaml+'Name') == 'saveServiceDraftButton')
ancestor = parents[save_service]
while ancestor is not settings:
    assert ancestor.tag != wpf+'ScrollViewer', 'Service actions must stay visible while scrolling'
    ancestor = parents[ancestor]
for handler in ['TranslationServiceComboBox_SelectionChanged', 'GeminiModelComboBox_SelectionChanged',
                'OllamaUrlTextBox_TextChanged', 'CustomApiUrlTextBox_TextChanged', 'GroqModelComboBox_LostFocus',
                'MicrosoftLegacyModeCheckBox_CheckedChanged', 'SaveApiKeysButton_Click', 'ViewApiKeysButton_Click']:
    body = settings_code.split('void '+handler+'(', 1)[1].split('\n        }', 1)[0]
    assert 'ConfigManager.' not in body and 'Logic.Instance' not in body, handler
visibility = settings_code.split('private void UpdateServiceSpecificSettings(', 1)[1].split('private void GeminiApiKeyPasswordBox_PasswordChanged(', 1)[0]
assert 'ConfigManager.' not in visibility, 'Provider switching must not overwrite drafts with committed fields'
assert 'ServicePrompt_TextChanged' in (root/'src/SettingsWindow.xaml').read_text()
legacy_prompt_writer = config_code.split('public bool SaveServicePrompt(', 1)[1].split('// Check if auto sizing', 1)[0]
assert 'File.WriteAllText' not in legacy_prompt_writer
prompt_reader = config_code.split('public string GetServicePrompt(', 1)[1].split('public bool SaveServicePrompt(', 1)[0]
assert prompt_reader.index('_configValues.TryGetValue(ServicePreferenceDraft.PromptKey(service)') < prompt_reader.index('File.ReadAllText(filePath)')
assert 'Action<List<string>>? updateDraft = null' in (root/'src/ApiKeysWindow.xaml.cs').read_text()
assert 'RequestServiceDecision(() => SelectSettingsCategory(target))' in draft_code
assert 'ServicePreferencesCommitted();' in service_editor
assert 'new ServicePreferenceDraft(_values)' in service_model and 'No record-generated ToString' in service_model
print('Static service checks passed: fixed action area, draft event routes, key dialog callback, prompt fallback and one atomic service commit.')

# Capture: one six-field draft and commit; changing input is not an OCR/session action.
capture_editor = (root/'src/SettingsWindow.CaptureDraft.cs').read_text()
capture_api = config_code.split('internal bool TrySaveCapturePreferences(', 1)[1].split('public string GetOcrMethod()', 1)[0]
for name in ['captureSettingsCategory', 'captureDraftStatusText', 'saveCaptureDraftButton', 'cancelCaptureDraftButton', 'stayWithCaptureDraftButton']:
    assert name in settings_names, name
for handler in ['ScreenComboBox_SelectionChanged', 'OcrMethodComboBox_SelectionChanged', 'AutoTranslateCheckBox_CheckedChanged',
                'MultiSelectionAreaCheckBox_CheckedChanged', 'AutoOCRCheckBox_CheckedChanged', 'MangaModeCheckBox_CheckedChanged']:
    body = settings_code.split('void '+handler+'(', 1)[1].split('\n        }', 1)[0]
    assert 'CaptureFieldChanged(sender);' in body and 'ConfigManager.' not in body and 'MainWindow.Instance' not in body, handler
manga_visibility = settings_code.split('private void UpdateMangaModeVisibility(', 1)[1].split('private void AutoTranslateCheckBox_CheckedChanged(', 1)[0]
assert 'ConfigManager.' not in manga_visibility, 'Hydrating manga visibility must not write settings'
assert capture_api.count('WriteConfig("Default", candidate)') == 1
for key in ['OCR_METHOD', 'SELECTED_SCREEN_INDEX', 'AUTO_OCR', 'AUTO_TRANSLATE_ENABLED', 'MULTI_SELECTION_AREA', 'MANGA_MODE']:
    assert '['+key+']' in capture_api, key
assert capture_api.index('DescribeCaptureScreens(screens) != expectedTopology') < capture_api.index('PreferenceCommit.TryUpdate(')
assert capture_api.index('edited.Validate(') < capture_api.index('PreferenceCommit.TryUpdate(')
assert '_captureDraft.SetScreen(-1);' in capture_editor and 'HasCaptureEdits' in capture_editor
assert 'RequestCaptureDecision(() => SelectSettingsCategory(target))' in draft_code
assert 'RequestCaptureDecision(() => RequestResultDecision(() => RequestRemainingDecision(true, () => RequestRemainingDecision(false, action))))' in service_editor
save_capture = next(el for el in settings.iter() if el.get(xaml+'Name') == 'saveCaptureDraftButton')
ancestor = parents[save_capture]
while ancestor is not settings:
    assert ancestor.tag != wpf+'ScrollViewer', 'Capture actions must remain outside the scrolling form'
    ancestor = parents[ancestor]
main_session = (root/'src/MainWindow.Session.cs').read_text()
capture_apply = main_session.split('internal static void CapturePreferencesCommitted(', 1)[1].split('internal static void ServicePreferencesCommitted()', 1)[0]
assert 'PauseWorkflowSession("Draft_CaptureSessionPaused")' in capture_apply
assert 'main._workflowLanguagesConfirmed = false' in capture_apply
assert 'main._workflowCaptureNeedsSelection = true' in capture_apply
assert 'main._workflowCaptureConfirmed = false' in capture_apply and 'main._workflowResultConfirmed = false' in capture_apply
assert 'SetAutoTranslateEnabled(' not in capture_apply and 'SetSelectedScreenIndex(' not in capture_apply
assert 'if (_workflowCaptureNeedsSelection) return "Draft_CaptureSelectSource";' in workflow
assert 'savedTranslationAreas[index].ScreenIndex != ConfigManager.Instance.GetSelectedScreenIndex()' in main_code
monitor_code = (root/'src/MonitorWindow.xaml.cs').read_text()
monitor_auto = monitor_code.split('private void AutoTranslateCheckBox_CheckedChanged(', 1)[1].split('private void MonitorWindow_Loaded(', 1)[0]
assert 'if (_isInitializing) return;' in monitor_auto
print('Static capture checks passed: six-field atomic commit, topology validation, draft-only edits, silent sync and source confirmation guards.')

# Result: isolated sample previews, one atomic draft, and a single settings route.
result_editor = (root/'src/SettingsWindow.ResultDraft.cs').read_text()
result_model = (root/'src/ResultPreferenceDraft.cs').read_text()
result_api = config_code.split('internal bool TrySaveResultPreferences(', 1)[1].split('internal CapturePreferenceValues ReadCapturePreferences()', 1)[0]
assert result_api.count('WriteConfig("Default", candidate)') == 1
assert result_api.index('ResultPreferenceDraft.Validate(edited)') < result_api.index('PreferenceCommit.TryUpdate(')
assert 'conflict = !ResultPreferenceDraft.Same(expected, current)' in result_api
preview = result_editor.split('private void UpdateResultPreview(', 1)[1].split('private void RenderResultDraft()', 1)[0]
assert 'ConfigManager.' not in preview and 'Logic.Instance' not in preview and 'Screen.' not in preview
edit = result_editor.split('private void ResultInputEdited(', 1)[1].split('private void RefreshResultDraft()', 1)[0]
assert 'ConfigManager.' not in edit and 'Logic.Instance' not in edit
save_result = result_editor.split('private void SaveResultDraft_Click(', 1)[1].split('private void CancelResultDraft_Click(', 1)[0]
assert save_result.index('TrySaveResultPreferences(') < save_result.index('MainWindow.ResultPreferencesCommitted(')
for name in ['saveResultDraftButton', 'resultDraftStatusText', 'overlayPreviewSurface', 'chatPreviewSurface']:
    element = next(el for el in settings.iter() if el.get(xaml+'Name') == name)
    ancestor = parents[element]
    while ancestor is not settings:
        assert ancestor.tag != wpf+'ScrollViewer', name+' must remain outside the scrolling form'
        ancestor = parents[ancestor]
fields = re.findall(r'AddResultInput\(\w+, "([^"]+)"', result_editor)
assert len(fields) == len(set(fields)) == 21
for field in fields: assert '["'+field+'"]' in result_model, field
for code in [settings_code, (root/'src/SettingsWindow.Design.cs').read_text(), (root/'src/ChatBoxWindow.xaml.cs').read_text()]:
    assert 'new OverlayOptionsWindow(' not in code and 'new ChatBoxOptionsWindow(' not in code
assert 'ShowResultFor(MainWindow.Instance)' in (root/'src/ChatBoxWindow.xaml.cs').read_text()
assert 'RequestResultDecision(() => SelectSettingsCategory(target))' in draft_code
result_apply = main_session.split('internal static void ResultPreferencesCommitted(', 1)[1].split('internal static void AdvancedPreferencesCommitted()', 1)[0]
assert 'PauseWorkflowSession("Draft_ResultSessionPaused")' in result_apply
assert 'ClearAllTextObjects(' not in result_apply and 'StartWorkflowSession(' not in result_apply
assert 'ChatBoxWindow.Instance?.ApplyConfigurationStyling()' in result_apply
assert 'RefreshResultAppearanceIfCreated()' in result_apply
assert 'double.IsFinite(number)' in result_model
print('Static result checks passed: 21 draft fields, isolated previews, fixed actions, atomic save, unified route and preserved session text.')

# Final settings integration: the remaining editable pages use drafts too.
remaining = (root/'src/SettingsWindow.RemainingDrafts.cs').read_text()
section_model = (root/'src/SectionPreferenceDraft.cs').read_text()
for name in ['saveAdvancedDraftButton', 'saveShortcutsDraftButton', 'advancedDraftStatusText', 'shortcutsDraftStatusText']:
    element = next(el for el in settings.iter() if el.get(xaml+'Name') == name)
    ancestor = parents[element]
    while ancestor is not settings:
        assert ancestor.tag != wpf+'ScrollViewer', name
        ancestor = parents[ancestor]
for handler in ['CharLevelCheckBox_CheckedChanged', 'BlockDetectionPowerTextBox_LostFocus', 'SettleTimeTextBox_LostFocus',
                'MaxContextPiecesTextBox_LostFocus', 'MinContextSizeTextBox_LostFocus', 'MinChatBoxTextSizeTextBox_LostFocus',
                'GameInfoTextBox_TextChanged', 'MinTextFragmentSizeTextBox_LostFocus', 'TextSimilarThresholdTextBox_LostFocus',
                'MinLetterConfidenceTextBox_LostFocus', 'MinLineConfidenceTextBox_LostFocus', 'LineSpacingFactorTextBox_LostFocus',
                'LlmPreferSpeedCheckBox_Changed', 'TranslationPrefetchCheckBox_Changed', 'TranslationCacheCheckBox_Changed',
                'SetHotKeyEnableCheckBox_CheckChange', 'HDRSupportCheckBox_CheckedChanged']:
    body = settings_code.split('void '+handler+'(', 1)[1].split('\n        }', 1)[0]
    assert 'StageRemainingField(sender);' in body and 'ConfigManager.' not in body and 'Logic.Instance' not in body, handler
for handler in ['AssignHotkey', 'ResetHotkeysButton_Click', 'SaveIgnorePhrases']:
    body = settings_code.split('void '+handler+'(', 1)[1].split('\n        }', 1)[0]
    assert 'SaveConfig(' not in body and '.SetHotKey(' not in body and '.SaveIgnorePhrases(' not in body
assert 'double.IsFinite(number)' in section_model
section_api = config_code.split('internal bool TrySaveSectionPreferences(', 1)[1].split('internal IReadOnlyDictionary<string, string> ReadResultPreferences()', 1)[0]
assert section_api.count('WriteConfig("Default", candidate)') == 1
assert section_api.index('InvalidAdvancedField(edited)') < section_api.index('PreferenceCommit.TryUpdate(')
assert section_api.index('TryParseHotkey(') < section_api.index('PreferenceCommit.TryUpdate(')
assert 'combinations.Add((modifiers, key))' in section_api
save_remaining = remaining.split('private void SaveRemainingDraft_Click(', 1)[1].split('private void CancelRemainingDraft_Click(', 1)[0]
assert save_remaining.index('TrySaveSectionPreferences(') < save_remaining.index('MainWindow.AdvancedPreferencesCommitted()')
assert save_remaining.index('TrySaveSectionPreferences(') < save_remaining.index('KeyboardShortcuts.ReloadHotkeys()')
profiles = (root/'src/SettingsWindow.Profiles.cs').read_text()
assert 'MessageBox' not in profiles and 'RequestSettingsDecision(' in profiles
for handler in ['CreateProfileButton_Click', 'UpdateProfileButton_Click', 'RemoveProfileButton_Click', 'LoadProfileButton_Click']:
    body = settings_code.split('void '+handler+'(', 1)[1].split('\n        }', 1)[0]
    assert 'RequestProfileOperation(' in body and 'File.Delete' not in body and 'GetTranslationAreas(' not in body
profile_api = config_code.split('private static HashSet<string> ProfilePreferenceKeys()', 1)[1].split('internal IReadOnlyDictionary<string, string> ReadAdvancedPreferences()', 1)[0]
assert 'SELECTED_SCREEN_INDEX' not in profile_api and 'TRANSLATION_AREAS' not in profile_api
assert 'ProfileDocument.Read(' in profile_api and 'PreferenceCommit.TryUpdate(_configValues, edits' in profile_api
assert 'savedTranslationAreas.Clear()' not in profiles
assert 'CapturePreferencesCommitted(captureBefore, config.ReadCapturePreferences(), true)' in profiles
print('Final static checks passed: advanced/shortcut drafts, ignore-list staging, one commit, shortcut conflicts, inline profile decisions and scoped profile import.')
