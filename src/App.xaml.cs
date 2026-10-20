using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace AlpWareStudio.Localay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private MainWindow? _mainWindow;
    // Keep this marker alive until the process exits so Setup/Uninstall cannot replace in-use files.
    private static System.Threading.Mutex? _runningMarker;

    public App()
    {
        Console.SetOut(new RedactingTextWriter(Console.Out));
        // Register the earliest possible exception handler to catch assembly load failures,
        // TypeInitializationExceptions, and other fatal errors that happen before OnStartup.
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteCrashLog("AppDomain.UnhandledException (fatal=" + args.IsTerminating + ")", ex);
            }
        };

        // Catch unobserved task exceptions (e.g. from fire-and-forget async
        // work) before they tear down the process.
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// Write a crash log to a file in the app directory.
    /// This is needed because the console may not exist yet when OnStartup fails.
    /// </summary>
    private static void WriteCrashLog(string context, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(AppPaths.LogDirectory, "crash_log.txt");
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string entry = $"[{timestamp}] {context}\n" +
                           $"  Exception: {ex.GetType().FullName}: {ex.Message}\n" +
                           $"  HRESULT: 0x{ex.HResult:X8}\n" +
                           $"  Stack: {ex.StackTrace}\n";
            if (ex.InnerException != null)
            {
                entry += $"  Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n" +
                         $"  Inner HRESULT: 0x{ex.InnerException.HResult:X8}\n" +
                         $"  Inner Stack: {ex.InnerException.StackTrace}\n";
            }
            entry += "\n";
            File.AppendAllText(logPath, CredentialRedactor.Redact(entry));
        }
        catch { /* last resort — nothing we can do */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch ALL exceptions during startup, including assembly load / WinRT activation failures.
        // Without this, the app silently exits on systems where WinRT components are missing.
        try
        {
            if (AppRestart.RunHelper(e.Args) is int helperExitCode)
            {
                Shutdown(helperExitCode);
                return;
            }
            StartupCore(e);
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnStartup", ex);
            System.Windows.MessageBox.Show(
                UiText.Format("Msg_StartupError", $"{ex.GetType().Name}: {ex.Message}",
                    Path.Combine(AppPaths.LogDirectory, "crash_log.txt")),
                UiText.Get("Title_StartupError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        _runningMarker = new System.Threading.Mutex(false, "Local\\AlpWareStudio.Localay.Running");
        base.OnStartup(e);

        ApplicationPreferences.CaptureSystemCulture();

        // Force invariant culture for all threads to prevent locale-dependent
        // number formatting (e.g. comma vs period decimal separator) from
        // corrupting config values and other serialized data.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        ApplicationPreferences.Initialize();


        // Set up application-wide keyboard handling
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Handle session ending (when user ends task from taskbar or logs off)
        this.SessionEnding += App_SessionEnding;

        // We'll hook keyboard events in the main window and other windows instead
        // of at the application level (which isn't supported in this context)

        // Show splash screen first
        SplashManager.Instance.ShowSplash();

        // Initialize ChatBoxWindow instance without showing it
        // This ensures ChatBoxWindow.Instance is available immediately
        new ChatBoxWindow();

        // Create main window but don't show it yet
        _mainWindow = new MainWindow();

        // We'll attach the keyboard handlers when the windows are loaded
        // Each window now has its own Application_KeyDown method attached to PreviewKeyDown

        // Add event handler to show main window after splash closes
        SplashManager.Instance.SplashClosed += (sender, args) =>
        {
            _mainWindow?.Show();

            // Attach key handler to other windows once main window is shown
            AttachKeyHandlersToAllWindows();
        };
    }

    // Ensure all windows are initialized and loaded
    private void AttachKeyHandlersToAllWindows()
    {
        // Each window now automatically attaches its own keyboard handler
        // when it's loaded, using PreviewKeyDown and its own Application_KeyDown method.
        // We don't need to do anything here anymore.
    }

    // Handle application-level keyboard events
    private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // No need to check window focus since this is only called when a window has focus
        KeyboardShortcuts.HandleKeyDown(e);
    }

    // Handle any unhandled exceptions to prevent app crashes
    private void App_DispatcherUnhandledException(object sender,
                                               System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception to disk (in addition to the console) so we can
        // diagnose silent crashes in release builds.
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        System.Console.WriteLine($"Unhandled application exception: {e.Exception.Message}");
        System.Console.WriteLine($"Stack trace: {e.Exception.StackTrace}");

        // Mark as handled to prevent app from crashing
        e.Handled = true;
    }

    // Handle session ending (when user ends task from taskbar or logs off)
    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        System.Console.WriteLine($"Session ending: {e.ReasonSessionEnding}");

        try
        {

            // Clean up Logic resources
            Logic.Instance.Finish();

            // Dispose TaskbarIcon from MainWindow if it exists
            if (_mainWindow?.MyNotifyIcon != null)
            {
                _mainWindow.MyNotifyIcon.Dispose();
            }

            // Force close MonitorWindow
            if (MonitorWindow.Instance != null)
            {
                MonitorWindow.Instance.ForceClose();
            }

            // Force close all windows
            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsLoaded)
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"Error closing window {window.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error during session ending cleanup: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ApplicationPreferences.Stop();
        base.OnExit(e);

        // The windows are gone, but a thread still running (native audio/OCR/TTS code, a library's
        // foreground thread) can keep the process alive with nothing on screen to close it. Give normal
        // shutdown a few seconds, then end the process. This thread is background, so it never delays
        // a shutdown that completes on its own.
        int exitCode = e.ApplicationExitCode;
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(5000);
            Console.WriteLine("Process still running 5 s after exit; forcing it to end");
            Environment.Exit(exitCode);
        })
        { IsBackground = true, Name = "ExitWatchdog" }.Start();
    }

    /// <summary>
    /// Restarts the application, ensuring it happens on the UI thread
    /// </summary>
    public static void ShutdownApplication()
    {
        // Make sure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            // If not on UI thread, invoke on UI thread
            Application.Current.Dispatcher.Invoke(ShutdownApplication);
            return;
        }

        try
        {
            // // Get current process path
            // string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

            // if (string.IsNullOrEmpty(appPath))
            // {
            //     System.Windows.MessageBox.Show("Unable to restart application: Could not determine application path.", 
            //         "Restart Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            //     return;
            // }

            // // Create process start info
            // ProcessStartInfo startInfo = new ProcessStartInfo
            // {
            //     FileName = appPath,
            //     UseShellExecute = true
            // };

            // // Start new instance
            // Process.Start(startInfo);

            // Schedule application shutdown after a brief delay to ensure new process starts
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                // Exit current instance
                Application.Current.Shutdown();
            }));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(UiText.Get("Msg_RestartFailed") + "\n\n" + UiText.Format("Msg_UnexpectedError", ex.Message),
                UiText.Get("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Restart after this process exits, without a shell or elevated helper.</summary>
    public static void RestartApplication()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(RestartApplication);
            return;
        }
        try
        {
            AppRestart.Schedule();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.Instance["Msg_RestartFailed"] + "\n" + ex.Message,
                "Localay", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        ShutdownApplication();
    }

    /// <summary>
    /// Shows a confirmation dialog and restarts the application if confirmed
    /// </summary>
    /// <param name="message">Custom message to show in the confirmation dialog</param>
    /// <returns>True if restart was initiated, false if user cancelled</returns>
    public static bool ConfirmAndRestartApplication(string? message = null)
    {
        // Make sure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            // If not on UI thread, invoke on UI thread and return result
            return (bool)Application.Current.Dispatcher.Invoke(
                new Func<string, bool>(ConfirmAndRestartApplication), message);
        }

        var result = System.Windows.MessageBox.Show(message ?? UiText.Get("Msg_RestartRequired"), UiText.Get("Title_Shutdown"),
            MessageBoxButton.OK, MessageBoxImage.Warning);

        ShutdownApplication();
        return true;

    }
}