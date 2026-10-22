using System.Globalization;
using Microsoft.Win32;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace AlpWareStudio.Localay;

internal static class ApplicationPreferences
{
    private static string _fallbackLanguage = "en";
    private static string[] _systemLanguages = ["en"];
    private static bool _systemDark;
    private static bool _initialized;
    private static bool _stopped;
    private static int _refreshQueued;
    private static (string Language, string Theme, string Effective, bool Dark)? _lastApplied;
    internal static event EventHandler? Changed;

    internal static string LanguagePreference => ConfigManager.Instance.GetLanguageInterface();
    internal static string ThemePreference => ConfigManager.Instance.GetThemeMode();
    internal static string EffectiveLanguage => LocalizationManager.Instance.CurrentLanguage;

    internal static void CaptureSystemCulture() => _fallbackLanguage = CultureInfo.CurrentUICulture.Name;

    // Called after invariant formatting is configured and before any window is created.
    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Refresh();
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.FrameworkElement.LoadedEvent,
            new System.Windows.RoutedEventHandler((sender, e) =>
            {
                if (sender is System.Windows.Window window) ThemeManager.ApplyWindowCaption(window);
            }));
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Application.Current.Activated += OnApplicationActivated;
    }

    internal static void Stop()
    {
        _stopped = true;
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        Application.Current.Activated -= OnApplicationActivated;
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
    private static void OnApplicationActivated(object? sender, EventArgs e)
    {
        if (LanguagePreference == PreferenceRules.System || ThemePreference == PreferenceRules.System) QueueRefresh();
    }

    private static void QueueRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (_stopped || dispatcher == null || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!_stopped) Refresh();
            }, DispatcherPriority.Background);
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _refreshQueued, 0); }
    }

    internal static bool SetLanguagePreference(string value)
    {
        if (!ConfigManager.Instance.SetLanguageInterface(value))
        {
            NotifyChanged(); // Restore all selectors to the last committed choice.
            return false;
        }
        Refresh();
        return true;
    }

    internal static bool SetThemePreference(string value)
    {
        if (!ConfigManager.Instance.SetThemeMode(value))
        {
            NotifyChanged();
            return false;
        }
        Refresh();
        return true;
    }

    internal static bool SaveGeneralDraft(GeneralPreferenceValues expected,
        GeneralPreferenceValues edited, out bool conflict)
    {
        if (!ConfigManager.Instance.TrySaveGeneralPreferences(expected, edited, out conflict)) return false;
        MainWindow.ApplySavedClipboardPreferences();
        Refresh(); // Apply and notify only after the combined replacement succeeds.
        return true;
    }

    private static void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (EventHandler observer in Changed.GetInvocationList())
        {
            try { observer(null, EventArgs.Empty); }
            catch (Exception exception) { App.WriteCrashLog("Application preference observer", exception); }
        }
    }

    internal static void Refresh()
    {
        if (_stopped) return;
        _systemLanguages = WindowsPreferenceReader.TryReadLanguages(out string[] languages)
            ? languages : [string.IsNullOrEmpty(_fallbackLanguage) ? "en" : _fallbackLanguage];
        _systemDark = WindowsPreferenceReader.ReadDark();
        string language = PreferenceRules.ResolveLanguage(LanguagePreference, _systemLanguages, InterfaceLanguageOptions.AvailableCodes);
        bool dark = PreferenceRules.ResolveDark(ThemePreference, _systemDark);
        var snapshot = (LanguagePreference, ThemePreference, language, dark);
        if (_lastApplied == snapshot && EffectiveLanguage == language && ThemeManager.IsDark == dark) return;
        ThemeManager.ApplyTheme(dark);
        LocalizationManager.Instance.CurrentLanguage = language;
        _lastApplied = snapshot;
        NotifyChanged();
    }

}
