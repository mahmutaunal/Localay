using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Diagnostics;
using System.Collections.ObjectModel;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Interop;
using RST;
using System.IO;

namespace AlpWareStudio.Localay
{
    // Class to represent an ignore phrase
    public enum IgnorePhraseMatchType
    {
        ExactMatch,
        Contains,
        RegularExpression
    }

    public class IgnorePhrase
    {
        public string Phrase { get; set; } = string.Empty;
        public IgnorePhraseMatchType MatchType { get; set; } = IgnorePhraseMatchType.ExactMatch;

        public IgnorePhrase(string phrase, IgnorePhraseMatchType matchType)
        {
            Phrase = phrase;
            MatchType = matchType;
        }
    }

    // One row of the exclude region list in Settings
    public class ExcludeRegionItem
    {
        public ExcludeRegionItem(int index, string text)
        {
            Index = index;
            Text = text;
        }

        public int Index { get; }
        public string Text { get; }
    }

    // Entry of the ignore-phrase match type dropdowns; the label follows the interface language
    public class MatchTypeOption : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly string _labelKey;

        public MatchTypeOption(IgnorePhraseMatchType value, string labelKey)
        {
            Value = value;
            _labelKey = labelKey;
        }

        public IgnorePhraseMatchType Value { get; }
        public string Label => LocalizationManager.Instance[_labelKey];

