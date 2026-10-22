using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    internal void ShowTranslationFor(Window owner)
    {
        ShowFor(owner);
        if (!ReferenceEquals(settingsCategories.SelectedItem, serviceSettingsCategory))
            RequestSettingsDecision(() => SelectSettingsCategory(serviceSettingsCategory));
    }

    private void UseGameAi_Click(object sender, RoutedEventArgs e) => SelectGameEngine(true);
    private void UseGameWeb_Click(object sender, RoutedEventArgs e) => SelectGameEngine(false);

    private void SelectGameEngine(bool localAi)
    {
        if (!CanEditServiceDraft) return;
        _serviceDraft!.SelectGameEngine(localAi);
        RefreshServiceDraft();
        ServiceDraftEdited();
    }

    private void RenderGameEngineEditor()
    {
        if (_serviceDraft == null || gameEngineNameText == null) return;
        bool local = HyMt2Profile.IsSelected(_serviceDraft.Service, _serviceDraft["ollama_model"]);
        bool web = _serviceDraft.Service == "Google Translate" && _serviceDraft["google_translate_use_cloud_api"] != "true";
        UiText.Bind(gameEngineNameText, TextBlock.TextProperty, local ? "Flow_EngineLocal" : web ? "Flow_EngineWeb" : "Flow_EngineUnsupported");
        UiText.Bind(gameEngineHelpText, TextBlock.TextProperty, local ? "Settings_GameLocalHint" : web ? "Settings_GameWebHint" : "Settings_GameUnsupportedHint");
        gameModelDownloadButton.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        gameOllamaInstallLink.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        gameModelDownloadButton.IsEnabled = !_serviceDraft.IsDirty && !_savingServiceDraft;
        useGameAiButton.IsEnabled = !local && !_savingServiceDraft;
        useGameWebButton.IsEnabled = !web && !_savingServiceDraft;
        foreach (var item in translationServiceComboBox.Items.OfType<ComboBoxItem>())
        {
            string service = item.Content?.ToString() ?? "";
            bool supported = service is "Ollama" or "Google Translate";
            item.Visibility = supported || service == _serviceDraft.Service ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = supported;
        }
        // A fixed translation profile does not consume the generic prompt or sampling fields.
        if (local)
        {
            promptOptionsExpander.Visibility = Visibility.Collapsed;
            modelSamplingExpander.Visibility = Visibility.Collapsed;
        }
        googleFreeFallbackLabel.Visibility = googleFreeFallbackComboBox.Visibility = Visibility.Collapsed;
        googleTranslateApiKeyLabel.Visibility = googleTranslateApiKeyGrid.Visibility = Visibility.Collapsed;
        ollamaModelTextBox.IsReadOnly = local;
        viewModelsButton.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
    }
}
