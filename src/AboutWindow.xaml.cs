using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AlpWareStudio.Localay;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        UiText.Bind(versionText, TextBlock.TextProperty, "About_Version", AppRelease.Version);
        licenseText.Text = ReadDocument("Localay.Licenses.LICENSE.md");
        noticesText.Text = ReadDocument("Localay.Licenses.THIRD_PARTY_NOTICES.md");
        Loaded += (_, _) => DpiHelper.FitWindowToWorkArea(this, center: true);
    }

    private static string ReadDocument(string name)
    {
        // Embed the original documents so they remain available offline in every distribution.
        using Stream stream = typeof(AboutWindow).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Required bundled document is missing: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, UiText.Format("Msg_UnableToOpenUrl", url, ex.Message),
                UiText.Get("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Products_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.ProductsUrl(ApplicationPreferences.EffectiveLanguage));
    private void Contact_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.ContactUrl);
    private void Source_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.ProjectUrl);
    private void Releases_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.ReleasesUrl);
    private void Issues_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.IssuesUrl);
    private void Upstream_Click(object sender, RoutedEventArgs e) => OpenLink(AppIdentity.UpstreamUrl);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