        public void RefreshText() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Label)));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    // One row of the hotkey table in the HotKeys tab
    public class HotkeySettingRow : System.ComponentModel.INotifyPropertyChanged
    {
        private string _hotkey = "";
        private bool _isCapturing;
        private string? _captureText;
        private string? _conflictText;

        public HotkeySettingRow(string functionName, string labelKey, string mainWindowLabelName)
        {
            FunctionName = functionName;
            LabelKey = labelKey;
            MainWindowLabelName = mainWindowLabelName;
        }

        public string FunctionName { get; }
        public string LabelKey { get; }
        public string MainWindowLabelName { get; }

        public string Label => LocalizationManager.Instance[LabelKey];

        public string Hotkey
        {
            get => _hotkey;
            set { _hotkey = value; Notify(nameof(Hotkey), nameof(HasHotkey), nameof(DisplayText), nameof(ClearVisibility)); }
        }

        public bool HasHotkey => !string.IsNullOrEmpty(_hotkey);

        public bool IsCapturing
        {
            get => _isCapturing;
            set { _isCapturing = value; Notify(nameof(IsCapturing), nameof(DisplayText), nameof(ClearVisibility)); }
        }

        // Partial combination shown while recording, e.g. "Ctrl + Alt + ..."
        public string? CaptureText
        {
            get => _captureText;
            set { _captureText = value; Notify(nameof(CaptureText), nameof(DisplayText)); }
        }

        public string DisplayText =>
            IsCapturing ? (CaptureText ?? LocalizationManager.Instance["Lbl_HotKeyPress"])
            : HasHotkey ? KeyboardShortcuts.FormatForDisplay(_hotkey)
            : LocalizationManager.Instance["Lbl_HotKeyNotSet"];

        public Visibility ClearVisibility => HasHotkey && !IsCapturing ? Visibility.Visible : Visibility.Hidden;

        public string? ConflictText
        {
            get => _conflictText;
            set { _conflictText = value; Notify(nameof(ConflictText), nameof(ConflictVisibility)); }
        }

        public Visibility ConflictVisibility => string.IsNullOrEmpty(_conflictText) ? Visibility.Collapsed : Visibility.Visible;

        // Re-read localized texts after a language change
        public void RefreshText() => Notify(nameof(Label), nameof(DisplayText));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void Notify(params string[] names)
        {
            foreach (string name in names)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            }
        }
    }

    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;
        private readonly bool _initializeRuntime;
        private bool _allowClose;
        private bool _hasBeenShown;


        public string profileName = "";

        public static SettingsWindow Instance
        {
            get
            {
                if (_instance == null || !IsWindowValid(_instance))
                {
                    _instance = new SettingsWindow();
                }
                return _instance;
            }
        }

        public SettingsWindow() : this(initializeRuntime: true) { }

        // Load the complete production BAML in a bare WPF host without starting capture,
        // creating MainWindow/ConfigManager, or accessing the user's profiles.
        internal SettingsWindow(bool initializeRuntime)
        {
            _initializeRuntime = initializeRuntime;
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("SettingsWindow constructor: Setting _isInitializing to true");

            InitializeComponent();
            InitializeDesignNavigation();
            InitializeResultEditor();
            var languageTemplate = (DataTemplate)FindResource("SettingsLanguageTemplate");
            foreach (var combo in new[] { sourceLanguageComboBox, targetLanguageComboBox })
                foreach (var item in combo.Items.OfType<ComboBoxItem>()) item.ContentTemplate = languageTemplate;
            InitializeHotkeyRows();
            InitializeServiceDraftEvents();
            InitializeRemainingEditors();
            if (!initializeRuntime) return;

            InitializeGeneralPreferences();
            foreach (var item in ocrMethodComboBox.Items.OfType<ComboBoxItem>())
                if (item.Content?.ToString() == "OneOCR") item.IsEnabled = NativeOcrAssets.IsAvailable;
            LocalizationManager.Instance.PropertyChanged += LocalizationChanged;
            MainWindow.Instance.ExcludeRegionsChanged += ExcludeRegionsChanged;
            ApplicationPreferences.Changed += GeneralPreferencesChanged;
            Loaded += SettingsWindow_Loaded;
            IsVisibleChanged += SettingsVisibilityChanged;
        }

        private void LocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LocalizationManager.Strings)) return;
            foreach (var row in _hotkeyRows) row.RefreshText();
            UpdateHotkeyConflicts();
            MatchTypeOptions.ForEach(option => option.RefreshText());
            LoadExcludeRegions();
            RenderGeneralDraft();
            RenderCaptureDraft();
            RenderResultDraft();
            RenderRemainingDraft(true); RenderRemainingDraft(false);
        }

        private void ExcludeRegionsChanged(object? sender, EventArgs e) => LoadExcludeRegions();

        private void GeneralPreferencesChanged(object? sender, EventArgs e)
        {
            RefreshGeneralPreferences();
            bool wasInitializing = _isInitializing;
            try
            {
                _isInitializing = true;
                LoadAvailableScreens();
            }
            finally { _isInitializing = wasInitializing; }
        }

        private void SettingsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            MainWindow.Instance.UpdateSettingsButtonState(IsVisible);
            if (IsVisible) RefreshGeneralPreferences();
            else { StopHotkeyCapture(); CancelPendingProfile(); }
        }

        internal void ShowFor(Window owner)
        {
            try
            {
                if (!IsVisible) ReloadSetting();
                Owner = owner.IsVisible && owner.WindowState != WindowState.Minimized ? owner : null;
                if (!_hasBeenShown)
                    WindowStartupLocation = Owner == null
                        ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                DpiHelper.FitWindowToWorkArea(this);
                Activate();
                _hasBeenShown = true;
            }
            catch
            {
                Hide();
                throw;
            }
        }

        internal static void ReloadIfVisible()
        {
            if (_instance?.IsVisible == true) _instance.ReloadSetting();
        }

        internal static void CloseIfCreated() => _instance?.ClosePermanently();

        internal void ClosePermanently()
        {
            if (_initializeRuntime && IsVisible) closeSettingsButton.Focus();
            _pendingGeneralAction = null; // Application shutdown never applies an unsaved draft.
            _pendingServiceAction = null;
            _pendingCaptureAction = null;
            _pendingResultAction = null;
            _pendingSectionActions.Clear();
            _pendingProfileAction = null;
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_initializeRuntime && !_allowClose)
            {
                e.Cancel = true;
                // Commit fields saved on LostFocus before hiding the persistent window.
                closeSettingsButton.Focus();
                RequestDismiss();
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_initializeRuntime)
            {
                LocalizationManager.Instance.PropertyChanged -= LocalizationChanged;
                MainWindow.Instance.ExcludeRegionsChanged -= ExcludeRegionsChanged;
                ApplicationPreferences.Changed -= GeneralPreferencesChanged;
                StopHotkeyCapture();
                _hotkeyStatusTimer?.Stop();
            }
            if (ReferenceEquals(_instance, this)) _instance = null;
            base.OnClosed(e);
        }

        private bool _syncingGeneralPreferences;

        private void InitializeGeneralPreferences()
        {
            _syncingGeneralPreferences = true;
            InterfaceLanguageOptions.Populate(interfaceLanguageComboBox);
            _syncingGeneralPreferences = false;
            RefreshGeneralPreferences();
        }

        internal static void RefreshGeneralPreferencesIfCreated() => _instance?.RefreshGeneralPreferences();

        private void RefreshGeneralPreferences()
        {
            if (interfaceLanguageComboBox == null || appearanceComboBox == null) return;
            RefreshGeneralDraft();
        }

        private void InterfaceLanguagePreference_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _syncingGeneralPreferences || _generalDraft == null ||
                SelectedTag(interfaceLanguageComboBox) is not string code) return;
            _generalDraft.SetLanguage(code);
            GeneralDraftEdited();
        }

        private void AppearancePreference_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _syncingGeneralPreferences || _generalDraft == null ||
                SelectedTag(appearanceComboBox) is not string theme) return;
            _generalDraft.SetTheme(theme);
            GeneralDraftEdited();
        }

        private void QuickStartFromSettings_Click(object sender, RoutedEventArgs e)
        {
            RequestSettingsDecision(() =>
            {
                Hide();
                MainWindow.Instance.OpenQuickStart();
            });
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e) => Close();

        // Reload setting for setting windows
        public void ReloadSetting()
        {
            _instance = this;
            LoadSettings();
            ApplicationPreferences.Refresh();
            RefreshGeneralPreferences();
        }

        // Flag to prevent saving during initialization
        private bool _isInitializing = true;

        // Collection to hold the ignore phrases
        private ObservableCollection<IgnorePhrase> _ignorePhrases = new ObservableCollection<IgnorePhrase>();

        public List<MatchTypeOption> MatchTypeOptions { get; } = new List<MatchTypeOption>
        {
            new MatchTypeOption(IgnorePhraseMatchType.ExactMatch, "Cmb_MatchType_ExactMatch"),
            new MatchTypeOption(IgnorePhraseMatchType.Contains, "Cmb_MatchType_Contains"),
            new MatchTypeOption(IgnorePhraseMatchType.RegularExpression, "Cmb_MatchType_RegularExpression"),
        };

        private bool _settingsLoaded;

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs? e)
        {
            if (_settingsLoaded) return;
            try { LoadSettings(); }
            catch (Exception ex)
            {
                Hide();
                SettingsWindowError.Show(MainWindow.Instance, ex);
            }
        }

        private void LoadSettings()
        {
            try
            {
                Console.WriteLine("SettingsWindow_Loaded: Starting initialization");

                // Set initialization flag to prevent saving during setup
                _isInitializing = true;

                LoadAvailableScreens();
                LoadAllProfile();

                // Make sure keyboard shortcuts work from this window too
                PreviewKeyDown -= Application_KeyDown;
                PreviewKeyDown += Application_KeyDown;

                // Set initial values only after the window is fully loaded
                LoadSettingsFromMainWindow();

                // Make sure service-specific settings are properly initialized
                UpdateServiceSpecificSettings(_serviceDraft?.Service ?? ConfigManager.Instance.GetCurrentTranslationService());

                // Make sure button check language package are properly initialize
                string currentOcr = ConfigManager.Instance.GetOcrMethod();
                if (currentOcr != "Windows OCR")
                {
                    checkLanguagePack.Visibility = Visibility.Collapsed;
                    checkLanguagePackButton.Visibility = Visibility.Collapsed;
                }
                Console.WriteLine("Settings window fully loaded and initialized. Changes will now be saved.");
                _settingsLoaded = true;
            }
            finally
            {
                _isInitializing = false; // Ensure we don't get stuck in initialization mode
            }
        }

        // Load list of available screens with display name and resolution
        private void LoadAvailableScreens()
        {
            try
            {
                // Clear existing items
                screenComboBox.Items.Clear();

                // Get all screens
                var screens = System.Windows.Forms.Screen.AllScreens;
                ObserveCaptureScreenTopology(ConfigManager.DescribeCaptureScreens(screens));

                // Add each screen to the combo box
                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];

                    // Get native resolution
                    int width = screen.Bounds.Width;
                    int height = screen.Bounds.Height;

                    // Get friendly display name
                    string displayName = GetFriendlyScreenName(screen.DeviceName);

                    // Create display info
                    string screenInfo = $"{displayName} ({width} x {height})";
                    if (screen.Primary)
                    {
                        screenInfo += " (" + UiText.Get("Lbl_Primary") + ")";
                    }

                    // Create combo box item
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = screenInfo,
                        Tag = i  // Store screen index as Tag
                    };

                    screenComboBox.Items.Add(item);

                    Console.WriteLine($"Screen {i}: {displayName}, {width}x{height}, Primary: {screen.Primary}");
                }

                if (_captureDraft != null) RefreshCaptureDraft();
                else
                {
                    int selectedIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                    screenComboBox.SelectedIndex = selectedIndex >= 0 && selectedIndex < screens.Length ? selectedIndex : -1;
                }

                Console.WriteLine($"Loaded {screenComboBox.Items.Count} screens");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading available screens: {ex.Message}");
            }
        }
        // Get friendly name for a screen device
        private string GetFriendlyScreenName(string deviceName)
        {
            try
            {
                // Extract the device name from the full path (e.g., \\.\DISPLAY1)
                string shortDeviceName = deviceName.Substring(deviceName.LastIndexOf('\\') + 1);

                // Try multiple methods to get the friendly name

                // Method 1: Using EnumDisplayDevices with the device name
                DISPLAY_DEVICE displayDevice = new DISPLAY_DEVICE();
                displayDevice.cb = Marshal.SizeOf(displayDevice);

                if (EnumDisplayDevices(deviceName, 0, ref displayDevice, 0))
                {
                    if (!string.IsNullOrEmpty(displayDevice.DeviceString))
                    {
                        return $"{shortDeviceName} - {displayDevice.DeviceString}";
                    }
                }

                uint deviceIndex = 0;
                DISPLAY_DEVICE device = new DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(device);

                while (EnumDisplayDevices(null, deviceIndex, ref device, 0))
                {
                    if (device.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(device.DeviceString))
                        {
                            return $"{shortDeviceName} - {device.DeviceString}";
                        }
                        break;
                    }

                    // Try to get monitor info for this adapter
                    DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                    monitor.cb = Marshal.SizeOf(monitor);
                    uint monitorIndex = 0;

                    while (EnumDisplayDevices(device.DeviceName, monitorIndex, ref monitor, 0))
                    {
                        if (monitor.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(monitor.DeviceString))
                            {
                                return $"{shortDeviceName} - {monitor.DeviceString}";
                            }
                            break;
                        }
                        monitorIndex++;
                        monitor.cb = Marshal.SizeOf(monitor);
                    }

                    deviceIndex++;
                    device.cb = Marshal.SizeOf(device);
                }

                try
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor"))
                    {
                        foreach (var monitor in searcher.Get())
                        {
                            string monitorName = monitor["Name"]?.ToString() ?? "";
                            string monitorDeviceId = monitor["DeviceID"]?.ToString() ?? "";
                            string monitorDescription = monitor["Description"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(monitorName) && monitorName != "Default Monitor")
                            {
                                return $"{shortDeviceName} - {monitorName}";
                            }
                            else if (!string.IsNullOrEmpty(monitorDescription) && monitorDescription != "Default Monitor")
                            {
                                return $"{shortDeviceName} - {monitorDescription}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting monitor name via WMI: {ex.Message}");
                }

                // Fallback to just the device name if we couldn't get the friendly name
                return shortDeviceName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting friendly screen name: {ex.Message}");
                return deviceName;
            }
        }

        // P/Invoke declarations for getting display device information
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8,
            VGACompatible = 0x16,
            Removable = 0x20,
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000
        }

        // Select screen - placeholder for now
        private void ScreenComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }

        private void ApiKeyPasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            AddServiceDraftKey(sender);
        }

        private void ViewApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            ShowServiceDraftKeys(sender);
        }

        private void SaveApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            AddServiceDraftKey(sender);
        }
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }

        // Google Translate API Key changed
        private void GoogleTranslateApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // Google Translate Service Type changed
        private void GoogleTranslateServiceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }

        // Google Translate language mapping checkbox changed
        private void GoogleTranslateMappingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Google Translate API link click
        private void GoogleTranslateApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cloud.google.com/translate/docs/setup");
        }

        // Helper method to check if a window instance is still valid
        private static bool IsWindowValid(Window window)
        {
            // Check if the window still exists in the application's window collection
            var windowCollection = System.Windows.Application.Current.Windows;
            for (int i = 0; i < windowCollection.Count; i++)
            {
                if (windowCollection[i] == window)
                {
                    return true;
                }
            }
            return false;
        }

        private void LoadSettingsFromMainWindow()
        {
            // XAML owns the handlers; _isInitializing suppresses writes during hydration.
            // Set context settings
            maxContextPiecesTextBox.Text = ConfigManager.Instance.GetMaxContextPieces().ToString();
            minContextSizeTextBox.Text = ConfigManager.Instance.GetMinContextSize().ToString();
            minChatBoxTextSizeTextBox.Text = ConfigManager.Instance.GetChatBoxMinTextSize().ToString();
            gameInfoTextBox.Text = ConfigManager.Instance.GetGameInfo();
            minTextFragmentSizeTextBox.Text = ConfigManager.Instance.GetMinTextFragmentSize().ToString();
            minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString(CultureInfo.InvariantCulture);
            minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString(CultureInfo.InvariantCulture);
            lineSpacingFactorTextBox.Text = ConfigManager.Instance.GetLineSpacingFactor().ToString(CultureInfo.InvariantCulture);

            SelectLanguagesFromConfig();
            ListHotKey_TextChanged();

            // Set text similar threshold from config
            textSimilarThresholdTextBox.Text = ConfigManager.Instance.GetTextSimilarThreshold().ToString(CultureInfo.InvariantCulture);

            // Set char level from config
            charLevelCheckBox.IsChecked = ConfigManager.Instance.IsCharLevelEnabled();

            // Set HDR support
            hdrSupportCheckBox.IsChecked = ConfigManager.Instance.IsHDRSupportEnabled();

            // Set hot key enable
            hotKeyEnableCheckBox.IsChecked = ConfigManager.Instance.IsHotKeyEnabled();

            // Translation speed
            llmPreferSpeedCheckBox.IsChecked = ConfigManager.Instance.IsLlmPreferSpeedEnabled();
            translationCacheCheckBox.IsChecked = ConfigManager.Instance.IsTranslationCacheEnabled();
            translationPrefetchCheckBox.IsChecked = ConfigManager.Instance.IsTranslationPrefetchEnabled();

            RefreshCaptureDraft();
            RefreshResultDraft();
            RefreshRemainingDrafts();

            // Set block detection settings directly from BlockDetectionManager

            blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2", CultureInfo.InvariantCulture);
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2", CultureInfo.InvariantCulture);

            Console.WriteLine($"SettingsWindow: Loaded block detection power: {blockDetectionPowerTextBox.Text}");
            Console.WriteLine($"SettingsWindow: Loaded settle time: {settleTimeTextBox.Text}");

            RefreshServiceDraft();

            // Load ignore phrases
            LoadIgnorePhrases();

            // Load exclude regions
            LoadExcludeRegions();


        }

        // ----- HotKeys tab -----

        // Display order of the hotkey table: function name, label key, name of the matching label in MainWindow
        private static readonly (string Function, string LabelKey, string MainWindowLabel)[] HotkeyRowDefinitions =
        {
            ("Start/Stop", "Lbl_StartStopTranslation", "hotKeyStartStop"),
            ("Overlay", "Lbl_ToggleOverlay", "hotKeyOverlay"),
            ("Setting", "Lbl_OpenSettings", "hotKeySetting"),
            ("Log", "Lbl_ToggleLog", "hotKeyLog"),
            ("Swap Languages", "Lbl_SwapLanguages", "hotKeySwapLanguages"),
            ("Retry Translation", "Lbl_TranslationRetry", "hotKeyRetryTranslate"),
            ("Select Exclude Region", "Lbl_SelectExcludeRegion", "hotKeyToggleExcludeRegions"),
        };

        private readonly ObservableCollection<HotkeySettingRow> _hotkeyRows = new ObservableCollection<HotkeySettingRow>();
        private HotkeySettingRow? _capturingHotkeyRow;
        private System.Windows.Threading.DispatcherTimer? _hotkeyStatusTimer;

        private void InitializeHotkeyRows()
        {
            foreach (var (function, labelKey, mainWindowLabel) in HotkeyRowDefinitions)
            {
                _hotkeyRows.Add(new HotkeySettingRow(function, labelKey, mainWindowLabel));
            }
            hotkeyItemsControl.ItemsSource = _hotkeyRows;
        }

        // Reload all hotkeys from config into the Settings table and the MainWindow shortcut list
        public void ListHotKey_TextChanged()
        {
            foreach (var row in _hotkeyRows)
            {
                row.Hotkey = _shortcutDraft != null ? _shortcutDraft[ConfigManager.Instance.HotkeyPreferenceKey(row.FunctionName)] : ConfigManager.Instance.GetHotKey(row.FunctionName);
            }
            RefreshMainWindowHotkeys();
            UpdateHotkeyConflicts();
        }

        // Startup must not construct Settings just to display shortcuts: a Settings load
        // failure would otherwise abort the remaining MainWindow initialization.
        internal static void RefreshMainWindowHotkeys()
        {
            foreach (var (function, _, label) in HotkeyRowDefinitions)
            {
                string hotkey = ConfigManager.Instance.GetHotKey(function);
                if (MainWindow.Instance.FindName(label) is TextBlock mainWindowLabel)
                    mainWindowLabel.Text = string.IsNullOrEmpty(hotkey) ? "—" : KeyboardShortcuts.FormatForDisplay(hotkey);
            }
        }

        private void UpdateHotkeyConflicts()
        {
            foreach (var row in _hotkeyRows)
            {
                var other = _hotkeyRows.FirstOrDefault(r => r != row && KeyboardShortcuts.AreSameHotkey(r.Hotkey, row.Hotkey));
                row.ConflictText = other == null
                    ? null
                    : string.Format(LocalizationManager.Instance["Lbl_HotKeyConflictWith"], other.Label);
            }
        }

        private void HotkeyCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { DataContext: HotkeySettingRow row } button)
                return;

            bool wasCapturingThisRow = _capturingHotkeyRow == row;
            StopHotkeyCapture();
            if (wasCapturingThisRow)
                return;

            _capturingHotkeyRow = row;
            row.IsCapturing = true;
            KeyboardShortcuts.SuspendHotkeys();
            HideHotkeyStatus();
            System.Windows.Input.Keyboard.Focus(button);
        }

        private void StopHotkeyCapture()
        {
            if (_capturingHotkeyRow == null)
                return;

            _capturingHotkeyRow.IsCapturing = false;
            _capturingHotkeyRow.CaptureText = null;
            _capturingHotkeyRow = null;
            KeyboardShortcuts.ResumeHotkeys();
        }

        private void HotkeyCaptureButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var row = _capturingHotkeyRow;
            if (row == null || (sender as FrameworkElement)?.DataContext != row)
                return;

            // Swallow everything while recording, including Space/Enter that would click the button
            e.Handled = true;

            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;

            if (modifiers == System.Windows.Input.ModifierKeys.None)
            {
                if (key == System.Windows.Input.Key.Escape)
                {
                    StopHotkeyCapture();
                    return;
                }
                if (key == System.Windows.Input.Key.Back || key == System.Windows.Input.Key.Delete)
                {
                    StopHotkeyCapture();
                    AssignHotkey(row, "");
                    return;
                }
            }

            switch (KeyboardShortcuts.TryBuildHotkey(modifiers, key, out string hotkey))
            {
                case KeyboardShortcuts.HotkeyCaptureResult.ModifierOnly:
                    UpdateCapturePreview(row, modifiers);
                    break;
                case KeyboardShortcuts.HotkeyCaptureResult.NeedsModifier:
                    ShowHotkeyStatus(LocalizationManager.Instance["Msg_HotKeyNeedModifier"], isError: true);
                    break;
                case KeyboardShortcuts.HotkeyCaptureResult.Unsupported:
                    ShowHotkeyStatus(LocalizationManager.Instance["Msg_HotKeyUnsupportedKey"], isError: true);
                    break;
                case KeyboardShortcuts.HotkeyCaptureResult.Ok:
                    StopHotkeyCapture();
                    AssignHotkey(row, hotkey);
                    break;
            }
        }

        private void HotkeyCaptureButton_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var row = _capturingHotkeyRow;
            if (row == null || (sender as FrameworkElement)?.DataContext != row)
                return;

            e.Handled = true;
            UpdateCapturePreview(row, System.Windows.Input.Keyboard.Modifiers);
        }

        private static void UpdateCapturePreview(HotkeySettingRow row, System.Windows.Input.ModifierKeys modifiers)
        {
            string prefix = KeyboardShortcuts.FormatModifiers(modifiers, " + ", forDisplay: true);
            row.CaptureText = prefix.Length > 0 ? prefix + "…" : null;
        }

        private void HotkeyCaptureButton_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext == _capturingHotkeyRow)
            {
                StopHotkeyCapture();
            }
        }

        private void ClearHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is HotkeySettingRow row)
            {
                StopHotkeyCapture();
                AssignHotkey(row, "");
            }
        }

        private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
        {
            StopHotkeyCapture();
            if (_shortcutDraft == null) return;
            foreach (var row in _hotkeyRows)
            {
                row.Hotkey = ConfigManager.Instance.GetDefaultHotKey(row.FunctionName);
                _shortcutDraft.Set(ConfigManager.Instance.HotkeyPreferenceKey(row.FunctionName), row.Hotkey);
            }
            UpdateHotkeyConflicts(); RemainingEdited(false);
        }

        // Save a new hotkey for a row ("" clears it), asking before taking the combination from another function
        private void AssignHotkey(HotkeySettingRow row, string hotkey)
        {
            if (_shortcutDraft == null) return;
            row.Hotkey = hotkey;
            _shortcutDraft.Set(ConfigManager.Instance.HotkeyPreferenceKey(row.FunctionName), hotkey);
            UpdateHotkeyConflicts();
            RemainingEdited(false);
        }

        private void ApplyHotkeyChanges()
        {
            RefreshRemainingDrafts();
        }

        private void ShowHotkeyStatus(string message, bool isError)
        {
            statusUpdateHotKey.Text = message;
            statusUpdateHotKey.SetResourceReference(TextBlock.ForegroundProperty, isError ? "DangerBrush" : "SuccessBrush");
            statusUpdateHotKey.Visibility = Visibility.Visible;

            _hotkeyStatusTimer?.Stop();
            _hotkeyStatusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 4 : 2) };
            _hotkeyStatusTimer.Tick += (s, e) => HideHotkeyStatus();
            _hotkeyStatusTimer.Start();
        }

        private void HideHotkeyStatus()
        {
            _hotkeyStatusTimer?.Stop();
            statusUpdateHotKey.Visibility = Visibility.Collapsed;
        }

        // Language settings
        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "ja";
                Console.WriteLine($"Settings: Source language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);

                // Update MainWindow source language
                if (MainWindow.Instance.sourceLanguageComboBox != null &&
                    MainWindow.FindLanguageItem(MainWindow.Instance.sourceLanguageComboBox, language) is ComboBoxItem mainItem)
                {
                    MainWindow.Instance.sourceLanguageComboBox.SelectedItem = mainItem;
                }

                // Reset the OCR hash to force a fresh comparison after changing source language
                Logic.Instance.ClearAllTextObjects();
            }
        }

        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "en";
                Console.WriteLine($"Settings: Target language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);

                // Update MainWindow target language
                if (MainWindow.Instance.targetLanguageComboBox != null &&
                    MainWindow.FindLanguageItem(MainWindow.Instance.targetLanguageComboBox, language) is ComboBoxItem mainItem)
                {
                    MainWindow.Instance.targetLanguageComboBox.SelectedItem = mainItem;
                }

                // Reset the OCR hash to force a fresh comparison after changing target language
                Logic.Instance.ClearAllTextObjects();
            }
        }

        private void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }

        private void UpdateMangaModeVisibility(string? ocrMethod)
        {
            // Visibility only: loading settings must never write configuration.
            bool show = ocrMethod == "OneOCR" || MangaModeCheckBox.IsChecked == true;
            MangaModeLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MangaModeCheckBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }

        private void CharLevelCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }


        // Language swap button handler
        public void SwapLanguagesButton_Click(object? sender, RoutedEventArgs e)
        {
            MainWindow.Instance.SwapLanguages();
        }

        // Select the source/target languages saved in config, falling back to the MainWindow selection
        private void SelectLanguagesFromConfig()
        {
            string? source = ConfigManager.Instance.GetSourceLanguage();
            if (string.IsNullOrEmpty(source))
                source = (MainWindow.Instance.sourceLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            string? target = ConfigManager.Instance.GetTargetLanguage();
            if (string.IsNullOrEmpty(target))
                target = (MainWindow.Instance.targetLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            if (MainWindow.FindLanguageItem(sourceLanguageComboBox, source) is ComboBoxItem sourceItem)
                sourceLanguageComboBox.SelectedItem = sourceItem;
            if (MainWindow.FindLanguageItem(targetLanguageComboBox, target) is ComboBoxItem targetItem)
                targetLanguageComboBox.SelectedItem = targetItem;
        }

        // Mirror a language change made elsewhere (MainWindow toolbar) without re-running the change handlers
        public static void SyncLanguageSelection()
        {
            var window = _instance;
            if (window == null)
                return;

            window.sourceLanguageComboBox.SelectionChanged -= window.SourceLanguageComboBox_SelectionChanged;
            window.targetLanguageComboBox.SelectionChanged -= window.TargetLanguageComboBox_SelectionChanged;
            window.SelectLanguagesFromConfig();
            window.sourceLanguageComboBox.SelectionChanged += window.SourceLanguageComboBox_SelectionChanged;
            window.targetLanguageComboBox.SelectionChanged += window.TargetLanguageComboBox_SelectionChanged;
        }

        // Block detection settings
        private void BlockDetectionPowerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void SettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        // Translation service changed
        private void TranslationServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceChoiceChanged();
        }

        // Load prompt for the currently selected translation service
        private void LoadCurrentServicePrompt()
        {
            HydrateServicePrompt();
        }

        // Save prompt button clicked
        private void SavePromptButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt();
        }

        // Text box lost focus - save prompt
        private void PromptTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt();
        }

        // Save the current prompt to the selected service
        private void SaveCurrentPrompt()
        {
            StageServicePrompt();
        }

        private void RestoreDefaultPromptButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditServiceDraft) return;
            promptTemplateTextBox.Text = ConfigManager.Instance.GetDefaultServicePrompt(_serviceDraft!.Service);
            StageServicePrompt();
        }

        // Update service-specific settings visibility
        private void UpdateServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isOllamaSelected = selectedService == "Ollama";
                bool isLMStudioSelected = selectedService == "LM Studio";
                bool isGeminiSelected = selectedService == "Gemini";
                bool isCustomApiSelected = selectedService == "Custom API";
                bool isChatGptSelected = selectedService == "ChatGPT";
                bool isMistralSelected = selectedService == "Mistral";
                bool isGoogleTranslateSelected = selectedService == "Google Translate";
                bool isYandexSelected = selectedService == "Yandex";
                bool isGroqSelected = selectedService == "Groq";
                bool isMicrosoftSelected = selectedService == "Microsoft";
                bool isMarianSelected = selectedService == MarianTranslationService.ServiceName;
                marianReadinessText.Visibility = isMarianSelected ? Visibility.Visible : Visibility.Collapsed;
                if (isMarianSelected)
                    UiText.Bind(marianReadinessText, System.Windows.Controls.TextBlock.TextProperty,
                        MarianTranslationService.HasPackage ? "Marian_PackagePresent" : "Marian_PackageMissing");

                // Make sure the window is fully loaded and controls are initialized
                if (ollamaUrlLabel == null || ollamaUrlTextBox == null ||
                    ollamaPortLabel == null || ollamaPortTextBox == null ||
                    ollamaModelLabel == null || ollamaModelGrid == null ||
                    ollamaTemperatureLabel == null || ollamaTemperatureTextBox == null ||
                    ollamaTopPLabel == null || ollamaTopPTextBox == null ||
                    ollamaTopKLabel == null || ollamaTopKTextBox == null ||
                    lmstudioUrlLabel == null || lmstudioUrlTextBox == null ||
                    lmstudioPortLabel == null || lmstudioPortTextBox == null ||
                    lmstudioModelLabel == null || lmstudioModelGrid == null ||
                    geminiApiKeyLabel == null || geminiApiKeyPasswordBox == null ||
                    geminiModelLabel == null || geminiModelGrid == null ||
                    customApiKeyLabel == null || customApiKeyPasswordBox == null ||
                    customApiModelLabel == null || customApiModelGrid == null ||
                    groqApiKeyLabel == null || groqApiKeyPasswordBox == null ||
                    groqModelLabel == null || groqModelGrid == null ||
                    mistralApiKeyLabel == null || mistralApiKeyPasswordBox == null ||
                    mistralModelLabel == null || mistralModelGrid == null ||
                    chatGptApiKeyLabel == null || chatGptApiKeyGrid == null ||
                    chatGptModelLabel == null || chatGptModelGrid == null ||
                    googleTranslateApiKeyLabel == null || googleTranslateApiKeyGrid == null ||
                    googleTranslateServiceTypeLabel == null || googleTranslateServiceTypeComboBox == null ||
                    googleTranslateMappingLabel == null || googleTranslateMappingCheckBox == null ||
                    microsoftApiKeyLabel == null || microsoftLegacyKeyPasswordBox == null ||
                    viewMicrosoftKeysButton == null || SaveMicrosoftKeysButton == null ||
                    microsoftLegacyModeCheckBox == null)
                {
                    Console.WriteLine("UI elements not initialized yet. Skipping visibility update.");
                    return;
                }

                // Show/hide Gemini-specific settings
                geminiApiKeyLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyPasswordBox.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyHelpText.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelGrid.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                viewGeminiKeysButton.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveGeminiKeysButton.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Custom API-specific settings
                customApiKeyLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiKeyPasswordBox.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelGrid.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                viewCustomApiKeysButton.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveCustomApiKeysButton.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiUrlLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiUrlTextBox.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelGrid.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiNoteTextBlock.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Groq-specific settings
                groqApiKeyLabel.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqApiKeyPasswordBox.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqApiKeyHelpText.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqModelLabel.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqModelGrid.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                viewGroqKeysButton.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveGroqKeysButton.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Mistral-specific settings
                mistralApiKeyLabel.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralApiKeyPasswordBox.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralApiKeyHelpText.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralModelLabel.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralModelGrid.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                viewMistralKeysButton.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveMistralKeysButton.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Ollama-specific settings
                ollamaUrlLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaUrlGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTemperatureLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTemperatureTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTopPLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTopPTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTopKLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaTopKTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide LM Studio-specific settings
                lmstudioUrlLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioUrlGrid.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioPortLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioPortTextBox.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioModelLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioModelGrid.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide ChatGPT-specific settings
                chatGptApiKeyLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptApiKeyGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                viewChatGptKeysButton.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveChatGptKeysButton.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Google Translate-specific settings
                googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                bool isGoogleWeb = isGoogleTranslateSelected && googleTranslateServiceTypeComboBox.SelectedIndex == 0;
                googleFreeFallbackLabel.Visibility = isGoogleWeb ? Visibility.Visible : Visibility.Collapsed;
                googleFreeFallbackComboBox.Visibility = isGoogleWeb ? Visibility.Visible : Visibility.Collapsed;

                // Hide prompt template for Google Translate / Yandex / Microsoft
                bool showPromptTemplate = !isGoogleTranslateSelected && !isYandexSelected && !isMicrosoftSelected && !isMarianSelected;
                promptOptionsExpander.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                modelSamplingExpander.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;

                // API key is only visible for Google Translate if Cloud API is selected
                bool showGoogleTranslateApiKey = isGoogleTranslateSelected &&
                    (googleTranslateServiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() == "Cloud API (paid)";

                googleTranslateApiKeyLabel.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateApiKeyGrid.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                // viewGoogleTranslateKeysButton.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide prompt template and related controls for Google Translate
                promptLabel.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                promptTemplateTextBox.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                savePromptButton.Visibility = Visibility.Collapsed;
                restoreDefaultPromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;

                RenderGameEngineEditor();

                // Show/hide Microsoft-specific settings
                // microsoftApiKeyLabel.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // microsoftLegacyKeyPasswordBox.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // viewMicrosoftKeysButton.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // SaveMicrosoftKeysButton.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                microsoftLegacyModeCheckBox.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service-specific settings: {ex.Message}");
            }
        }


        // Gemini API Key changed
        private void GeminiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // Custom API Key changed
        private void CustomApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // Groq API Key changed
        private void GroqApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // Microsoft legacy key changed
        private void MicrosoftLegacyKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        private void MicrosoftLegacyModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Mistral API Key changed
        private void MistralApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // Ollama URL changed
        private void OllamaUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // LM Studio URL changed
        private void LMStudioUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Ollama Port changed
        private void OllamaPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // LMStudio Port changed
        private void LMStudioPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Ollama Model changed
        private void OllamaModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Ollama Temperature changed
        private void OllamaTemperatureTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Ollama Top P changed
        private void OllamaTopPTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Ollama Top K changed
        private void OllamaTopKTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // LM Studio Model changed
        private void LMStudioModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        // Model downloader instance
        private readonly OllamaModelDownloader _modelDownloader = new OllamaModelDownloader();

        private async void TestModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serviceDraft?.IsDirty != false || _serviceDraft.Service != "Ollama") return;
            string model = ollamaModelTextBox.Text.Trim();
            await _modelDownloader.TestAndDownloadModel(model);
        }

        private void ViewModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com/search");
        }

        private void ViewModelsButtonLMStudio_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://lmstudio.ai/models");
        }

        private void GeminiApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/tutorials/setup");
        }

        private void GroqApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://console.groq.com/keys");
        }

        private void MistralApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://docs.mistral.ai/getting-started");
        }

        private void GeminiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }
        private void CustomApiUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void CustomApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void CustomApiModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void GroqModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }

        private void MistralModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }

        private void ViewGeminiModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/gemini-api/docs/models");
        }

        private void ViewGroqModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://console.groq.com/docs/models");
        }

        private void ViewMistralModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://docs.mistral.ai/getting-started/models/models_overview/");
        }

        private void GeminiModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void CustomApiModelTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void GroqModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void MistralModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ServiceFieldChanged(sender, false);
        }

        private void OllamaDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com");
        }

        private void LMStudioDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://lmstudio.ai/download");
        }

        private void ChatGptApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/api-keys");
        }

        private void ViewChatGptModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/docs/models");
        }

        // ChatGPT API Key changed
        private void ChatGptApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServiceKeyInputChanged(sender);
        }

        // ChatGPT Model changed
        private void ChatGptModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }

        private void OpenUrl(string url)
        {
            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening URL: {ex.Message}");
                MessageBox.Show(UiText.Format("Msg_UnableToOpenUrl", url, ex.Message),
                    UiText.Get("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Context settings handlers
        private void MaxContextPiecesTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void MinContextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void MinChatBoxTextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void GameInfoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void MinTextFragmentSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void TextSimilarThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void MinLetterConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void MinLineConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void LineSpacingFactorTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void CheckLanguagePackButton_Click(object sender, RoutedEventArgs e)
        {
            if (sourceLanguageComboBox.SelectedItem is ComboBoxItem selected && selected.Content is string source)
                OcrLanguageSetupWindow.ShowFor(this, source);
        }

        private void CreateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProfileOperation("create");
        }

        private void RemoveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProfileOperation("remove");
        }

        private void UpdateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProfileOperation("update");
        }

        private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProfileOperation("load");
        }

        private void LoadAllProfile()
        {
            if (!Directory.Exists(ConfigManager.Instance._profileFolderPath))
            {
                Directory.CreateDirectory(ConfigManager.Instance._profileFolderPath);
            }

            profileComboBox.Items.Clear();

            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string?> fileNames = Directory.GetFiles(ConfigManager.Instance._profileFolderPath, "*.txt")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name)
                .ToList();

            foreach (string? name in fileNames)
            {
                if (!string.IsNullOrEmpty(name) && uniqueNames.Add(name))
                {
                    profileComboBox.Items.Add(name);
                }
            }
        }

        // Handle Clear Context button click
        private void ClearContextButton_Click(object sender, RoutedEventArgs e)
        {
            RequestSettingsDecision(() => ExecuteClearContextButton_Click(sender, e));
        }

        private void ExecuteClearContextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Clearing translation context and history");

                // Clear translation history in MainWindow
                MainWindow.Instance.ClearTranslationHistory();

                // Reset hash to force new translation on next capture
                Logic.Instance.ResetHash();

                // Clear any existing text objects
                Logic.Instance.ClearAllTextObjects();

                // Show success message
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_ContextCleared"],
                    LocalizationManager.Instance.Strings["Title_Success"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                Console.WriteLine("Translation context cleared successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing translation context: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorClearingContext"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // Ignore Phrases methods

        // Load ignore phrases from ConfigManager
        private void LoadIgnorePhrases()
        {
            try
            {
                _ignorePhrases.Clear();

                IReadOnlyList<(string Phrase, IgnorePhraseMatchType MatchType)> phrases = _advancedDraft != null ? ConfigManager.ParseIgnorePhrases(_advancedDraft[ConfigManager.IGNORE_PHRASES]) : ConfigManager.Instance.GetIgnorePhrases();

                foreach (var (phrase, matchType) in phrases)
                {
                    if (!string.IsNullOrEmpty(phrase))
                    {
                        _ignorePhrases.Add(new IgnorePhrase(phrase, matchType));
                    }
                }

                ignorePhraseListView.ItemsSource = _ignorePhrases;
                newMatchTypeComboBox.ItemsSource = MatchTypeOptions;
                newMatchTypeComboBox.SelectedIndex = 0;
                newMatchTypeComboBox.DisplayMemberPath = "Label";
                newMatchTypeComboBox.SelectedValuePath = "Value";

                Console.WriteLine($"Loaded {_ignorePhrases.Count} ignore phrases");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ignore phrases: {ex.Message}");
            }
        }

        // Save all ignore phrases to ConfigManager
        private void SaveIgnorePhrases()
        {
            if (_isInitializing || _syncingRemainingDrafts || _advancedDraft == null) return;
            string encoded = string.Concat(_ignorePhrases.Select(p => ConfigManager.EncodeIgnorePhrase(p.Phrase) + "|" + p.MatchType + "|"));
            _advancedDraft.Set(ConfigManager.IGNORE_PHRASES, encoded);
            RemainingEdited(true);
        }

        // An invalid pattern used to be saved silently and then never matched (the compile error
        // only reached the console). Tell the user instead.
        private static bool ValidateIgnoreRegex(string pattern)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern);
                return true;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_InvalidRegex"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }
        }

        // Check a new or edited phrase; shows the reason and returns false when it can't be used.
        // Matching ignores case, so duplicates do too.
        private bool ValidateIgnorePhrase(string phrase, IgnorePhraseMatchType matchType, IgnorePhrase? editing)
        {
            if (string.IsNullOrEmpty(phrase))
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_PleaseEnterPhrase"],
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }

            if (_ignorePhrases.Any(p => p != editing && string.Equals(p.Phrase, phrase, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_PhraseAlreadyExists"], phrase),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }

            return matchType != IgnorePhraseMatchType.RegularExpression || ValidateIgnoreRegex(phrase);
        }

        // Add a new ignore phrase
        private void AddIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string phrase = newIgnorePhraseTextBox.Text.Trim();
                var matchType = newMatchTypeComboBox.SelectedValue is IgnorePhraseMatchType mt
                    ? mt
                    : IgnorePhraseMatchType.ExactMatch;

                if (!ValidateIgnorePhrase(phrase, matchType, editing: null))
                    return;

                _ignorePhrases.Add(new IgnorePhrase(phrase, matchType));

                SaveIgnorePhrases();

                newIgnorePhraseTextBox.Text = "";
                newMatchTypeComboBox.SelectedIndex = 0;

                Console.WriteLine($"Added ignore phrase: '{phrase}' (Match Type: {matchType})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding ignore phrase: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorAddingPhrase"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void NewIgnorePhraseTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                AddIgnorePhraseButton_Click(sender, e);
            }
        }

        // ----- In-place editing of a phrase in the list -----

        private void IgnorePhraseTextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            // Clicking into the text box doesn't select the row by itself; select it so Remove works
            if ((sender as FrameworkElement)?.DataContext is IgnorePhrase row)
            {
                ignorePhraseListView.SelectedItem = row;
            }
        }

        private void IgnorePhraseTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox textBox || textBox.DataContext is not IgnorePhrase row)
                return;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                CommitIgnorePhraseEdit(textBox, row);
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                textBox.Text = row.Phrase;
                textBox.SelectAll();
            }
        }

        private void IgnorePhraseTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && textBox.DataContext is IgnorePhrase row)
            {
                CommitIgnorePhraseEdit(textBox, row);
            }
        }

        private bool _committingIgnorePhraseEdit;

        private void CommitIgnorePhraseEdit(System.Windows.Controls.TextBox textBox, IgnorePhrase row)
        {
            // A validation MessageBox takes the focus and would re-enter through LostKeyboardFocus
            if (_committingIgnorePhraseEdit)
                return;

            string phrase = textBox.Text.Trim();
            if (phrase == row.Phrase)
            {
                textBox.Text = row.Phrase;
                return;
            }

            _committingIgnorePhraseEdit = true;
            try
            {
                if (!ValidateIgnorePhrase(phrase, row.MatchType, editing: row))
                {
                    textBox.Text = row.Phrase;
                    return;
                }

                Console.WriteLine($"Edited ignore phrase: '{row.Phrase}' -> '{phrase}'");
                row.Phrase = phrase;
                textBox.Text = phrase;
                SaveIgnorePhrases();
            }
            finally
            {
                _committingIgnorePhraseEdit = false;
            }
        }

        // Remove a selected ignore phrase
        private void RemoveIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ignorePhraseListView.SelectedItem is IgnorePhrase selectedPhrase)
                {
                    string phrase = selectedPhrase.Phrase;

                    {
                        // Remove from the collection
                        _ignorePhrases.Remove(selectedPhrase);

                        // Save to ConfigManager
                        SaveIgnorePhrases();

                        Console.WriteLine($"Removed ignore phrase: '{phrase}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing ignore phrase: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorRemovingPhrase"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // Handle selection changed event
        private void IgnorePhraseListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Enable or disable the Remove button based on selection
            removeIgnorePhraseButton.IsEnabled = ignorePhraseListView.SelectedItem != null;
        }

        // Handle checkbox changed event
        private void IgnorePhraseMatchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                // The first selection when a row's combo box is created is not a user change
                if (e.RemovedItems.Count == 0)
                    return;

                if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.DataContext is IgnorePhrase ignorePhrase)
                {
                    var matchType = comboBox.SelectedValue is IgnorePhraseMatchType mt
                        ? mt
                        : IgnorePhraseMatchType.ExactMatch;

                    if (matchType == IgnorePhraseMatchType.RegularExpression && !ValidateIgnoreRegex(ignorePhrase.Phrase))
                    {
                        // Keep the previous match type for a phrase that is not a valid regex.
                        // SelectedValue is two-way bound, so MatchType already holds the new
                        // value; the previous one comes from the removed item.
                        var previous = e.RemovedItems.Count > 0 && e.RemovedItems[0] is MatchTypeOption removed
                            ? removed.Value
                            : IgnorePhraseMatchType.ExactMatch;
                        ignorePhrase.MatchType = previous;
                        _isInitializing = true;
                        try { comboBox.SelectedValue = previous; }
                        finally { _isInitializing = false; }
                        return;
                    }

                    ignorePhrase.MatchType = matchType;

                    SaveIgnorePhrases();

                    Console.WriteLine($"Updated ignore phrase: '{ignorePhrase.Phrase}' (Match Type: {matchType})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ignore phrase: {ex.Message}");
            }
        }

        // Load exclude regions from MainWindow (which loads them from ConfigManager)
        private void LoadExcludeRegions()
        {
            try
            {
                var regions = MainWindow.Instance.excludeRegions;
                excludeRegionsListView.ItemsSource = regions
                    .Select((region, index) => new ExcludeRegionItem(index, string.Format(
                        LocalizationManager.Instance["Lbl_ExcludeRegionItem"],
                        index + 1,
                        (int)Math.Round(region.Width), (int)Math.Round(region.Height),
                        (int)Math.Round(region.X), (int)Math.Round(region.Y))))
                    .ToList();

                // Set the checkbox without saving the value back
                bool wasInitializing = _isInitializing;
                _isInitializing = true;
                showExcludeRegionsCheckBox.IsChecked = MainWindow.Instance.GetShowExcludeRegions();
                _isInitializing = wasInitializing;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading exclude regions: {ex.Message}");
            }
        }

        // Add a new exclude region
        private void AddExcludeRegionButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide settings window; the list updates through ExcludeRegionsChanged once the region is drawn
            RequestSettingsDecision(() =>
            {
                Hide();
                MainWindow.Instance.ToggleExcludeRegionSelector();
            });
        }

        private void RemoveExcludeRegionButton_Click(object sender, RoutedEventArgs e)
        {
            RequestSettingsDecision(() => ExecuteRemoveExcludeRegionButton_Click(sender, e));
        }

        private void ExecuteRemoveExcludeRegionButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ExcludeRegionItem item)
            {
                MainWindow.Instance.RemoveExcludeRegion(item.Index);
            }
        }

        // Clear all exclude regions
        private void ClearExcludeRegionsButton_Click(object sender, RoutedEventArgs e)
        {
            RequestSettingsDecision(() => ExecuteClearExcludeRegionsButton_Click(sender, e));
        }

        private void ExecuteClearExcludeRegionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.Instance.ClearExcludeRegions();
                Console.WriteLine("All exclude regions cleared from settings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing exclude regions: {ex.Message}");
            }
        }

        // Show exclude regions checkbox checked
        private void ShowExcludeRegionsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool show = showExcludeRegionsCheckBox.IsChecked ?? true;
            MainWindow.Instance.SetShowExcludeRegions(show);
        }

        // Show exclude regions checkbox unchecked
        private void ShowExcludeRegionsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool show = showExcludeRegionsCheckBox.IsChecked ?? true;
            MainWindow.Instance.SetShowExcludeRegions(show);
        }


        // private void OpenAiRealtimeApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        // {
        //     if (_isInitializing) return;
        //     ConfigManager.Instance.SetOpenAiRealtimeApiKey(openAiRealtimeApiKeyPasswordBox.Password.Trim());
        // }

        //Handle Auto-translate checkbox change for audio service

        // Handle Multi selection area checkbox change for multi selection area
        private void MultiSelectionAreaCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening URL: {ex.Message}");
            }
        }

        private void AutoOCRCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }


        private void LlmPreferSpeedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void TranslationPrefetchCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void TranslationCacheCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void SetHotKeyEnableCheckBox_CheckChange(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        private void HDRSupportCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            StageRemainingField(sender);
        }

        // Select the item whose Tag matches (case-insensitive); falls back to the first item
        private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbItem && string.Equals(cbItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = cbItem;
                    return;
                }
            }
            if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
        }

        private static string? SelectedTag(ComboBox comboBox) =>
            (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        private void GoogleFreeFallbackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceFieldChanged(sender, true);
        }


        private void MangaModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CaptureFieldChanged(sender);
        }
    }
}
