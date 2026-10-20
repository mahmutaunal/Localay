using System.IO;
using System.ComponentModel;
using System.Diagnostics;

namespace AlpWareStudio.Localay;

internal enum OcrInstallOutcome { Completed, RestartRequired, Pending, Unsupported, Failed, Cancelled, Busy }

internal static class OcrCapabilityInstaller
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static bool IsBusy => Gate.CurrentCount == 0;
    internal static OcrInstallOutcome FromExitCode(int code) => code switch
    {
        0 => OcrInstallOutcome.Completed, 10 => OcrInstallOutcome.RestartRequired,
        11 => OcrInstallOutcome.Unsupported, 14 => OcrInstallOutcome.Pending, _ => OcrInstallOutcome.Failed
    };

    // Only fixed, allow-listed capability tags enter this script. No user text, paths,
    // downloaded scripts, locale commands or restart commands are executed.
    internal static string BuildScript(string capabilityTag, bool exitProcess = true)
    {
        string tag = OcrLanguageSetup.CanonicalCapabilityTag(capabilityTag);
        string core = "& {\n" + """
            $ErrorActionPreference = 'Stop'
            try {
                Import-Module (Join-Path $PSHOME 'Modules\Dism\Dism.psd1') -ErrorAction Stop
            """ + "\n    $available = @(Dism\\Get-WindowsCapability -Online -ErrorAction Stop)\n" +
            $"    $ocr = @($available | Where-Object {{ $_.Name -like 'Language.OCR~~~{tag}~*' }})\n" +
            $"    $basic = @($available | Where-Object {{ $_.Name -like 'Language.Basic~~~{tag}~*' }})\n" + """
                if ($ocr.Count -ne 1 -or $basic.Count -ne 1) { return 11 }
                if ([string]$ocr[0].State -match 'Pending' -or [string]$basic[0].State -match 'Pending') { return 14 }
                foreach ($capability in @($basic[0], $ocr[0])) {
                    $current = Dism\Get-WindowsCapability -Online -Name $capability.Name -ErrorAction Stop
                    if ([string]$current.State -match 'Pending') { return 14 }
                    if ([string]$current.State -ne 'Installed') {
                        $result = Dism\Add-WindowsCapability -Online -Name $capability.Name -ErrorAction Stop
                        if ($result.RestartNeeded) { return 10 }
                        $current = Dism\Get-WindowsCapability -Online -Name $capability.Name -ErrorAction Stop
                        if ([string]$current.State -match 'Pending') { return 14 }
                        if ([string]$current.State -ne 'Installed') { return 12 }
                    }
                }
                return 0
            } catch {
                Write-Error $_ -ErrorAction Continue
                return 12
            }
            }
            """;
        return exitProcess ? "$localayResult = " + core + "\nexit $localayResult" : core;
    }

    internal static ProcessStartInfo CreateStartInfo(string systemDirectory, string capabilityTag)
    {
        if (!Path.IsPathFullyQualified(systemDirectory)) throw new ArgumentException("A system directory is required.");
        var info = new ProcessStartInfo(Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = true, Verb = "runas", WorkingDirectory = systemDirectory
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(BuildScript(capabilityTag));
        return info;
    }

    internal static async Task<OcrInstallOutcome> InstallAsync(string capabilityTag)
    {
        if (!OperatingSystem.IsWindows()) return OcrInstallOutcome.Failed;
        if (!await Gate.WaitAsync(0)) return OcrInstallOutcome.Busy;
        try
        {
            var info = CreateStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.System), capabilityTag);
            using var process = Process.Start(info);
            if (process == null) return OcrInstallOutcome.Failed;
            // Windows servicing may take several minutes. Closing the guide never kills DISM.
            await process.WaitForExitAsync();
            return FromExitCode(process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return OcrInstallOutcome.Cancelled; }
        catch (Exception ex)
        {
            Console.WriteLine($"OCR capability installation failed: {ex.GetType().Name} (0x{ex.HResult:X8})");
            return OcrInstallOutcome.Failed;
        }
        finally { Gate.Release(); }
    }
}
