using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// First-run welcome screen. Only asks for what a new user must decide (the language pair);
    /// everything else runs on defaults and stays configurable in Settings.
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        // Common game → reader language pairs offered as one-click presets
        private static readonly (string Source, string Target)[] CommonPairs =
        {
            ("en", "tr"), ("ja", "tr"), ("ja", "en"), ("ko", "en")
        };

        private bool _isLoading = true;

        /// <summary>
        /// True when the user closed the window with "Open Settings" and the caller should open the Settings window.
        /// </summary>
        public bool OpenSettingsRequested { get; private set; }

        public WelcomeWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => DpiHelper.FitWindowToWorkArea(this, center: true);

            // Language lists are copied from the main window so this screen offers exactly the codes OCR/translation use
            LoadInterfaceLanguages();
            PopulateLanguageComboBox(SourceLanguageComboBox, MainWindow.Instance.sourceLanguageComboBox, ConfigManager.Instance.GetSourceLanguage());
            PopulateLanguageComboBox(TargetLanguageComboBox, MainWindow.Instance.targetLanguageComboBox, ConfigManager.Instance.GetTargetLanguage());
            BuildCommonPairButtons();
            Win10WindowHint.Visibility = MainWindow.Instance.IsWindows10 ? Visibility.Visible : Visibility.Collapsed;
            UpdateFormattedTexts();

            LocalizationManager.Instance.PropertyChanged += LocalizationManager_PropertyChanged;
            ApplicationPreferences.Changed += ApplicationPreferences_Changed;
            Closed += (s, e) =>
            {
                LocalizationManager.Instance.PropertyChanged -= LocalizationManager_PropertyChanged;
                ApplicationPreferences.Changed -= ApplicationPreferences_Changed;
                // Shown once automatically; it stays reachable from the Quick Start button
                ConfigManager.Instance.SetNeedShowQuickStart(false);
            };

            _isLoading = false;
        }

        private void LoadInterfaceLanguages()
        {
            InterfaceLanguageOptions.Populate(InterfaceLanguageComboBox);
            RefreshInterfacePreference();
        }

        private void RefreshInterfacePreference()
        {
            bool loading = _isLoading;
            _isLoading = true;
            try { InterfaceLanguageOptions.SelectPreference(InterfaceLanguageComboBox, ApplicationPreferences.LanguagePreference); }
            finally { _isLoading = loading; }
        }

        private void ApplicationPreferences_Changed(object? sender, EventArgs e)
        {
            RefreshInterfacePreference();
            UpdateFormattedTexts();
        }

        private static void PopulateLanguageComboBox(ComboBox target, ComboBox source, string selectedCode)
        {
            var codes = source.Items.OfType<ComboBoxItem>()
                .Select(item => item.Content?.ToString())
                .Where(code => !string.IsNullOrEmpty(code))
                .Select(code => code!);

            foreach (var (code, name) in codes.Select(code => (code, GetLanguageDisplayName(code))).OrderBy(x => x.Item2))
            {
                var item = new ComboBoxItem { Content = name, Tag = code };
                target.Items.Add(item);
                if (string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    target.SelectedItem = item;
                }
            }
        }

        private void BuildCommonPairButtons()
        {
            foreach (var (source, target) in CommonPairs)
            {
                // Skip presets whose languages are not offered (keeps this list valid if the main lists change)
                if (FindItem(SourceLanguageComboBox, source) == null || FindItem(TargetLanguageComboBox, target) == null)
                    continue;

                var button = new Button
                {
                    Content = $"{GetLanguageShortName(source)} → {GetLanguageShortName(target)}",
                    Tag = (source, target),
                    Style = (Style)FindResource("PairButton")
                };
                button.Click += CommonPairButton_Click;
                CommonPairsPanel.Children.Add(button);
            }
        }

        private void CommonPairButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ValueTuple<string, string> pair })
            {
                SourceLanguageComboBox.SelectedItem = FindItem(SourceLanguageComboBox, pair.Item1);
                TargetLanguageComboBox.SelectedItem = FindItem(TargetLanguageComboBox, pair.Item2);
            }
        }

        private static ComboBoxItem? FindItem(ComboBox comboBox, string code) =>
            comboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, code, StringComparison.OrdinalIgnoreCase));

        private static string GetLanguageDisplayName(string code) => LanguageDisplay.GetDisplayName(code);

        private static string GetLanguageShortName(string code) => LanguageDisplay.GetShortName(code);

        private void LocalizationManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LocalizationManager.Strings)) return;
            foreach (var combo in new[] { SourceLanguageComboBox, TargetLanguageComboBox })
                foreach (var item in combo.Items.OfType<ComboBoxItem>())
                    item.Content = GetLanguageDisplayName((string)item.Tag);
            CommonPairsPanel.Children.Clear();
            BuildCommonPairButtons();
            UpdateFormattedTexts();
        }

        /// <summary>
        /// Texts that embed other values (current services, button labels) cannot use plain bindings.
        /// </summary>
        private void UpdateFormattedTexts()
        {
            var strings = LocalizationManager.Instance.Strings;

            ReadyText.Text = string.Format(strings["Welcome_ReadyDesc"],
                ConfigManager.Instance.GetOcrMethod(),
                UiText.Service(ConfigManager.Instance.GetCurrentTranslationService()),
                strings["Btn_Settings"]);

            NextStepsText.Text = string.Format(strings["Welcome_NextDesc"],
                strings["Btn_SelectArea"], strings["Btn_SelectWindow"], strings["Main_Start"],
                strings["Btn_Overlay"], strings["Btn_ChatBox"]);

            ReopenHintText.Text = string.Format(strings["Welcome_ReopenHint"], strings["Lbl_QuickStart"], strings["Main_More"]);

            bool hasWindow = MainWindow.Instance.IsCapturingGameWindow;
            SelectedWindowText.Text = hasWindow
                ? string.Format(strings["Welcome_WindowSelected"], MainWindow.Instance.CapturedWindowTitle)
                : strings["Welcome_NoWindowSelected"];
            SelectedWindowText.Foreground = (System.Windows.Media.Brush)FindResource(hasWindow ? "SuccessBrush" : "MutedBrush");
        }

        private void ChooseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new WindowSelectorPopup { Owner = this };
            popup.WindowSelected += (handle, title) => MainWindow.Instance.SelectGameWindow(handle, title);
            popup.ShowDialog();
            UpdateFormattedTexts();
        }

        private void InterfaceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || InterfaceLanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string languageCode })
                return;

            ApplicationPreferences.SetLanguagePreference(languageCode);
        }

        private void SaveLanguages()
        {
            if (SourceLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string source })
            {
                ConfigManager.Instance.SetSourceLanguage(source);
            }
            if (TargetLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string target })
            {
                ConfigManager.Instance.SetTargetLanguage(target);
            }
        }

        private void GetStartedButton_Click(object sender, RoutedEventArgs e)
        {
            SaveLanguages();
            DialogResult = true;
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveLanguages();
            OpenSettingsRequested = true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
