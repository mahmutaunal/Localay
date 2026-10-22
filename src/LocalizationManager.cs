using System.ComponentModel;
using System.IO;
using Newtonsoft.Json;

namespace AlpWareStudio.Localay;

public class LocalizationManager : INotifyPropertyChanged
{
    private static LocalizationManager? _instance;
    public static LocalizationManager Instance => _instance ??= new LocalizationManager();

    private readonly string _languageDirectory;
    private readonly Dictionary<string, string> _defaultStrings;
    private Dictionary<string, string> _strings;
    private string _currentLanguage = "en";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set { if (_currentLanguage != value) LoadLanguage(value); }
    }

    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value : $"[{key}]";

    public LocalizationManager Strings => this;

    private LocalizationManager() : this(Path.Combine(AppContext.BaseDirectory, "Languages")) { }

    // Language loading can be checked with isolated resource files without a WPF host.
    internal LocalizationManager(string languageDirectory)
    {
        _languageDirectory = languageDirectory;
        _defaultStrings = ReadLanguage("en") ?? new Dictionary<string, string>();
        _strings = new Dictionary<string, string>(_defaultStrings);
    }

    private Dictionary<string, string>? ReadLanguage(string code)
    {
        try
        {
            string path = Path.Combine(_languageDirectory, code + ".json");
            if (!File.Exists(path)) return null;
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            return loaded?.Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key, item => item.Value);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error loading language {code}: {exception.GetType().Name}");
            return null;
        }
    }

    public void LoadLanguage(string langCode)
    {
        string code = PreferenceRules.NormalizeLanguagePreference(langCode);
        if (code == PreferenceRules.System) code = "en";
        var loaded = ReadLanguage(code);
        var next = new Dictionary<string, string>(_defaultStrings);
        if (loaded != null)
            foreach (var item in loaded) next[item.Key] = item.Value;

        // Commit a complete dictionary and its effective language before notifying bindings.
        _strings = next;
        _currentLanguage = loaded != null ? code : "en";
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(Strings));
        OnPropertyChanged("Item[]");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        if (PropertyChanged == null) return;
        foreach (PropertyChangedEventHandler observer in PropertyChanged.GetInvocationList())
        {
            try { observer(this, new PropertyChangedEventArgs(propertyName)); }
            catch (Exception exception) { App.WriteCrashLog("Localization observer", exception); }
        }
    }
}
