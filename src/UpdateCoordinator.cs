using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace AlpWareStudio.Localay;

internal static class UpdateCoordinator
{
    private static readonly HttpClient Http = ReleaseUpdateClient.CreateHttpClient();
    internal static readonly ReleaseUpdateClient Client = new(Http);
    private static readonly CancellationTokenSource Lifetime = new();
    private static DispatcherTimer? _timer;
    private static UpdateWindow? _window;
    private static bool _checking;
    private static DateTimeOffset _lastAttempt;
    private static bool _automatic = true;
    private static bool _started;
    private static bool _preferencesLoaded;
    private static string StatePath => Path.Combine(AppPaths.DataDirectory, "update-preferences.json");
    private sealed record Preferences(bool Automatic, DateTimeOffset LastAttempt);
    internal static bool Automatic { get { LoadPreferences(); return _automatic; } }

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        LoadPreferences();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _timer.Tick += async (_, _) => await CheckAsync(false);
        _timer.Start();
        _ = CheckAfterStartupAsync();
    }

    private static void LoadPreferences()
    {
        if (_preferencesLoaded) return;
        _preferencesLoaded = true;
        try
        {
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(StatePath));
            if (saved != null)
            {
                _automatic = saved.Automatic;
                _lastAttempt = saved.LastAttempt > DateTimeOffset.UtcNow ? DateTimeOffset.MinValue : saved.LastAttempt;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private static async Task CheckAfterStartupAsync()
    {
        try { await Task.Delay(5000, Lifetime.Token); await CheckAsync(false); }
        catch (OperationCanceledException) { }
    }

    internal static bool SetAutomatic(bool enabled)
    {
        LoadPreferences();
        bool previous = _automatic;
        _automatic = enabled;
        try { Save(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _automatic = previous;
            MessageBox.Show(UiText.Get("Update_PreferenceFailed"), UiText.Get("Update_Title"));
            return false;
        }
    }

    internal static async Task CheckAsync(bool manual)
    {
        if (Lifetime.IsCancellationRequested) return;
        if (_window != null) { if (manual) { _window.Show(); _window.Activate(); } return; }
        if (_checking) { if (manual) Notify("Update_Checking"); return; }
        if (!manual && (!_automatic || DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromHours(24))) return;
        if (manual && DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromMinutes(1)) { Notify("Update_Wait"); return; }
        _checking = true;
        try
        {
            _lastAttempt = DateTimeOffset.UtcNow;
            try { Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var release = await Client.CheckAsync(Version.Parse(AppRelease.Version), Lifetime.Token);
            if (Lifetime.IsCancellationRequested || (!manual && !_automatic)) return;
            if (release == null) { if (manual) Notify("Update_Current"); return; }
            _window = new UpdateWindow(release);
            _window.Closed += (_, _) => _window = null;
            _window.Show(); // Modeless: translation remains usable until the user chooses to install.
        }
        catch (OperationCanceledException) { if (manual && !Lifetime.IsCancellationRequested) Notify("Update_CheckFailed"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException)
        {
            if (manual && !Lifetime.IsCancellationRequested) Notify("Update_CheckFailed");
        }
        finally { _checking = false; }
    }

    private static void Notify(string key) => MessageBox.Show(UiText.Get(key), UiText.Get("Update_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    private static void Save() => AppPaths.WriteAtomic(StatePath, JsonSerializer.Serialize(new Preferences(_automatic, _lastAttempt)));
    internal static void Stop()
    {
        Lifetime.Cancel();
        _timer?.Stop();
        _window?.Close();
    }
}
