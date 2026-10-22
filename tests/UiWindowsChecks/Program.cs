using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using AlpWareStudio.Localay;

internal static class Program
{
    private static int passed;
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [STAThread]
    private static int Main()
    {
        try
        {
            // Use a bare WPF host. The Settings command check creates MainWindow with
            // temporary configuration; no capture, global hotkeys or startup prompts run.
            Application.ResourceAssembly = typeof(AboutWindow).Assembly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = ReadResources("App.xaml", _ => true);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            ThemeManager.ApplyTheme(false);
            var strings = LocalizationManager.Instance;
            strings.CurrentLanguage = "en";

            var about = new AboutWindow();
            var keys = new ApiKeysWindow("Custom API", ["fake-regression-key-1234"]);
            var menu = new MenuItem();
            UiText.Bind(menu, MenuItem.HeaderProperty, "Menu_CopySource");
            var profile = new TextBlock();
            UiText.Bind(profile, TextBlock.TextProperty, "Profile_Loaded", "Custom API");
            var waiting = new TextBlock();
            UiText.Bind(waiting, TextBlock.TextProperty, "Status_WaitingService", "Custom API");
            var languages = new ComboBox();
            InterfaceLanguageOptions.Populate(languages);
            InterfaceLanguageOptions.SelectPreference(languages, "system");
            Flush();
            Check(about.Title == "About Localay", "compiled About window loads English bindings");
            Check(InterfaceLanguageOptions.AvailableCodes.Contains("tr"), "Turkish resource available at runtime");
            Check(profile.Text.Contains("Custom API", StringComparison.Ordinal), "English profile text retains user name");

            foreach (string code in new[] { "tr", "en", "tr" })
            {
                strings.CurrentLanguage = code;
                Flush();
                Check(about.Title == strings["About_Title"], "open About title refreshes: " + code);
                Check(keys.Title.Contains(UiText.Service("Custom API"), StringComparison.Ordinal), "open API dialog refreshes provider title: " + code);
                Check((string)menu.Header == strings["Menu_CopySource"], "existing context menu refreshes: " + code);
                Check(waiting.Text == UiText.Format("Status_WaitingService", UiText.Service("Custom API")), "pending translation refreshes: " + code);
                Check(profile.Text.Contains("Custom API", StringComparison.Ordinal), "user profile name is not translated: " + code);
                Check((languages.SelectedItem as ComboBoxItem)?.Tag as string == "system", "interface language identifier survives: " + code);
                Check((languages.Items[0] as ComboBoxItem)?.Content as string == strings["Preference_System"], "system option label refreshes: " + code);
                Check(AppIdentity.ProductsUrl(ApplicationPreferences.EffectiveLanguage).Contains("/" + (code == "tr" ? "tr" : "en") + "/products/", StringComparison.Ordinal), "effective runtime language drives product link: " + code);
            }

            var license = (TextBox)about.FindName("licenseText");
            var notices = (TextBox)about.FindName("noticesText");
            Check(license.IsReadOnly && license.Text.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.Ordinal), "license available read-only offline");
            Check(notices.IsReadOnly && notices.Text.Contains("Thanh Pham", StringComparison.Ordinal), "original contributor retained offline");
            var version = (TextBlock)about.FindName("versionText");
            Check(version.Text == UiText.Format("About_Version", AppRelease.Version), "About version uses current assembly");

            var foreground = (SolidColorBrush)app.Resources["TextBrush"];
            Color light = foreground.Color;
            ThemeManager.ApplyTheme(true);
            Flush();
            Check(ThemeManager.IsDark && foreground.Color != light, "dark theme updates existing shared brush");
            Check(ReferenceEquals(about.Foreground, foreground), "open About observes shared theme brush");
            ThemeManager.ApplyTheme(false);
            Flush();
            Check(!ThemeManager.IsDark && foreground.Color == light, "light theme restores existing brush");

            CheckTemplate("MainWindow.xaml", "LanguageCodeTemplate", "LanguageNameConverter", "LanguageShortNameConverter");
            CheckTemplate("SettingsWindow.xaml", "SettingsLanguageTemplate", "SettingsLanguageNameConverter");
            CheckSettingsWindow();
            CheckSettingsError();
            CheckOverlayTextLayout();
            CheckOcrSetupGuide();
            CheckSettingsCommand();
            var body = (FrameworkElement)about.Content;
            foreach (double width in new[] { 460d, 620d })
            {
                body.Measure(new Size(width, 420));
                body.Arrange(new Rect(0, 0, width, 420));
                body.UpdateLayout();
                Check(!double.IsNaN(body.DesiredSize.Height) && body.ActualWidth == width, "About layout completes at width " + width);
            }
            about.Close();
            keys.Close();
            app.Shutdown();
            Console.WriteLine($"{passed} Windows WPF integration checks passed. No interactive capture, installer, or physical DPI verification was performed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckOcrSetupGuide()
    {
        static void Click(Button button)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Flush();
        }
        var snapshot = OcrLanguageSetup.Evaluate("en", []);
        var completion = new TaskCompletionSource<OcrInstallOutcome>();
        int installs = 0;
        var guide = new OcrLanguageSetupWindow("en", () => snapshot, tag =>
        {
            Check(tag == "en-US", "OCR guide sends only its frozen source capability to the installer");
            installs++;
            return completion.Task;
        });
        try
        {
            guide.Show(); Flush();
            var install = (Button)guide.FindName("ocrSetupInstall");
            var recheck = (Button)guide.FindName("ocrSetupRecheck");
            var result = (TextBlock)guide.FindName("ocrSetupResult");
            Check(install.IsEnabled && recheck.IsEnabled, "missing OCR opens with install and recheck actions");
            foreach (string language in new[] { "tr", "en" })
            {
                LocalizationManager.Instance.CurrentLanguage = language;
                Flush();
                Check(guide.Title == UiText.Get("OcrSetup_Title") &&
                    ((TextBlock)guide.FindName("ocrSetupExplanation")).Text == UiText.Get("OcrSetup_Explanation"),
                    "open OCR guide localizes display-language explanation: " + language);
            }
            Click(install);
            Check(installs == 1 && !install.IsEnabled && !recheck.IsEnabled && result.Text == UiText.Get("OcrSetup_Installing"),
                "install progress prevents duplicate clicks without blocking the window");
            snapshot = OcrLanguageSetup.Evaluate("en", ["en-GB"]);
            completion.SetResult(OcrInstallOutcome.Completed); Flush();
            Check(!install.IsEnabled && recheck.IsEnabled && result.Text == UiText.Get("OcrSetup_Result_Completed"),
                "installation success is confirmed by a fresh recognizer query");
            Check(((TextBlock)guide.FindName("ocrSetupStatus")).Text.Contains("en-GB", StringComparison.Ordinal),
                "guide identifies the usable regional recognizer");
            snapshot = OcrLanguageSetup.Evaluate("en", [], querySucceeded: false);
            Click(recheck);
            Check(!install.IsEnabled && ((TextBlock)guide.FindName("ocrSetupStatus")).Text == UiText.Get("OcrSetup_Unavailable"),
                "OCR activation failure is not shown as an installable missing package");
        }
        finally { guide.Close(); }

        foreach (var outcome in new[] { OcrInstallOutcome.Pending, OcrInstallOutcome.RestartRequired, OcrInstallOutcome.Cancelled,
            OcrInstallOutcome.Failed, OcrInstallOutcome.Unsupported, OcrInstallOutcome.Completed })
        {
            var window = new OcrLanguageSetupWindow("en", () => OcrLanguageSetup.Evaluate("en", []),
                _ => Task.FromResult(outcome));
            try
            {
                window.Show(); Flush();
                Click((Button)window.FindName("ocrSetupInstall"));
                string key = outcome == OcrInstallOutcome.Completed ? "OcrSetup_InstalledNotActive" : "OcrSetup_Result_" + outcome;
                Check(((TextBlock)window.FindName("ocrSetupResult")).Text == UiText.Get(key),
                    "OCR guide distinguishes pending/restart/failure from usable OCR: " + outcome);
            }
            finally { window.Close(); }
        }
        var unknown = new OcrLanguageSetupWindow("unmapped", () => OcrLanguageSetup.Evaluate("unmapped", []));
        Check(!((Button)unknown.FindName("ocrSetupInstall")).IsEnabled && !((Button)unknown.FindName("ocrSetupCopy")).IsEnabled,
            "unknown source languages cannot launch or copy an arbitrary install command");
        unknown.Close();
        var late = new TaskCompletionSource<OcrInstallOutcome>();
        var closing = new OcrLanguageSetupWindow("en", () => OcrLanguageSetup.Evaluate("en", []), _ => late.Task);
        closing.Show(); Flush(); Click((Button)closing.FindName("ocrSetupInstall"));
        closing.Close(); late.SetResult(OcrInstallOutcome.Completed); Flush();
        Check(late.Task.IsCompletedSuccessfully && !closing.IsVisible,
            "closing the guide does not cancel an in-progress servicing operation or reopen a window");
    }

    private static void CheckOverlayTextLayout()
    {
        static TextObject Make(string source, string translated, double width, double height, double lineHeight,
            double min = 10, double max = 68, bool autoSize = true)
        {
            var item = new TextObject(source, 0, 0, width, height, Brushes.White, Brushes.Black)
            {
                TextTranslated = translated,
                OptionsOverride = new(autoSize, false, "Segoe UI", min, max)
            };
            item.BindCaptureGeometry(new(0, 0, width, height, lineHeight), 1, 1);
            item.CreateUIElement(useRelativePosition: false);
            item.UpdateUIElement();
            return item;
        }

        var shortLabel = Make("Play", "Oyna", 80, 23, 23);
        var longLabel = Make("Store", "Mağazadaki bütün oyunlara göz at", 80, 23, 23);
        Check(shortLabel.TextBlock.FontSize == 20 && longLabel.TextBlock.FontSize == 20,
            "equal source line heights preserve equal fonts despite translation lengths");
        Check(!longLabel.TextFitsBox, "long translation requests more space before shrinking");
        longLabel.Relayout(800, TextObject.DefaultMinSourceSizeRatio);
        Check(longLabel.TextFitsBox && longLabel.TextBlock.FontSize == 20,
            "widening fits the translation while preserving its source font size");
        var retainedBorder = longLabel.Border;
        var retainedText = longLabel.TextBlock;
        longLabel.UpdateUIElement();
        Check(ReferenceEquals(retainedBorder, longLabel.Border) && ReferenceEquals(retainedText, longLabel.TextBlock) &&
            longLabel.LayoutWidth == 800 && longLabel.TextBlock.FontSize == 20,
            "an identical UI update preserves the prepared visual and fitted width");
        Check(shortLabel.CanReuseSource(new("Play", 1, 1, 80, 23), 23),
            "unchanged source pixels and bounded OCR jitter can retain the same visual");
        shortLabel.SourceFrameCurrent = false;
        Check(!shortLabel.CanReuseSource(new("Play", 0, 0, 80, 23), 23),
            "a stale background cannot be reused merely because its text and geometry match");
        shortLabel.SourceFrameCurrent = true;
        longLabel.Relayout(80, 0.4, allowShrink: true);
        Check(longLabel.TextBlock.FontSize >= 10 && longLabel.TextBlock.FontSize < 20,
            "explicit last-resort shrink respects the user minimum");
        double shrunken = longLabel.TextBlock.FontSize;
        var heading = Make("Heading", "Başlık", 400, 46, 46);
        var paragraph = Make("one\ntwo\nthree", "Birinci satır ve sonraki satırlar", 250, 90, 23);
        Check(heading.TextBlock.FontSize == 40 && paragraph.TextBlock.FontSize == 20,
            "heading/body hierarchy and multiline source size are preserved");
        var limited = Make("Heading", "Başlık", 400, 46, 46, min: 12, max: 18);
        Check(limited.TextBlock.FontSize == 18, "custom font upper bound is honored");
        var fixedSize = Make("Play", "Oyna", 80, 23, 46, autoSize: false);
        Check(fixedSize.TextBlock.FontSize == 24, "auto-size disabled retains the existing fixed 24-DIP setting");
        var rtl = Make("Settings", "الإعدادات", 180, 23, 23);
        rtl.FlowDirection = FlowDirection.RightToLeft;
        rtl.UpdateUIElement();
        Check(rtl.TextBlock.FlowDirection == FlowDirection.RightToLeft && rtl.TextBlock.FontSize == 20,
            "RTL uses the same source-size policy and retains its direction");

        // Force the actual Loaded callback after layout/collision sizing has already happened.
        var canvas = new Canvas();
        canvas.Children.Add(shortLabel.Border);
        canvas.Children.Add(longLabel.Border);
        Canvas.SetTop(longLabel.Border, 50);
        var window = new Window { Content = canvas, Width = 900, Height = 250, ShowInTaskbar = false };
        try
        {
            window.Show(); Flush();
            Check(shortLabel.TextBlock.FontSize == 20 && longLabel.TextBlock.FontSize == shrunken,
                "Loaded does not override the chosen font or undo last-resort fitting");
            longLabel.Relayout(800, TextObject.DefaultMinSourceSizeRatio);
            Check(longLabel.TextBlock.FontSize == 20, "a later refresh restores preferred size when space is available");
        }
        finally { window.Close(); }

        var dpi = Make("Heading", "Başlık", 160, 46, 46);
        dpi.BindCaptureGeometry(new(300, 150, 160, 46, 46), 1, 1);
        var patch = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
            null, new byte[16], 8);
        dpi.SetErasedBackground(patch, new Rect(296, 146, 168, 54));
        dpi.UpdateUIElement();
        var halo = dpi.TextBlock.Effect;
        var eraser = dpi.EraserElement;
        dpi.UpdateUIElement();
        Check(ReferenceEquals(halo, dpi.TextBlock.Effect) && ReferenceEquals(eraser, dpi.EraserElement),
            "unchanged background and text reuse the prepared halo and eraser");
        dpi.ApplyRenderDpi(2, 2);
        Check(dpi.X == 150 && dpi.Y == 75 && dpi.Width == 80 && dpi.Height == 23 && dpi.SourceLineHeight == 23,
            "existing OCR objects reproject their original pixels on a DPI change");
        Check(dpi.TextBlock.FontSize == 20 && dpi.EraserElement!.Width == 84 &&
            Canvas.GetLeft(dpi.EraserElement) == 148 && Canvas.GetTop(dpi.EraserElement) == 73,
            "font and erased background scale together with the OCR box");
        dpi.ApplyRenderDpi(2, 2);
        dpi.ApplyRenderDpi(1, 1);
        Check(dpi.X == 300 && dpi.Width == 160 && dpi.TextBlock.FontSize == 40 && dpi.EraserElement!.Width == 168,
            "repeated DPI refreshes and monitor round-trip do not compound scaling");
        TextObject.ClearCache();
    }

