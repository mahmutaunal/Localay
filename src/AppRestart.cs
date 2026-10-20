using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace AlpWareStudio.Localay;

internal static class AppRestart
{
    private const string HelperArgument = "--restart-after";

    internal static void Schedule()
    {
        using var current = Process.GetCurrentProcess();
        var start = CreateStartInfo();
        start.ArgumentList.Add(HelperArgument);
        start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        using var helper = Process.Start(start) ?? throw new IOException("Could not start the restart helper.");
    }

    // This mode never initializes configuration, windows, capture or translation services.
    internal static int? RunHelper(string[] args)
    {
        if (args.Length == 0 || args[0] != HelperArgument) return null;
        if (args.Length != 3 || !int.TryParse(args[1], out int parentId) || parentId <= 0 ||
            parentId == Environment.ProcessId || !long.TryParse(args[2], out long startTicks)) return 1;
        try
        {
            Process? parent;
            try { parent = Process.GetProcessById(parentId); }
            catch (ArgumentException) { parent = null; } // Already exited.
            using (parent)
            {
                if (parent != null)
                {
                    // Guard against a recycled PID or an unrelated process.
                    try
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks == startTicks)
                        {
                            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath,
                                StringComparison.OrdinalIgnoreCase)) return 1;
                            if (!parent.WaitForExit(60000)) return 1;
                        }
                    }
                    catch (InvalidOperationException) { } // Exited during the checks.
                }
            }
            using var restarted = Process.Start(CreateStartInfo());
            return restarted == null ? 1 : 0;
        }
        catch { return 1; }
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        string executable = Environment.ProcessPath ?? throw new IOException("Application path is unavailable.");
        return new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
