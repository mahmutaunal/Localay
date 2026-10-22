using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private Action? _pendingProfileAction;
    private void ProfileSelection_Changed(object sender, RoutedEventArgs e) => UpdateProfileActions();
    private void UpdateProfileActions()
    {
        if (createGameProfileButton == null || profileComboBox == null) return;
        bool pending = _pendingProfileAction != null;
        string name = profileNameTextBox.Text.Trim();
        bool exists = _initializeRuntime && ProfileDocument.ValidName(name) && File.Exists(Path.Combine(ConfigManager.Instance._profileFolderPath, name + ".txt"));
        createGameProfileButton.IsEnabled = !pending && ProfileDocument.ValidName(name) && !exists;
        bool selected = profileComboBox.SelectedItem is string saved && ProfileDocument.ValidName(saved);
        loadGameProfileButton.IsEnabled = updateGameProfileButton.IsEnabled = removeGameProfileButton.IsEnabled = !pending && selected;
        profileNameTextBox.IsEnabled = profileComboBox.IsEnabled = !pending;
    }
    private void RequestProfileOperation(string operation)
    {
        string? name = operation == "create" ? profileNameTextBox.Text.Trim() : profileComboBox.SelectedItem as string;
        if (name == null || !ProfileDocument.ValidName(name)) { SetProfileStatus("Draft_ProfileNameInvalid"); return; }
        RequestSettingsDecision(() =>
        {
            if (operation == "create") { ExecuteProfileOperation(operation, name); return; }
            _pendingProfileAction = () => ExecuteProfileOperation(operation, name);
            UiText.Bind(statusUpdateGameProfile, TextBlock.TextProperty, operation switch
            {
                "load" => "Draft_ProfileLoadDecision", "update" => "Draft_ProfileUpdateDecision", _ => "Draft_ProfileRemoveDecision"
            }, name);
            profileDecisionPanel.Visibility = Visibility.Visible;
            UpdateProfileActions();
        });
    }
    private void ConfirmProfileOperation_Click(object sender, RoutedEventArgs e)
    {
        var action = _pendingProfileAction;
        _pendingProfileAction = null; profileDecisionPanel.Visibility = Visibility.Collapsed;
        action?.Invoke(); UpdateProfileActions();
    }
    private void CancelProfileOperation_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingProfile(); SetProfileStatus("Draft_ProfileCancelled");
    }
    private void CancelPendingProfile()
    {
        _pendingProfileAction = null;
        profileDecisionPanel.Visibility = Visibility.Collapsed; UpdateProfileActions();
    }
    private void SetProfileStatus(string key, params object[] args)
    {
        UiText.Bind(statusUpdateGameProfile, TextBlock.TextProperty, key, args);
        statusUpdateGameProfile.Visibility = Visibility.Visible;
    }
    private void ExecuteProfileOperation(string operation, string name)
    {
        var config = ConfigManager.Instance;
        var captureBefore = config.ReadCapturePreferences();
        var appearanceBefore = config.ReadResultPreferences();
        bool success = operation switch
        {
            "create" => config.TryWriteGameProfile(name, false), "update" => config.TryWriteGameProfile(name, true),
            "remove" => config.TryRemoveGameProfile(name), "load" => config.TryLoadGameProfile(name), _ => false
        };
        if (!success) { SetProfileStatus("Draft_ProfileFailed"); UpdateProfileActions(); return; }
        if (operation == "load")
        {
            MainWindow.AdvancedPreferencesCommitted();
            MainWindow.CapturePreferencesCommitted(captureBefore, config.ReadCapturePreferences(), true);
            MainWindow.ResultPreferencesCommitted(appearanceBefore, config.ReadResultPreferences());
            ReloadSetting();
        }
        LoadAllProfile();
        if (operation != "remove") profileComboBox.SelectedItem = name;
        SetProfileStatus(operation switch
        {
            "create" => "Profile_Created", "update" => "Profile_Updated", "remove" => "Profile_Removed", _ => "Draft_ProfileLoaded"
        }, name);
        UpdateProfileActions();
    }
}
