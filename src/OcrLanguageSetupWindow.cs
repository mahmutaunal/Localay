using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AlpWareStudio.Localay;

internal sealed class OcrLanguageSetupWindow : Window
{
    private readonly string _source;
    private readonly Func<OcrLanguageStatus> _query;
    private readonly Func<string, Task<OcrInstallOutcome>> _install;
    private readonly TextBlock _status, _installed, _result;
    private readonly Button _installButton, _recheck, _copy;
    private readonly ProgressBar _progress;
    private OcrLanguageStatus _snapshot;
    private bool _isInstalling, _closed;

    internal OcrLanguageSetupWindow(string source, Func<OcrLanguageStatus>? query = null,
        Func<string, Task<OcrInstallOutcome>>? install = null)
    {
        _source = source;
        _query = query ?? (() => WindowsOCRManager.Instance.GetLanguageStatus(source));
        _install = install ?? OcrCapabilityInstaller.InstallAsync;
        _snapshot = OcrLanguageSetup.Evaluate(source, Array.Empty<string>());
        Width = 680; Height = 560; MinWidth = 380; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UiText.Bind(this, TitleProperty, "OcrSetup_Title");
        SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new Thickness(18) };
        TextBlock Text(string name, string key, params object[] args)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            RegisterName(name, text);
            UiText.Bind(text, TextBlock.TextProperty, key, args);
            body.Children.Add(text);
            return text;
        }
        Text("ocrSetupExplanation", "OcrSetup_Explanation");
        Text("ocrSetupSource", "OcrSetup_Source", LanguageDisplay.GetDisplayName(source),
            OcrLanguageSetup.CapabilityTag(source) ?? source);
        _status = Text("ocrSetupStatus", "OcrSetup_Missing");
        _status.FontWeight = FontWeights.SemiBold;
        _installed = Text("ocrSetupInstalled", "OcrSetup_InstalledList", UiText.Get("OcrSetup_None"));
        Text("ocrSetupInstallHint", "OcrSetup_InstallHint");
        _progress = new ProgressBar { Height = 5, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(_progress);
        _result = Text("ocrSetupResult", "OcrSetup_Installing");
        _result.Visibility = Visibility.Collapsed;
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button Action(string name, string key)
        {
            var button = new Button { Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8) };
            RegisterName(name, button);
            UiText.Bind(button, ContentControl.ContentProperty, key);
            actions.Children.Add(button);
            return button;
        }
        _installButton = Action("ocrSetupInstall", "OcrSetup_Install");
        _installButton.Click += Install_Click;
        _recheck = Action("ocrSetupRecheck", "OcrSetup_Recheck");
        _recheck.Click += (_, _) => { _result.Visibility = Visibility.Collapsed; RefreshStatus(); };
        var windows = Action("ocrSetupWindows", "OcrSetup_WindowsSettings");
        windows.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:optionalfeatures") { UseShellExecute = true }); }
            catch { ShowResult("OcrSetup_SettingsFailed"); }
        };
        _copy = Action("ocrSetupCopy", "OcrSetup_CopyCommand");
        _copy.Click += (_, _) =>
        {
            try
            {
                if (_snapshot.CapabilityTag == null) return;
                System.Windows.Clipboard.SetText(OcrCapabilityInstaller.BuildScript(_snapshot.CapabilityTag, exitProcess: false));
                ShowResult("OcrSetup_CommandCopied");
            }
            catch { ShowResult("OcrSetup_CopyFailed"); }
        };
        body.Children.Add(actions);
        var close = new Button { Padding = new Thickness(12, 7, 12, 7), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        UiText.Bind(close, ContentControl.ContentProperty, "Btn_Close");
        close.Click += (_, _) => Close();
        body.Children.Add(close);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => DpiHelper.FitWindowToWorkArea(this, center: true);
        Closed += (_, _) => _closed = true;
        RefreshStatus();
    }

    internal static void ShowFor(Window owner, string source)
    {
        var window = new OcrLanguageSetupWindow(source);
        if (owner.IsVisible && owner.WindowState != WindowState.Minimized) window.Owner = owner;
        window.ShowDialog();
    }

    private void RefreshStatus()
    {
        try { _snapshot = _query(); }
        catch { _snapshot = OcrLanguageSetup.Evaluate(_source, Array.Empty<string>(), querySucceeded: false); }
        UiText.Bind(_status, TextBlock.TextProperty, "OcrSetup_" + _snapshot.State,
            _snapshot.RecognizerTag ?? _snapshot.CapabilityTag ?? _source);
        UiText.Bind(_installed, TextBlock.TextProperty, "OcrSetup_InstalledList",
            _snapshot.InstalledTags.Count == 0 ? UiText.Get("OcrSetup_None") : string.Join(", ", _snapshot.InstalledTags));
        _installButton.IsEnabled = !_isInstalling && !OcrCapabilityInstaller.IsBusy && _snapshot.State == OcrLanguageState.Missing;
        _recheck.IsEnabled = !_isInstalling;
        _copy.IsEnabled = !_isInstalling && _snapshot.CapabilityTag != null && !_snapshot.IsReady;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || _snapshot.State != OcrLanguageState.Missing || _snapshot.CapabilityTag == null) return;
        _isInstalling = true;
        RefreshStatus();
        if (_snapshot.State != OcrLanguageState.Missing || _snapshot.CapabilityTag == null) { _isInstalling = false; RefreshStatus(); return; }
        _progress.Visibility = Visibility.Visible;
        ShowResult("OcrSetup_Installing");
        OcrInstallOutcome result;
        try { result = await _install(_snapshot.CapabilityTag); }
        catch { result = OcrInstallOutcome.Failed; }
        finally { _isInstalling = false; }
        if (_closed) return;
        _progress.Visibility = Visibility.Collapsed;
        RefreshStatus();
        string key = result == OcrInstallOutcome.Completed && !_snapshot.IsReady
            ? "OcrSetup_InstalledNotActive" : "OcrSetup_Result_" + result;
        ShowResult(key);
    }

    private void ShowResult(string key)
    {
        UiText.Bind(_result, TextBlock.TextProperty, key);
        _result.Visibility = Visibility.Visible;
    }
}
