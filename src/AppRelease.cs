using System.Diagnostics;
using System.Reflection;

namespace AlpWareStudio.Localay;

internal static class AppRelease
{
    internal const string ReleasesUrl = AppIdentity.ReleasesUrl;
    internal static string Version => typeof(AppRelease).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    internal static void OpenReleases() => Process.Start(new ProcessStartInfo(ReleasesUrl)
    {
        UseShellExecute = true
    });
}