    private static void CheckSettingsWindow()
    {
        var settings = new SettingsWindow(initializeRuntime: false);
        try
        {
            settings.Show();
            Flush();
            Check(settings.IsVisible, "complete compiled Settings window opens");
            var tabs = (TabControl)settings.FindName("settingsCategories");
            var phrases = (ListView)settings.FindName("ignorePhraseListView");
            phrases.ItemsSource = new[] { new IgnorePhrase("sample", IgnorePhraseMatchType.Contains) };
            foreach (var tab in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = tab;
                Flush();
                foreach (var expander in Descendants(settings).OfType<Expander>().ToList())
                    expander.IsExpanded = true;
                Flush();
                Check(Descendants(settings).Any(), "Settings category renders: " + tab.Header);
            }
            var matchType = Descendants(phrases).OfType<ComboBox>().Single();
            Check(ReferenceEquals(matchType.ItemsSource, settings.MatchTypeOptions), "ignore-phrase template resolves its Settings ancestor");
            Check(matchType.SelectedValue is IgnorePhraseMatchType.Contains, "ignore-phrase template preserves selected match type");
            foreach (string language in new[] { "en", "tr" })
            {
                LocalizationManager.Instance.CurrentLanguage = language;
                Flush();
                Check(settings.Title == UiText.Get("Settings_Title"), "complete Settings title follows language: " + language);
            }
            settings.Hide();
            settings.Show();
            Flush();
            Check(settings.IsVisible, "compiled Settings window can hide and reopen");
            Check(typeof(ConfigManager).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null) == null,
                "Settings markup does not create user configuration during initialization");
            Check(!Application.Current.Windows.OfType<MainWindow>().Any(), "Settings visual check does not start MainWindow or capture");
        }
        finally { settings.Close(); }
    }

    private static void CheckSettingsCommand()
    {
        // Exercise the actual command and full Settings hydration against temporary data.
        // Only capture, clipboard monitoring, global hotkeys and startup prompts are disabled.
        string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Localay.SettingsChecks." + Guid.NewGuid().ToString("N"));
        var configField = typeof(ConfigManager).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? previousConfig = configField.GetValue(null);
        string previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        MainWindow? main = null;
        SettingsWindow? settings = null;
        var saveErrors = new List<Exception>();
        try
        {
            System.IO.Directory.CreateDirectory(temporary);
            var config = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
            config.SetLanguageInterface("tr");
            configField.SetValue(null, config);
            LocalizationManager.Instance.CurrentLanguage = "tr";
            main = new MainWindow(initializeRuntime: false);
            main.Show(); Flush();
            Check(main.FindName("languageSelector") == null && main.FindName("AppVersion") == null,
                "interface preferences and version are no longer on the task surface");
            var button = (Button)main.FindName("settingsButton");
            void ClickSettings()
            {
                bool diagnostic = false;
                // A regression must fail this test instead of hanging in a modal error dialog.
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    foreach (var dialog in Application.Current.Windows.OfType<Window>()
                        .Where(w => w.Title == UiText.Get("Settings_ErrorTitle")).ToArray())
                    {
                        diagnostic = true;
                        dialog.Close();
                    }
                }));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Flush();
                Check(!diagnostic, "Settings command completes without an error dialog");
            }
            ClickSettings();
            settings = SettingsWindow.Instance;
            Check(settings.IsVisible && ReferenceEquals(settings.Owner, main), "actual Settings command loads configuration and opens an owned window");
            Check(button.FontWeight == FontWeights.SemiBold, "Settings button reflects the visible window");
            Check(((ComboBoxItem)((ComboBox)settings.FindName("interfaceLanguageComboBox")).SelectedItem).Tag as string == "tr",
                "full Settings hydration preserves the saved interface preference");
            CheckGeneralPreferences(main, settings, config, temporary, saveErrors);
            CheckServicePreferences(settings, config, temporary, saveErrors);
            CheckCapturePreferences(settings, config, temporary, saveErrors);
            CheckResultPreferences(settings, config, temporary, saveErrors);
            CheckRemainingPreferences(settings, config, temporary, saveErrors);
            for (int i = 0; i < 10; i++)
            {
                settings.Close(); Flush();
                Check(!settings.IsVisible && button.FontWeight == FontWeights.Normal, "title-bar close hides Settings and resets its command state");
                ClickSettings();
                Check(ReferenceEquals(settings, SettingsWindow.Instance) && settings.IsVisible, "Settings reopens using the same hydrated window");
            }
            ((TabControl)settings.FindName("settingsCategories")).SelectedIndex = 5;
            var threshold = (TextBox)settings.FindName("textSimilarThresholdTextBox");
            double savedThreshold = config.GetTextSimilarThreshold();
            threshold.Text = "0.8";
            var writer = new System.IO.StringWriter();
            var originalOutput = Console.Out;
            try
            {
                Console.SetOut(writer);
                threshold.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            }
            finally { Console.SetOut(originalOutput); }
            Check(config.GetTextSimilarThreshold() == savedThreshold && !writer.ToString().Contains("Saved config to "),
                "advanced field edits and focus loss never write after repeated Settings reloads");
            ((Button)settings.FindName("cancelAdvancedDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            settings.WindowState = WindowState.Minimized;
            ClickSettings();
            Check(settings.WindowState == WindowState.Normal && settings.IsVisible, "Settings command restores a minimized window");
            settings.Hide(); Flush();
            Check(button.FontWeight == FontWeights.Normal, "region-selection Hide path resets the command state");

            // A load failure must leave a retryable hidden window and an inactive button.
            System.IO.Directory.Delete(config._profileFolderPath, recursive: true);
            System.IO.File.WriteAllText(config._profileFolderPath, "simulated directory conflict");
            bool failed = false;
            try { settings.ShowFor(main); }
            catch (System.IO.IOException) { failed = true; }
            Check(failed && !settings.IsVisible && button.FontWeight == FontWeights.Normal, "failed Settings hydration leaves a retryable inactive command");
            System.IO.File.Delete(config._profileFolderPath);
            System.IO.Directory.CreateDirectory(config._profileFolderPath);
            ClickSettings();
            Check(settings.IsVisible, "Settings can retry after a temporary profile access failure");

            settings.Hide();
            settings.Left = -100000;
            settings.Top = -100000;
            ClickSettings();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(settings).Handle;
            var bounds = DpiHelper.GetWindowPhysicalRect(hwnd);
            var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            Check(bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Left + bounds.Width <= area.Right + 1 && bounds.Top + bounds.Height <= area.Bottom + 1,
                "reopening brings an off-screen Settings window inside the work area");
            settings.Hide(); main.Hide();
            ClickSettings();
            Check(settings.IsVisible && settings.Owner == null, "tray-style Settings command works while the main window is hidden");
            Check(!Application.Current.Windows.OfType<MonitorWindow>().Any(), "Settings command test does not start screen capture or the overlay");
            Check(System.IO.File.ReadAllText(System.IO.Path.Combine(temporary, "config.txt")).Contains("language_interface|tr|", StringComparison.Ordinal),
                "opening and closing Settings does not overwrite the saved interface preference");
        }
        finally
        {
            settings?.ClosePermanently();
            main?.MyNotifyIcon?.Dispose();
            main?.Close();
            configField.SetValue(null, previousConfig);
            LocalizationManager.Instance.CurrentLanguage = previousLanguage;
            if (System.IO.Directory.Exists(temporary)) System.IO.Directory.Delete(temporary, recursive: true);
        }
    }

    private static void CheckGeneralPreferences(MainWindow main, SettingsWindow settings, ConfigManager config,
        string temporary, List<Exception> saveErrors)
    {
        static void Select(ComboBox combo, string code) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag as string == code);
        var settingsLanguage = (ComboBox)settings.FindName("interfaceLanguageComboBox");
        var theme = (ComboBox)settings.FindName("appearanceComboBox");
        var save = (Button)settings.FindName("saveGeneralDraftButton");
        var cancel = (Button)settings.FindName("cancelGeneralDraftButton");
        var stay = (Button)settings.FindName("stayWithGeneralDraftButton");
        var categories = (TabControl)settings.FindName("settingsCategories");
        void Click(Button control) { control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        void SaveLanguage(string code)
        {
            string before = config.GetLanguageInterface();
            Select(settingsLanguage, code); Flush();
            Check(config.GetLanguageInterface() == before, "selecting interface language only edits the draft");
            if (save.IsEnabled) Click(save);
        }
        var version = (TextBlock)settings.FindName("settingsVersionText");
        var welcome = new WelcomeWindow { Owner = main };
        try
        {
            welcome.Show(); Flush();
            var welcomeLanguage = (ComboBox)welcome.FindName("InterfaceLanguageComboBox");
            void Verify(string preference)
            {
                Flush();
                Check(new[] { settingsLanguage, welcomeLanguage }.All(combo =>
                    (combo.SelectedItem as ComboBoxItem)?.Tag as string == preference),
                    "Settings and Quick Start selectors agree: " + preference);
                Check(settings.Title == UiText.Get("Settings_Title") && welcome.Title == UiText.Get("Welcome_WindowTitle") &&
                    version.Text == UiText.Format("About_Version", AppRelease.Version),
                    "open windows and version label refresh without restarting");
                var reopened = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
                Check(reopened.GetLanguageInterface() == preference, "fresh configuration restores the selected interface preference: " + preference);
            }
            SaveLanguage("en"); Verify("en");
            SaveLanguage("tr"); Verify("tr");
            Select(welcomeLanguage, "en"); Verify("en");
            SaveLanguage("system"); Verify("system");
            Check(config.GetLanguageInterface() == "system", "system preference is not replaced by its effective language");
            SaveLanguage("tr"); Verify("tr");
            Check(ApplicationPreferences.SetThemePreference("light"), "theme uses the same committed preference path");
            Check(saveErrors.Count == 0, "normal preference changes and reopening have no save failures");

            Select(settingsLanguage, "en"); Select(theme, "dark"); Flush();
            Check(config.GetLanguageInterface() == "tr" && config.GetThemeMode() == "light" && !ThemeManager.IsDark,
                "drafting both preferences leaves the running interface unchanged");
            settings.Close(); Flush();
            Check(settings.IsVisible && stay.Visibility == Visibility.Visible, "closing a dirty window requests an inline decision");
            Click(stay); Click(cancel); Verify("tr");
            Select(settingsLanguage, "en"); categories.SelectedIndex = 1; Flush();
            Check(categories.SelectedIndex == 0 && stay.Visibility == Visibility.Visible, "dirty navigation waits on General");
            Click(save); Verify("en");
            Check(categories.SelectedIndex == 1, "successful save completes requested navigation");
            categories.SelectedIndex = 0;
            SaveLanguage("tr"); Verify("tr");
            Select(settingsLanguage, "en");
            Check(ApplicationPreferences.SetThemePreference("dark"), "external writer changes the saved theme");
            Check(!save.IsEnabled && (settingsLanguage.SelectedItem as ComboBoxItem)?.Tag as string == "en",
                "conflict preserves edits and blocks overwriting externally changed preferences");
            Click(cancel); Verify("tr");
            Check((theme.SelectedItem as ComboBoxItem)?.Tag as string == "dark", "cancel loads the latest committed theme");
            Check(ApplicationPreferences.SetThemePreference("light"), "restore theme baseline");

            var clipboardEnabled = (CheckBox)settings.FindName("clipboardAutoTranslateCheckBox");
            var clipboardCopy = (CheckBox)settings.FindName("clipboardCopyResultCheckBox");
            var consent = (CheckBox)settings.FindName("clipboardConsentCheckBox");
            var delay = (TextBox)settings.FindName("clipboardDebounceTextBox");
            var limit = (TextBox)settings.FindName("clipboardMaxCharsTextBox");
            delay.Text = "invalid"; settingsLanguage.Focus(); Flush();
            Check(!save.IsEnabled && delay.Text == "invalid" && config.GetClipboardDebounceMs() == 300,
                "invalid numeric input survives focus changes and never persists");
            Click(cancel);
            clipboardEnabled.IsChecked = true; Flush();
            Check(!save.IsEnabled && !config.IsClipboardAutoTranslateEnabled() && !config.GetBoolValue("clipboard_privacy_consent_v1"),
                "draft enable neither starts monitoring nor records consent");
            consent.IsChecked = true; delay.Text = "750"; limit.Text = "2500"; clipboardCopy.IsChecked = false;
            Select(settingsLanguage, "en"); Select(theme, "dark"); Click(save);
            Check(config.IsClipboardAutoTranslateEnabled() && config.GetBoolValue("clipboard_privacy_consent_v1") &&
                config.GetClipboardDebounceMs() == 750 && config.GetClipboardMaxChars() == 2500 && !config.IsClipboardCopyResultEnabled() &&
                config.GetLanguageInterface() == "en" && config.GetThemeMode() == "dark", "one General save commits all options and permission");
            var reopenedGeneral = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
            Check(reopenedGeneral.IsClipboardAutoTranslateEnabled() && reopenedGeneral.GetClipboardDebounceMs() == 750 &&
                reopenedGeneral.GetClipboardMaxChars() == 2500, "fresh config reads the saved clipboard ranges and consent");
            clipboardEnabled.IsChecked = false; delay.Text = "300"; limit.Text = "5000"; clipboardCopy.IsChecked = true;
            Select(settingsLanguage, "tr"); Select(theme, "light"); Click(save); Verify("tr");
            Check(!config.IsClipboardAutoTranslateEnabled() && !config.GetBoolValue("clipboard_privacy_consent_v1"),
                "saving disable revokes persisted clipboard permission");

            string configPath = System.IO.Path.Combine(temporary, "config.txt");
            string lastGood = configPath + ".last-good";
            string saved = System.IO.File.ReadAllText(configPath);
            System.IO.File.Move(configPath, lastGood);
            System.IO.Directory.CreateDirectory(configPath); // Force an actual atomic replacement failure.
            try
            {
                Select(settingsLanguage, "en"); Select(theme, "dark");
                clipboardEnabled.IsChecked = true; consent.IsChecked = true; delay.Text = "600"; Click(save);
                Check(!config.IsClipboardAutoTranslateEnabled() && !config.GetBoolValue("clipboard_privacy_consent_v1") &&
                    config.GetClipboardDebounceMs() == 300 && clipboardEnabled.IsChecked == true && delay.Text == "600",
                    "failed General replacement preserves saved clipboard choices and keeps all pending edits");
                Check(saveErrors.Count == 1 && config.GetLanguageInterface() == "tr" &&
                    LocalizationManager.Instance.CurrentLanguage == "tr", "failed save preserves the committed language and reports one error");
                Check((settingsLanguage.SelectedItem as ComboBoxItem)?.Tag as string == "en" &&
                    (theme.SelectedItem as ComboBoxItem)?.Tag as string == "dark" && save.IsEnabled &&
                    (welcomeLanguage.SelectedItem as ComboBoxItem)?.Tag as string == "tr",
                    "failed batch save retains edits while other windows retain committed preferences");
                Check(!ApplicationPreferences.SetThemePreference("dark") && config.GetThemeMode() == "light" && !ThemeManager.IsDark,
                    "a failed theme save cannot apply an unsaved theme");
                Check(System.IO.File.ReadAllText(lastGood) == saved, "failed preferences do not change the last committed file");
            }
            finally
            {
                System.IO.Directory.Delete(configPath);
                System.IO.File.Move(lastGood, configPath);
            }
            Click(cancel); Verify("tr");
            config.SetValue("preference_save_probe", "kept");
            config.SaveConfig();
            Verify("tr");
            Check(System.IO.File.ReadAllText(configPath).Contains("language_interface|tr|", StringComparison.Ordinal),
                "a later unrelated save cannot persist the previously failed language choice");

            config.SaveTranslationAreas([], "preference-export");
            string exported = System.IO.File.ReadAllText(System.IO.Path.Combine(config._profileFolderPath, "preference-export.txt"));
            Check(!exported.Contains("language_interface|", StringComparison.Ordinal) && !exported.Contains("theme_mode|", StringComparison.Ordinal),
                "actual profile export excludes application interface preferences");
            string injected = System.IO.Path.Combine(config._profileFolderPath, "preference-import.txt");
            System.IO.File.WriteAllText(injected, "language_interface|en|\ntheme_mode|dark|\ndark_mode|true|\nsource_language|en|\n");
            config.GetTranslationAreas(injected);
            Check(config.GetLanguageInterface() == "tr" && config.GetThemeMode() == "light",
                "actual legacy profile import cannot replace the user's interface language or theme");
            config.SaveConfig(); Verify("tr");
            SaveLanguage("en"); Verify("en");
            SaveLanguage("tr"); Verify("tr");
        }
        finally { welcome.Close(); }
    }

    // Source prepared for a Windows host; never executed as part of the Mac-only design work.
    private static void CheckServicePreferences(SettingsWindow settings, ConfigManager config,
        string temporary, List<Exception> saveErrors)
    {
        var baseline = config.ReadServicePreferences();
        var category = (TabControl)settings.FindName("settingsCategories");
        var choice = (ComboBox)settings.FindName("translationServiceComboBox");
        var port = (TextBox)settings.FindName("ollamaPortTextBox");
        var model = (TextBox)settings.FindName("ollamaModelTextBox");
        var prompt = (TextBox)settings.FindName("promptTemplateTextBox");
        var key = (PasswordBox)settings.FindName("geminiApiKeyPasswordBox");
        var save = (Button)settings.FindName("saveServiceDraftButton");
        var cancel = (Button)settings.FindName("cancelServiceDraftButton");
        var stay = (Button)settings.FindName("stayWithServiceDraftButton");
        void Select(string service) { choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(i => i.Content?.ToString() == service); Flush(); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        category.SelectedIndex = 1;
        Select("Ollama"); model.Text = "draft-only-model"; port.Text = "";
        Check(!save.IsEnabled && config.GetOllamaModel() == baseline[ConfigManager.OLLAMA_MODEL], "invalid port stays in the service draft without persisting the model");
        port.Text = "12001";
        Select("Gemini"); key.Password = "fake-service-batch-key"; prompt.Text = "Translate source_language to target_language. Regression prompt.";
        Select("Ollama");
        Check(model.Text == "draft-only-model" && port.Text == "12001", "provider switching restores its pending model and port");
        category.SelectedIndex = 0; Flush();
        Check(category.SelectedIndex == 1 && stay.Visibility == Visibility.Visible, "dirty Service page blocks navigation with an inline decision");
        Click(stay);
        Select("Google Translate");
        string configPath = System.IO.Path.Combine(temporary, "config.txt");
        string savedPath = configPath + ".service-last-good";
        System.IO.File.Move(configPath, savedPath);
        System.IO.Directory.CreateDirectory(configPath);
        int errorsBefore = saveErrors.Count;
        try
        {
            Click(save);
            Check(saveErrors.Count == errorsBefore + 1 && ServicePreferenceDraft.Same(config.ReadServicePreferences(), baseline),
                "failed service replacement preserves selected provider, model, keys and prompts together");
            Select("Gemini");
            Check(key.Password == "fake-service-batch-key" && prompt.Text.Contains("Regression prompt"),
                "failed replacement retains pending key and prompt input");
        }
        finally
        {
            System.IO.Directory.Delete(configPath);
            System.IO.File.Move(savedPath, configPath);
        }
        Select("Google Translate");
        config.SetValue("service_unrelated_probe", "preserve"); config.SaveConfig();
        Click(save);
        Check(config.GetCurrentTranslationService() == "Google Translate" && config.GetOllamaModel() == "draft-only-model" &&
            config.GetOllamaPort() == "12001" && config.GetApiKeysList("Gemini").Contains("fake-service-batch-key") &&
            config.GetServicePrompt("Gemini").Contains("Regression prompt"), "one service save applies provider drafts, credentials and prompt");
        Check(config.GetValue("service_unrelated_probe") == "preserve" &&
            !System.IO.File.ReadAllText(configPath).Contains("fake-service-batch-key"), "service batch preserves unrelated settings and protects new keys");
        var reopened = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
        Check(reopened.GetServicePrompt("Gemini").Contains("Regression prompt") && reopened.GetOllamaPort() == "12001",
            "fresh configuration restores prompt overrides and connection values");
        Select("Gemini"); key.Password = "another-fake-pending-key";
        Click(cancel);
        Select("Gemini");
        Check(key.Password == "", "cancel discards pending credentials across provider changes");
        Click(cancel);
        var current = config.ReadServicePreferences();
        Check(config.TrySaveServicePreferences(current, baseline, out bool conflict) && !conflict,
            "service integration fixture restores its original saved preferences");
        settings.ReloadSetting(); category.SelectedIndex = 0;
    }

    // Prepared Windows integration checks; this method is not run during the Mac-only source work.
    private static void CheckCapturePreferences(SettingsWindow settings, ConfigManager config,
        string temporary, List<Exception> saveErrors)
    {
        var baseline = config.ReadCapturePreferences();
        var categories = (TabControl)settings.FindName("settingsCategories");
        var screen = (ComboBox)settings.FindName("screenComboBox");
        var ocr = (ComboBox)settings.FindName("ocrMethodComboBox");
        var autoOcr = (CheckBox)settings.FindName("AutoOCRCheckBox");
        var autoTranslate = (CheckBox)settings.FindName("autoTranslateCheckBox");
        var multiple = (CheckBox)settings.FindName("multiSelectionAreaCheckBox");
        var manga = (CheckBox)settings.FindName("MangaModeCheckBox");
        var save = (Button)settings.FindName("saveCaptureDraftButton");
        var cancel = (Button)settings.FindName("cancelCaptureDraftButton");
        var stay = (Button)settings.FindName("stayWithCaptureDraftButton");
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        categories.SelectedIndex = 2;
        ocr.SelectedItem = ocr.Items.OfType<ComboBoxItem>().Single(item => item.Content?.ToString() == "Windows OCR");
        manga.IsChecked = true; Flush();
        Check(!save.IsEnabled && config.ReadCapturePreferences() == baseline,
            "invalid Windows OCR/manga combination never mutates the saved capture settings");
        Click(cancel);
        autoOcr.IsChecked = !baseline.AutoOcr;
        autoTranslate.IsChecked = !baseline.AutoTranslate;
        multiple.IsChecked = !baseline.MultipleAreas;
        screen.SelectedItem = screen.Items.OfType<ComboBoxItem>().First();
        Check(config.ReadCapturePreferences() == baseline, "capture checkbox and monitor edits remain draft-only");
        settings.Close(); Flush();
        Check(settings.IsVisible && stay.Visibility == Visibility.Visible, "dirty capture settings request an inline close decision");
        Click(stay);
        categories.SelectedIndex = 0; Flush();
        Check(categories.SelectedIndex == 2, "capture draft blocks leaving the page until resolved");
        Click(stay);
        string configPath = System.IO.Path.Combine(temporary, "config.txt");
        string lastGood = configPath + ".capture-last-good";
        System.IO.File.Move(configPath, lastGood); System.IO.Directory.CreateDirectory(configPath);
        int errorsBefore = saveErrors.Count;
        try
        {
            Click(save);
            Check(saveErrors.Count == errorsBefore + 1 && config.ReadCapturePreferences() == baseline && save.IsEnabled &&
                autoOcr.IsChecked == !baseline.AutoOcr && autoTranslate.IsChecked == !baseline.AutoTranslate,
                "failed capture replacement preserves saved values and all editor choices");
        }
        finally
        {
            System.IO.Directory.Delete(configPath); System.IO.File.Move(lastGood, configPath);
        }
        config.SetValue("capture_unrelated_probe", "keep"); config.SaveConfig();
        var edited = new CapturePreferenceValues((ocr.SelectedItem as ComboBoxItem)!.Content.ToString()!,
            (int)((ComboBoxItem)screen.SelectedItem).Tag, autoOcr.IsChecked == true, autoTranslate.IsChecked == true,
            multiple.IsChecked == true, manga.IsChecked == true);
        Click(save);
        Check(config.ReadCapturePreferences() == edited && config.GetValue("capture_unrelated_probe") == "keep",
            "one capture save commits all six choices while preserving other settings");
        var reopened = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
        Check(reopened.ReadCapturePreferences() == edited, "fresh configuration restores the complete capture batch");
        autoOcr.IsChecked = baseline.AutoOcr;
        config.SetAutoOCR(baseline.AutoOcr);
        Click(save);
        Check(!save.IsEnabled, "capture save conflict never overwrites an external capture preference change");
        Click(cancel);
        Check(config.TrySaveCapturePreferences(config.ReadCapturePreferences(), baseline, ConfigManager.ReadCaptureScreenTopology(), out var issue) && issue == null,
            "capture integration fixture restores its saved configuration");
        settings.ReloadSetting(); categories.SelectedIndex = 0;
    }

    private static void CheckResultPreferences(SettingsWindow settings, ConfigManager config,
        string temporary, List<Exception> saveErrors)
    {
        var baseline = config.ReadResultPreferences();
        var categories = (TabControl)settings.FindName("settingsCategories");
        var save = (Button)settings.FindName("saveResultDraftButton");
        var cancel = (Button)settings.FindName("cancelResultDraftButton");
        var defaults = (Button)settings.FindName("resultDefaultsButton");
        var stay = (Button)settings.FindName("stayWithResultDraftButton");
        IEnumerable<DependencyObject> Children(DependencyObject parent)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
                if (child is DependencyObject node)
                {
                    yield return node;
                    foreach (var descendant in Children(node)) yield return descendant;
                }
        }
        Control Input(string key) => Children(settings).OfType<Control>().Single(c => c.Tag as string == key);
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        categories.SelectedIndex = 3; Flush();
        var size = (TextBox)Input("chatbox_font_size");
        var color = (TextBox)Input("chatbox_translated_text_color");
        size.Text = baseline["chatbox_font_size"] == "18" ? "19" : "18";
        color.Text = "#FF9CC2FF"; Flush();
        Check(ResultPreferenceDraft.Same(config.ReadResultPreferences(), baseline) && save.IsEnabled &&
            ((SolidColorBrush)((TextBlock)settings.FindName("chatPreviewTranslation")).Foreground).Color ==
                (Color)ColorConverter.ConvertFromString("#FF9CC2FF"),
            "result edits update only the example preview before save");
        color.Text = "#incomplete"; Flush();
        Check(!save.IsEnabled && color.Text == "#incomplete" && ResultPreferenceDraft.Same(config.ReadResultPreferences(), baseline),
            "invalid result color remains editable and cannot be saved");
        color.Text = "#FF9CC2FF";
        categories.SelectedIndex = 0; Flush();
        Check(categories.SelectedIndex == 3 && stay.Visibility == Visibility.Visible, "dirty result draft blocks category navigation");
        Click(stay);
        string configPath = System.IO.Path.Combine(temporary, "config.txt");
        string lastGood = configPath + ".result-last-good";
        System.IO.File.Move(configPath, lastGood); System.IO.Directory.CreateDirectory(configPath);
        int errorsBefore = saveErrors.Count;
        string editedSize = size.Text;
        try
        {
            Click(save);
            Check(saveErrors.Count == errorsBefore + 1 && ResultPreferenceDraft.Same(config.ReadResultPreferences(), baseline) &&
                save.IsEnabled && size.Text == editedSize, "failed result save preserves all draft values and saved settings");
        }
        finally
        {
            System.IO.Directory.Delete(configPath); System.IO.File.Move(lastGood, configPath);
        }
        config.SetValue("result_unrelated_probe", "keep"); config.SaveConfig();
        Click(save);
        var edited = config.ReadResultPreferences();
        var reopened = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
        Check(edited["chatbox_font_size"] == editedSize && edited["chatbox_translated_text_color"] == "#FF9CC2FF" &&
            ResultPreferenceDraft.Same(edited, reopened.ReadResultPreferences()) && config.GetValue("result_unrelated_probe") == "keep",
            "result save persists the complete draft and preserves unrelated settings");
        Click(defaults);
        Check(ResultPreferenceDraft.Same(config.ReadResultPreferences(), edited), "result defaults affect only the draft");
        Click(cancel);
        Check(size.Text == editedSize && !save.IsEnabled, "result cancel restores the saved form and preview");
        size.Text = editedSize == "20" ? "21" : "20";
        config.SetValue("show_icon_signal", edited["show_icon_signal"] == "true" ? "false" : "true"); config.SaveConfig();
        Click(save);
        Check(!save.IsEnabled && config.ReadResultPreferences()["chatbox_font_size"] == editedSize,
            "result save conflict cannot overwrite an external appearance change");
        Click(cancel);
        Check(config.TrySaveResultPreferences(config.ReadResultPreferences(), baseline, out bool conflict) && !conflict,
            "result integration fixture restores saved configuration");
        settings.ReloadSetting(); categories.SelectedIndex = 0;
    }

    private static void CheckRemainingPreferences(SettingsWindow settings, ConfigManager config,
        string temporary, List<Exception> saveErrors)
    {
        var categories = (TabControl)settings.FindName("settingsCategories");
        var advanced = config.ReadAdvancedPreferences();
        var scale = (TextBox)settings.FindName("blockDetectionPowerTextBox");
        var save = (Button)settings.FindName("saveAdvancedDraftButton");
        var cancel = (Button)settings.FindName("cancelAdvancedDraftButton");
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        categories.SelectedIndex = 5; Flush();
        scale.Text = "NaN"; Flush();
        Check(!save.IsEnabled && SectionPreferenceDraft.Same(advanced, config.ReadAdvancedPreferences()),
            "advanced nonfinite input is retained without changing saved settings");
        scale.Text = advanced["block_detection_scale"] == "6" ? "7" : "6";
        string editedScale = scale.Text;
        categories.SelectedIndex = 0; Flush();
        Check(categories.SelectedIndex == 5, "advanced edits guard category navigation");
        Click((Button)settings.FindName("stayWithAdvancedDraftButton"));
        string path = System.IO.Path.Combine(temporary, "config.txt"), good = path + ".advanced-last-good";
        System.IO.File.Move(path, good); System.IO.Directory.CreateDirectory(path);
        int errorsBefore = saveErrors.Count;
        try
        {
            Click(save);
            Check(saveErrors.Count == errorsBefore + 1 && SectionPreferenceDraft.Same(advanced, config.ReadAdvancedPreferences()) &&
                scale.Text == editedScale && save.IsEnabled, "failed advanced batch save preserves committed values and raw edits");
        }
        finally { System.IO.Directory.Delete(path); System.IO.File.Move(good, path); }
        Click(save);
        var fresh = new ConfigManager(temporary, System.IO.Path.Combine(temporary, "no-legacy"), saveErrors.Add);
        Check(fresh.ReadAdvancedPreferences()["block_detection_scale"] == editedScale,
            "advanced Save persists the edited batch for a fresh reader");
        scale.Text = "8"; Click(cancel);
        Check(scale.Text == editedScale && !save.IsEnabled, "advanced Cancel restores all saved fields");
        categories.SelectedIndex = 4; Flush();
        var shortcutBaseline = config.ReadShortcutPreferences();
        var enable = (CheckBox)settings.FindName("hotKeyEnableCheckBox");
        enable.IsChecked = shortcutBaseline[ConfigManager.HOT_KEY_ENABLE] != "true";
        Check(SectionPreferenceDraft.Same(shortcutBaseline, config.ReadShortcutPreferences()) &&
            ((Button)settings.FindName("saveShortcutsDraftButton")).IsEnabled, "shortcut enable changes remain draft-only");
        Click((Button)settings.FindName("cancelShortcutsDraftButton"));
        Check(!((Button)settings.FindName("saveShortcutsDraftButton")).IsEnabled, "shortcut Cancel restores the saved state");
        var profileNameInput = (TextBox)settings.FindName("profileNameTextBox");
        var create = (Button)settings.FindName("createGameProfileButton");
        profileNameInput.Text = "CON"; Flush();
        Check(!create.IsEnabled, "invalid Windows profile name disables Create");
        profileNameInput.Text = "Design integration profile"; Flush(); Click(create);
        string profilePath = System.IO.Path.Combine(config._profileFolderPath, "Design integration profile.txt");
        Check(System.IO.File.Exists(profilePath) && !create.IsEnabled, "Create saves a profile and prevents accidental overwrite");
        string profileContent = System.IO.File.ReadAllText(profilePath);
        Check(!profileContent.Contains("theme_mode|") && !profileContent.Contains("hotkey_start_stop|") &&
            !profileContent.Contains("translation_areas|"), "profile scope excludes personal preferences and screen geometry");
        Click((Button)settings.FindName("removeGameProfileButton"));
        Check(System.IO.File.Exists(profilePath) && ((StackPanel)settings.FindName("profileDecisionPanel")).Visibility == Visibility.Visible,
            "profile removal waits for inline confirmation");
        // Cancel through the same routed handler used by the visible inline button.
        var decision = (StackPanel)settings.FindName("profileDecisionPanel");
        var actions = (WrapPanel)decision.Children[0];
        Click((Button)actions.Children[1]);
        Check(System.IO.File.Exists(profilePath), "cancelling profile removal preserves the file");
        Click((Button)settings.FindName("removeGameProfileButton")); Click((Button)actions.Children[0]);
        Check(!System.IO.File.Exists(profilePath) && config.ReadAdvancedPreferences()["block_detection_scale"] == editedScale,
            "confirmed profile removal preserves active settings");
        Check(config.TrySaveSectionPreferences(true, config.ReadAdvancedPreferences(), advanced, out bool conflict) && !conflict,
            "advanced integration fixture restores its saved preferences");
        settings.ReloadSetting(); categories.SelectedIndex = 0;
    }

    private static void CheckSettingsError()
    {
        const string secret = "fake-settings-secret-1234";
        CredentialRedactor.Register(secret);
        var error = new InvalidOperationException("outer", new Exception("nested failure: " + secret));
        var dialog = SettingsWindowError.Create(error, "test-log-path");
        try
        {
            dialog.Show();
            Flush();
            var details = Descendants(dialog).OfType<TextBox>().Single();
            Check(details.IsReadOnly && details.Text.Contains("nested failure"), "Settings diagnostics include nested exceptions and are selectable read-only");
            Check(!details.Text.Contains(secret) && details.Text.Contains("[REDACTED]"), "Settings diagnostics redact registered credentials");
            Check(Descendants(dialog).OfType<Button>().Any(b => Equals(b.Content, UiText.Get("Settings_CopyError"))), "Settings diagnostics expose a copy action");
            Check(dialog.Title == UiText.Get("Settings_ErrorTitle"), "Settings error dialog is localized");
        }
        finally { dialog.Close(); }
    }

    private static void CheckTemplate(string file, string templateKey, params string[] converterKeys)
    {
        var resources = ReadResources(file, element =>
            converterKeys.Contains(element.Attribute(Xaml + "Key")?.Value) || element.Attribute(Xaml + "Key")?.Value == templateKey);
        var presenter = new ContentPresenter { Content = "en", ContentTemplate = (DataTemplate)resources[templateKey] };
        presenter.Resources = resources;
        var item = new ComboBoxItem { Content = "ja", ContentTemplate = (DataTemplate)resources[templateKey], Resources = resources };
        foreach (string language in new[] { "en", "tr", "en" })
        {
            LocalizationManager.Instance.CurrentLanguage = language;
            Flush();
            Layout(presenter);
            Layout(item);
            string selected = string.Join(" ", Descendants(presenter).OfType<TextBlock>().Select(t => t.Text));
            string option = string.Join(" ", Descendants(item).OfType<TextBlock>().Select(t => t.Text));
            string expectedSelected = file == "MainWindow.xaml" ? LanguageDisplay.GetShortName("en") : LanguageDisplay.GetDisplayName("en");
            Check(selected == expectedSelected, file + " selected language refreshes: " + language);
            Check(option.Contains(LanguageDisplay.GetDisplayName("ja"), StringComparison.Ordinal), file + " dropdown language refreshes: " + language);
            Check((string)item.Content == "ja", file + " OCR language code remains unchanged: " + language);
        }
    }

    private static ResourceDictionary ReadResources(string file, Func<XElement, bool> include)
    {
        var source = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        var resources = source.Root!.Elements().Single(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));
        var dictionary = new XElement(Wpf + "ResourceDictionary",
            new XAttribute("xmlns", Wpf.NamespaceName), new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "local", "clr-namespace:AlpWareStudio.Localay;assembly=Localay"),
            resources.Elements().Where(include).Select(e => new XElement(e)));
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(400, 100));
        element.Arrange(new Rect(0, 0, 400, 100));
        element.UpdateLayout();
        Flush();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Flush()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }
}
