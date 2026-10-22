using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;

namespace AlpWareStudio.Localay;

internal sealed class UpdateWindow : Window
{
    private readonly ReleaseUpdate _release;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 6, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button _primary = new() { MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _cancel = new() { MinWidth = 90 };
    private CancellationTokenSource? _download;
    private string? _package;
    private string? _directory;
    private bool _closed;
    private bool _handoff;

    internal UpdateWindow(ReleaseUpdate release)
    {
        _release = release;
        Width = 560; Height = 470; MinWidth = 420; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        UiText.Bind(this, TitleProperty, "Update_Title");
        var panel = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        UiText.Bind(heading, TextBlock.TextProperty, "Update_Available", release.Version.ToString(3));
        panel.Children.Add(heading);
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) };
        UiText.Bind(detail, TextBlock.TextProperty, "Update_Details", AppRelease.Version, Math.Ceiling(release.Size / 1048576d));
        panel.Children.Add(detail);
        // Release notes are displayed as plain text; no HTML, links or scripts are executed.
        var notes = new TextBox
        {
            Text = release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 140, Padding = new Thickness(8)
        };
        notes.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        notes.SetResourceReference(ForegroundProperty, "TextBrush");
        notes.SetResourceReference(BorderBrushProperty, "BorderBrush");
        panel.Children.Add(notes);
        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        var buttons = new WrapPanel();
        _primary.SetResourceReference(StyleProperty, "LocalayPrimaryButton");
        _cancel.SetResourceReference(StyleProperty, "LocalayButton");
        UiText.Bind(_primary, ContentProperty, "Update_Download");
        UiText.Bind(_cancel, ContentProperty, "Update_Later");
        _primary.Click += Primary_Click;
        _cancel.Click += (_, _) => { if (_download != null) _download.Cancel(); else Close(); };
        buttons.Children.Add(_primary); buttons.Children.Add(_cancel); panel.Children.Add(buttons);
        var releases = new Button { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        releases.SetResourceReference(StyleProperty, "LocalayButton");
        UiText.Bind(releases, ContentProperty, "Btn_OfficialReleases");
        releases.Click += (_, _) =>
        {
            try { AppRelease.OpenReleases(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { UiText.Bind(_status, TextBlock.TextProperty, "Update_InstallFailed"); }
        };
        panel.Children.Add(releases);
        if (!UpdateInstaller.IsInstalledCopy())
        {
            _primary.IsEnabled = false;
            UiText.Bind(_status, TextBlock.TextProperty, "Update_Portable");
        }
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => DpiHelper.FitWindowToWorkArea(this, center: true);
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_package != null)
        {
            SettingsWindow.RequestUpdateInstall(() =>
            {
                if (_closed || _package == null) return;
                try
                {
                    // Settings decisions persist edits before this callback; do not overwrite them again.
                    UpdateInstaller.Schedule(_package, _release);
                    _handoff = true;
                    MainWindow.Instance.Close(); // Existing shutdown stops capture/translation and disposes windows.
                }
                catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
                {
                    _handoff = false;
                    UiText.Bind(_status, TextBlock.TextProperty, "Update_InstallFailed");
                }
            });
            return;
        }
        if (_download != null) return;
        _download = new CancellationTokenSource();
        _primary.IsEnabled = false;
        UiText.Bind(_cancel, ContentProperty, "Update_Cancel");
        UiText.Bind(_status, TextBlock.TextProperty, "Update_Downloading", 0);
        _directory = Path.Combine(UpdateInstaller.DownloadRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var progress = new Progress<int>(percent =>
            {
                if (_closed || _download == null) return;
                _progress.Value = percent;
                UiText.Bind(_status, TextBlock.TextProperty, "Update_Downloading", percent);
            });
            _package = await UpdateCoordinator.Client.DownloadAsync(_release, _directory, progress, _download.Token);
            if (_closed) { Cleanup(); return; }
            UiText.Bind(_primary, ContentProperty, "Update_Install");
            UiText.Bind(_status, TextBlock.TextProperty, "Update_Ready");
        }
        catch (OperationCanceledException)
        {
            if (!_closed) UiText.Bind(_status, TextBlock.TextProperty, _download.IsCancellationRequested ? "Update_Cancelled" : "Update_DownloadFailed");
            Cleanup();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException)
        {
            if (!_closed) UiText.Bind(_status, TextBlock.TextProperty, "Update_DownloadFailed");
            Cleanup();
        }
        finally
        {
            _download.Dispose(); _download = null;
            if (!_closed)
            {
                _primary.IsEnabled = true;
                UiText.Bind(_cancel, ContentProperty, "Update_Later");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _download?.Cancel();
        if (_download == null && !_handoff) Cleanup();
        base.OnClosed(e);
    }

    private void Cleanup()
    {
        if (_directory == null) return;
        try
        {
            File.Delete(Path.Combine(_directory, _release.FileName + ".part"));
            File.Delete(Path.Combine(_directory, _release.FileName));
            Directory.Delete(_directory); // Never recursively delete unrelated files.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _package = null;
    }
}
