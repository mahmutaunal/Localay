using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private void InitializeDesignNavigation()
    {
        UiText.Bind(settingsVersionText, TextBlock.TextProperty, "About_Version", AppRelease.Version);
        automaticUpdatesCheckBox.IsChecked = UpdateCoordinator.Automatic;
    }

    internal static void RefreshDiagnosticState(bool visible)
    {
        if (_instance == null) return;
        UiText.Bind(_instance.diagnosticsStatusText, TextBlock.TextProperty, visible ? "Btn_On" : "Btn_Off");
    }

    private void DiagnosticsFromSettings_Click(object sender, RoutedEventArgs e) => MainWindow.Instance.OpenDiagnostics();
    private void AboutFromSettings_Click(object sender, RoutedEventArgs e) => MainWindow.Instance.OpenAbout();
    private void ResetFromSettings_Click(object sender, RoutedEventArgs e) => MainWindow.Instance.ResetFromSettings();

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) => await UpdateCoordinator.CheckAsync(true);

    private void AutomaticUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateCoordinator.SetAutomatic(automaticUpdatesCheckBox.IsChecked == true);
        automaticUpdatesCheckBox.IsChecked = UpdateCoordinator.Automatic;
    }

    internal static void RequestUpdateInstall(Action install)
    {
        if (_instance == null) { install(); return; }
        var settings = _instance;
        settings.closeSettingsButton.Focus(); // Capture edits committed on focus loss first.
        bool dirty = settings._generalDraft?.IsDirty == true || settings._serviceDraft?.IsDirty == true ||
            settings.HasCaptureEdits || settings._resultDraft?.IsDirty == true ||
            settings._advancedDraft?.IsDirty == true || settings._shortcutDraft?.IsDirty == true;
        if (dirty) { settings.Show(); settings.Activate(); }
        settings.RequestSettingsDecision(install);
    }

}
