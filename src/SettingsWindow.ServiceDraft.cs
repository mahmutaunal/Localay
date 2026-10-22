using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private ServicePreferenceDraft? _serviceDraft;
    private IReadOnlyDictionary<string, string>? _observedServicePreferences;
    private bool _syncingServiceDraft;
    private bool _savingServiceDraft;
    private Action? _pendingServiceAction;
    private string? _serviceDraftStatus;
    private bool CanEditServiceDraft => !_isInitializing && !_syncingServiceDraft && _serviceDraft != null;
    private static readonly Dictionary<string, string> ServiceFields = new()
    {
        ["geminiModelComboBox"] = "gemini_model", ["groqModelComboBox"] = "groq_model",
        ["mistralModelComboBox"] = "mistral_model", ["chatGptModelComboBox"] = "chatgpt_model",
        ["customApiUrlTextBox"] = "custom_api_url", ["customApiModelTextBox"] = "custom_api_model",
        ["ollamaUrlTextBox"] = "ollama_url", ["ollamaPortTextBox"] = "ollama_port", ["ollamaModelTextBox"] = "ollama_model",
        ["ollamaTemperatureTextBox"] = "ollama_temperature", ["ollamaTopPTextBox"] = "ollama_top_p", ["ollamaTopKTextBox"] = "ollama_top_k",
        ["lmstudioUrlTextBox"] = "lm_studio_url", ["lmstudioPortTextBox"] = "lm_studio_port", ["lmstudioModelTextBox"] = "lm_studio_model",
        ["googleTranslateServiceTypeComboBox"] = "google_translate_use_cloud_api",
        ["googleTranslateMappingCheckBox"] = "google_translate_auto_map_languages",
        ["googleFreeFallbackComboBox"] = "google_free_fallback_service", ["microsoftLegacyModeCheckBox"] = "microsoft_legacy_signature_mode"
    };
    private static readonly Dictionary<string, string> ServiceKeyInputs = new()
    {
        ["geminiApiKeyPasswordBox"] = "Gemini", ["groqApiKeyPasswordBox"] = "Groq",
        ["mistralApiKeyPasswordBox"] = "Mistral", ["chatGptApiKeyPasswordBox"] = "ChatGPT",
        ["customApiKeyPasswordBox"] = "Custom API", ["googleTranslateApiKeyPasswordBox"] = "Google Translate",
        ["microsoftLegacyKeyPasswordBox"] = "Microsoft"
    };

    private void InitializeServiceDraftEvents()
    {
        foreach (string name in new[] { "geminiModelComboBox", "groqModelComboBox", "mistralModelComboBox", "chatGptModelComboBox" })
            if (FindName(name) is ComboBox combo)
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ServiceFieldChanged(combo, false)), true);
    }

    private static void SelectServiceValue(ComboBox combo, string value)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            (item.Tag?.ToString() ?? item.Content?.ToString()) == value);
        if (combo.SelectedItem == null) combo.Text = value;
    }

    private void RefreshServiceDraft()
    {
        if (!_initializeRuntime || _savingServiceDraft) return;
        var current = ConfigManager.Instance.ReadServicePreferences();
        _observedServicePreferences = current;
        if (_serviceDraft == null) _serviceDraft = new(current);
        else _serviceDraft.Observe(current);
        _syncingServiceDraft = true;
        try
        {
            SelectServiceValue(translationServiceComboBox, _serviceDraft.Service);
            foreach (var field in ServiceFields)
            {
                string value = _serviceDraft[field.Value];
                switch (FindName(field.Key))
                {
                    case TextBox text: text.Text = value; break;
                    case CheckBox check: check.IsChecked = value == "true"; break;
                    case ComboBox combo when field.Key == "googleTranslateServiceTypeComboBox":
                        combo.SelectedIndex = value == "true" ? 1 : 0; break;
                    case ComboBox combo: SelectServiceValue(combo, value); break;
                }
            }
            foreach (var input in ServiceKeyInputs)
                if (FindName(input.Key) is PasswordBox box) box.Password = _serviceDraft.PendingKey(input.Value);
            HydrateServicePrompt();
            UpdateServiceSpecificSettings(_serviceDraft.Service);
        }
        finally { _syncingServiceDraft = false; }
        RenderServiceDraft();
    }

    private void ServiceChoiceChanged()
    {
        if (!CanEditServiceDraft || translationServiceComboBox.SelectedItem is not ComboBoxItem item) return;
        _serviceDraft!.Set("translation_service", item.Content?.ToString() ?? "");
        _serviceDraftStatus = null;
        RefreshServiceDraft(); // Observe preserves a dirty draft, including providers no longer shown.
        ServiceDraftEdited();
    }

    private void ServiceFieldChanged(object sender, bool selection)
    {
        if (!CanEditServiceDraft || sender is not FrameworkElement control || !ServiceFields.TryGetValue(control.Name, out string? key)) return;
        string value = control switch
        {
            TextBox text => text.Text,
            CheckBox check => check.IsChecked == true ? "true" : "false",
            ComboBox combo when control.Name == "googleTranslateServiceTypeComboBox" => combo.SelectedIndex == 1 ? "true" : "false",
            ComboBox combo when selection && combo.SelectedItem is ComboBoxItem item => item.Tag?.ToString() ?? item.Content?.ToString() ?? "",
            ComboBox combo => combo.Text,
            _ => ""
        };
        _serviceDraft!.Set(key, value);
        if (key == "google_translate_use_cloud_api") UpdateServiceSpecificSettings(_serviceDraft.Service);
        ServiceDraftEdited();
    }

    private void HydrateServicePrompt()
    {
        if (_serviceDraft == null) return;
        bool syncing = _syncingServiceDraft;
        _syncingServiceDraft = true;
        try
        {
            promptTemplateTextBox.Text = ServicePreferenceDraft.UsesPrompt(_serviceDraft.Service)
                ? _serviceDraft[ServicePreferenceDraft.PromptKey(_serviceDraft.Service)] : "";
        }
        finally { _syncingServiceDraft = syncing; }
    }

    private void ServicePrompt_TextChanged(object sender, TextChangedEventArgs e) => StageServicePrompt();
    private void StageServicePrompt()
    {
        if (!CanEditServiceDraft || !ServicePreferenceDraft.UsesPrompt(_serviceDraft!.Service)) return;
        _serviceDraft.Set(ServicePreferenceDraft.PromptKey(_serviceDraft.Service), promptTemplateTextBox.Text);
        ServiceDraftEdited();
    }

    private void ServiceKeyInputChanged(object sender)
    {
        if (!CanEditServiceDraft || sender is not PasswordBox box || !ServiceKeyInputs.TryGetValue(box.Name, out string? service)) return;
        _serviceDraft!.SetPendingKey(service, box.Password);
        ServiceDraftEdited();
    }

    private void AddServiceDraftKey(object sender)
    {
        if (!CanEditServiceDraft) return;
        string? service = (sender as FrameworkElement)?.Tag?.ToString();
        if (sender is PasswordBox box) ServiceKeyInputs.TryGetValue(box.Name, out service);
        if (service == null || !ServicePreferenceDraft.KeyPrefixes.ContainsKey(service)) return;
        _serviceDraft!.AddPendingKey(service);
        RefreshServiceDraft();
        ServiceDraftEdited();
    }

    private void ShowServiceDraftKeys(object sender)
    {
        if (!CanEditServiceDraft || sender is not FrameworkElement control || control.Tag is not string service) return;
        var window = new ApiKeysWindow(service, _serviceDraft!.Keys(service), keys =>
        {
            _serviceDraft.SetKeys(service, keys);
            ServiceDraftEdited();
        }) { Owner = this };
        window.ShowDialog();
    }

    private void ServiceDraftEdited()
    {
        _serviceDraftStatus = null;
        RenderServiceDraft();
        if (_serviceDraft?.IsDirty == false) CompleteServiceAction();
    }

    private void RenderServiceDraft()
    {
        if (_serviceDraft == null || serviceDraftStatusText == null) return;
        // Re-read legacy prompt files on hydration/save, not on every keypress.
        var current = _observedServicePreferences ?? ConfigManager.Instance.ReadServicePreferences();
        bool conflict = _serviceDraft.HasConflict(current);
        var candidate = _serviceDraft.Candidate();
        string? issue = ServicePreferenceDraft.Validate(candidate, _serviceDraft.Baseline);
        serviceKeysSummaryText.Visibility = ServicePreferenceDraft.KeyPrefixes.ContainsKey(_serviceDraft.Service) ? Visibility.Visible : Visibility.Collapsed;
        if (ServicePreferenceDraft.KeyPrefixes.TryGetValue(_serviceDraft.Service, out string? keyPrefix))
            UiText.Bind(serviceKeysSummaryText, TextBlock.TextProperty, "Draft_ServiceKeysCount",
                candidate[keyPrefix + "_api_keys"].Split("+++", StringSplitOptions.RemoveEmptyEntries).Length);
        string status = conflict ? "Draft_ServiceConflict" : _pendingServiceAction != null ? "Draft_ServiceDecision"
            : issue ?? _serviceDraftStatus ?? (_serviceDraft.IsDirty ? "Draft_ServiceDirty" : "Draft_ServiceClean");
        UiText.Bind(serviceDraftStatusText, TextBlock.TextProperty, status);
        serviceDraftStatusText.SetResourceReference(TextBlock.ForegroundProperty, issue != null ? "DangerBrush" : conflict || _pendingServiceAction != null ? "WarningBrush" : "MutedBrush");
        saveServiceDraftButton.IsEnabled = _serviceDraft.IsDirty && !conflict && issue == null && !_savingServiceDraft;
        cancelServiceDraftButton.IsEnabled = _serviceDraft.IsDirty && !_savingServiceDraft;
        stayWithServiceDraftButton.Visibility = _pendingServiceAction != null ? Visibility.Visible : Visibility.Collapsed;
        testModelButton.IsEnabled = !_serviceDraft.IsDirty && issue == null;
    }

    private void SaveServiceDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceDraft == null || !_serviceDraft.IsDirty || _savingServiceDraft) return;
        var candidate = _serviceDraft.Candidate();
        if (ServicePreferenceDraft.Validate(candidate, _serviceDraft.Baseline) != null) { RenderServiceDraft(); return; }
        bool saved = false;
        _savingServiceDraft = true;
        try
        {
            saved = ConfigManager.Instance.TrySaveServicePreferences(_serviceDraft.Baseline, candidate, out bool conflict);
            if (saved)
            {
                _serviceDraft.Reset(ConfigManager.Instance.ReadServicePreferences());
                _serviceDraftStatus = "Draft_ServiceSaved";
                MainWindow.ServicePreferencesCommitted();
            }
            else _serviceDraftStatus = conflict ? "Draft_ServiceConflict" : "Draft_ServiceFailed";
        }
        finally { _savingServiceDraft = false; RefreshServiceDraft(); }
        if (saved) CompleteServiceAction();
    }

    private void CancelServiceDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceDraft == null || _savingServiceDraft) return;
        _serviceDraft.Reset(ConfigManager.Instance.ReadServicePreferences());
        _serviceDraftStatus = "Draft_ServiceCancelled";
        RefreshServiceDraft();
        CompleteServiceAction();
    }

    private void StayWithServiceDraft_Click(object sender, RoutedEventArgs e)
    {
        _pendingServiceAction = null; _serviceDraftStatus = null;
        RenderServiceDraft(); translationServiceComboBox.Focus();
    }

    private void RequestServiceDecision(Action action)
    {
        if (_serviceDraft?.IsDirty != true) { action(); return; }
        _pendingServiceAction = action;
        SelectSettingsCategory(serviceSettingsCategory);
        RenderServiceDraft();
        if (saveServiceDraftButton.IsEnabled) saveServiceDraftButton.Focus();
        else cancelServiceDraftButton.Focus();
    }

    private void CompleteServiceAction()
    {
        var action = _pendingServiceAction; _pendingServiceAction = null;
        RenderServiceDraft(); action?.Invoke();
    }

    private void RequestSettingsDecision(Action action) => RequestGeneralDecision(() => RequestServiceDecision(() => RequestCaptureDecision(() => RequestResultDecision(() => RequestRemainingDecision(true, () => RequestRemainingDecision(false, action))))));
}
