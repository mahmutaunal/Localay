using System.IO;

namespace AlpWareStudio.Localay;

internal static class AppPaths
{
    private static readonly Lazy<string> Data = new(() => CreateDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlpWare Studio", "Localay")));
    private static readonly Lazy<string> Logs = new(() => CreateDirectory(Path.Combine(DataDirectory, "Logs")));
    internal static string DataDirectory => Data.Value;
    internal static string LogDirectory => Logs.Value;

    private static string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void MigrateLegacySettings(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        // Do not import logs, binaries, models, or arbitrary files from the application folder.
        foreach (string name in new[] { "config.txt", "gemini_config.txt", "groq_config.txt",
            "mistral_config.txt", "ollama_config.txt", "lmstudio_config.txt", "chatgpt_config.txt",
            "custom_api_config.txt", "google_translate_config.txt" })
        {
            string original = Path.Combine(source, name);
            string target = Path.Combine(destination, name);
            if (!File.Exists(original) || File.Exists(target)) continue;
            string text = File.ReadAllText(original);
            bool mainConfig = name == "config.txt";
            WriteAtomic(target, ConfigFileSecurity.RewriteSecrets(text, mainConfig));
            // The new copy is committed first. Best-effort removal of plaintext keys in the old file.
            string sanitized = ConfigFileSecurity.RewriteSecrets(text, false, preserveInterfacePreferences: true);
            if (sanitized != text)
            {
                try { WriteAtomic(original, sanitized); }
                catch (UnauthorizedAccessException) { Console.WriteLine("Legacy settings contain keys; remove the old config after migration."); }
                catch (IOException) { Console.WriteLine("Could not sanitize legacy settings; remove the old config after migration."); }
            }
        }
        string profiles = Path.Combine(source, "Profiles");
        if (!Directory.Exists(profiles)) return;
        string targetProfiles = CreateDirectory(Path.Combine(destination, "Profiles"));
        foreach (string original in Directory.EnumerateFiles(profiles, "*.txt"))
        {
            string target = Path.Combine(targetProfiles, Path.GetFileName(original));
            if (!File.Exists(target)) WriteAtomic(target,
                ConfigFileSecurity.RewriteSecrets(File.ReadAllText(original), false));
        }
    }

    internal static void WriteAtomic(string path, string content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
