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
            // Use a bare WPF host. Do not run Localay startup, create MainWindow/ConfigManager,
            // capture text, register hotkeys or open browser/mail destinations.
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
