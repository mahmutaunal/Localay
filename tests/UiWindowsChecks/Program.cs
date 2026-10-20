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
