using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay
{
    public partial class ApiKeysWindow : Window
    {
        private ObservableCollection<string> _apiKeys = new ObservableCollection<string>();
        private string _serviceType;
        private readonly Action<List<string>>? _updateDraft;
        private readonly List<string> _actualKeys;
        
        public ApiKeysWindow(string serviceType, List<string> apiKeys, Action<List<string>>? updateDraft = null)
        {
            InitializeComponent();
            
            _serviceType = serviceType;
            _updateDraft = updateDraft;
            _actualKeys = apiKeys.Where(key => !string.IsNullOrEmpty(key)).ToList();
            UiText.Bind(this, Window.TitleProperty, "ApiKeys_ServiceTitle", _serviceType);
            
            // Populate the list with existing keys
            foreach (var key in apiKeys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    // Mask API keys for display
                    string maskedKey = MaskApiKey(key);
                    _apiKeys.Add(maskedKey);
                }
            }
            
            // Set the ListView's ItemsSource
            apiKeysListView.ItemsSource = _apiKeys;
        }
        
        // Mask API key for display (show only first 4 and last 4 characters)
        private string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            {
                return new string('•', apiKey?.Length ?? 0);
            }
            
            return apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
        }
        
        // Clear selected API key
        private void ClearSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (apiKeysListView.SelectedItem != null)
            {
                int selectedIndex = apiKeysListView.SelectedIndex;
                
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_ConfirmRemoveKey"], 
                    LocalizationManager.Instance.Strings["Title_ConfirmRemoval"], 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Remove the key from the list
                    _apiKeys.RemoveAt(selectedIndex);
                    
                    // Save the updated list
                    if (selectedIndex < _actualKeys.Count) _actualKeys.RemoveAt(selectedIndex);
                    PublishKeys();
                }
            }
        }
        
        // Clear all API keys
        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiKeys.Count > 0)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ConfirmRemoveAllKeys"], UiText.Service(_serviceType)),
                    LocalizationManager.Instance.Strings["Title_ConfirmRemoval"], 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Clear the list
                    _apiKeys.Clear();
                    
                    // Save the empty list
                    _actualKeys.Clear();
                    PublishKeys();
                }
            }
        }
        
        private void PublishKeys()
        {
            if (_updateDraft != null) _updateDraft(new List<string>(_actualKeys));
            else ConfigManager.Instance.SaveApiKeysList(_serviceType, new List<string>(_actualKeys));
        }

        // Close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}