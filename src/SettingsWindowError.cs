using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AlpWareStudio.Localay;

internal static class SettingsWindowError
{
    internal static void Show(Window owner, Exception exception)
    {
        App.WriteCrashLog("Opening Settings", exception);
        string logPath;
        try { logPath = Path.Combine(AppPaths.LogDirectory, "crash_log.txt"); }
        catch { logPath = UiText.Get("Settings_ErrorLogUnavailable"); }
        var dialog = Create(exception, logPath);
        if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
            dialog.Owner = owner;
        dialog.ShowDialog();
    }

    // Construct separately so Windows checks can inspect diagnostics without writing logs
    // or blocking on a modal dialog.
    internal static Window Create(Exception exception, string logPath)
    {
        string details = CredentialRedactor.Redact(logPath + Environment.NewLine + Environment.NewLine + exception);
        var dialog = new Window
        {
            Width = 680, Height = 430, MinWidth = 360, MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        UiText.Bind(dialog, Window.TitleProperty, "Settings_ErrorTitle");
        dialog.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var body = new DockPanel { Margin = new Thickness(16) };
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        UiText.Bind(explanation, TextBlock.TextProperty, "Settings_ErrorMessage");
        DockPanel.SetDock(explanation, Dock.Top);
        body.Children.Add(explanation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        UiText.Bind(copy, ContentControl.ContentProperty, "Settings_CopyError");
        copy.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(details); }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Windows.MessageBox.Show(dialog, UiText.Get("Settings_CopyErrorFailed"), dialog.Title);
            }
        };
        var close = new Button { Padding = new Thickness(12, 6, 12, 6), IsCancel = true };
        UiText.Bind(close, ContentControl.ContentProperty, "Btn_Close");
        close.Click += (_, _) => dialog.Close();
        actions.Children.Add(copy);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        body.Children.Add(actions);
        body.Children.Add(new TextBox
        {
            Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        dialog.Content = body;
        dialog.Loaded += (_, _) => DpiHelper.FitWindowToWorkArea(dialog, center: true);
        return dialog;
    }
}
