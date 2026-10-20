using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AlpWareStudio.Localay;

[SupportedOSPlatform("windows")]
internal static class WindowsPreferenceReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[]? buffer, ref uint length);

    internal static bool TryReadLanguages(out string[] languages)
    {
        languages = [];
        try
        {
            uint length = 0;
            const uint muiLanguageName = 0x8;
            if (GetUserPreferredUILanguages(muiLanguageName, out _, null, ref length) && length > 0 && length <= 65536)
            {
                char[] buffer = new char[length];
                if (GetUserPreferredUILanguages(muiLanguageName, out _, buffer, ref length))
                {
                    languages = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    return languages.Length > 0;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { Console.WriteLine("Windows display language API unavailable."); }
        return false;
    }

    internal static bool ReadDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { return false; }
    }
}
