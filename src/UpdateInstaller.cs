using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AlpWareStudio.Localay;

// Wait outside WPF's shutdown path. The helper has no running-app mutex and never elevates itself.
internal static class UpdateInstaller
{
    private const string HelperArgument = "--install-update-after";
    internal static string DownloadRoot => Path.Combine(Path.GetTempPath(), "LocalayUpdates");

    internal static bool IsInstalledCopy()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var registration = machine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}_is1");
            return registration?.GetValue("InstallLocation") is string location &&
                string.Equals(Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return false; }
    }

    internal static void Schedule(string path, ReleaseUpdate release)
    {
        using var current = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Application path is unavailable."))
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (string argument in new[] { HelperArgument, current.Id.ToString(CultureInfo.InvariantCulture),
            current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), path,
            release.Size.ToString(CultureInfo.InvariantCulture), release.Sha256 }) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new IOException("Could not start update helper.");
    }

    internal static int? RunHelper(string[] args)
    {
        if (args.Length == 0 || args[0] != HelperArgument) return null;
        if (args.Length != 6 || !int.TryParse(args[1], out int parentId) || parentId <= 0 || parentId == Environment.ProcessId ||
            !long.TryParse(args[2], out long ticks) || !long.TryParse(args[4], out long size) || size <= 0 ||
            size > ReleaseUpdateClient.MaximumPackageSize || !Regex.IsMatch(args[5], "^[a-fA-F0-9]{64}$")) return 1;
        bool parentExited = false;
        try
        {
            Process? parent;
            try { parent = Process.GetProcessById(parentId); }
            catch (ArgumentException) { parent = null; }
            using (parent)
            {
                if (parent != null)
                {
                    try
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != ticks ||
                            !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 1;
                        if (!parent.WaitForExit(60000)) return 1;
                    }
                    catch (InvalidOperationException) { } // Parent exited while it was being inspected.
                }
            }
            parentExited = true;
            string path = Path.GetFullPath(args[3]);
            if (!IsPackagePath(path)) throw new InvalidDataException("Unexpected update path.");
            // Keep the verified file open against writes/deletion until Setup is launched.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length != size || !Convert.ToHexString(SHA256.HashData(file)).Equals(args[5], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update file changed after download.");
            file.Position = 0;
            if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("Update is not a Windows executable.");
            using var setup = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = "/NORESTART /SP-",
                WorkingDirectory = Path.GetDirectoryName(path)!
            }) ?? throw new IOException("Could not start Setup.");
            // Normal Setup UI handles UAC and offers launch as the original user after installation.
            return 0;
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Update installer handoff", ex);
            if (parentExited)
            {
                // In particular, cancelling UAC should not leave the user without the original app.
                try
                {
                    var restart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                    restart.ArgumentList.Add("--update-handoff-failed");
                    using var restarted = Process.Start(restart);
                }
                catch (Exception restartError) { App.WriteCrashLog("Update handoff recovery", restartError); }
            }
            return 1;
        }
    }

    internal static bool IsPackagePath(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        return folder != null && Guid.TryParseExact(Path.GetFileName(folder), "N", out _) &&
            string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(DownloadRoot), StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(Path.GetFileName(path), @"^Localay-\d+\.\d+\.\d+-Setup-x64\.exe$") &&
            !new[] { path, folder, DownloadRoot }.Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0);
    }
}
