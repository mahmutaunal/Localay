// WhisperModelPickerWindow - pick a whisper.cpp ggml model and download it in-app,
// instead of fetching the *.bin by hand and copying it into app/AudioModel/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace AlpWareStudio.Localay
{
    public partial class WhisperModelPickerWindow : Window
    {
        /// <summary>
        /// Row shown in the list. Wraps a <see cref="WhisperModelInfo"/> and adds the display-only
        /// text so the DataTemplate can bind directly without value converters.
        /// </summary>
        public class ModelRow
        {
            public WhisperModelInfo Model { get; }

            public ModelRow(WhisperModelInfo model) => Model = model;

            public string ShortName => Model.ShortName;
            public string SizeText => Model.SizeText;

            public string Badge
            {
                get
                {
                    var parts = new List<string>();
                    if (Model.IsInstalled) parts.Add(L("Tag_Installed"));
                    if (Model.Recommended) parts.Add(L("Tag_Recommended"));
                    return parts.Count > 0 ? "· " + string.Join(" · ", parts) : "";
                }
            }

            public string Detail
            {
                get
                {
                    var parts = new List<string>
                    {
                        Model.MultiLingual ? L("Tag_Multilingual") : L("Tag_EnglishOnly"),
                        L(Model.QualityKey)
                    };
                    if (Model.IsQuantized) parts.Add(L("Tag_Quantized"));
                    return string.Join(" · ", parts);
                }
            }

            private static string L(string key) => LocalizationManager.Instance.Strings[key];
        }

        /// <summary>
        /// Base file name (no .bin) of a model that was downloaded during this dialog session,
        /// so the caller can select it in the model dropdown. Null if nothing was downloaded.
        /// </summary>
        public string? DownloadedModelBaseName { get; private set; }

        private bool _isDownloading;

        public WhisperModelPickerWindow()
        {
            InitializeComponent();
            RefreshList();
        }

        private void RefreshList()
        {
            bool includeEnglishOnly = showEnglishOnlyCheckBox.IsChecked == true;

            // Remember the selection across a refresh so the list does not jump after a download.
            string? selected = (modelListBox.SelectedItem as ModelRow)?.Model.FileBaseName;

            var rows = WhisperModelDownloader.Catalog
                .Where(m => m.MultiLingual || includeEnglishOnly)
                .Select(m => new ModelRow(m))
                .ToList();

            modelListBox.ItemsSource = rows;

            if (selected != null)
            {
                modelListBox.SelectedItem = rows.FirstOrDefault(r => r.Model.FileBaseName == selected);
            }

            UpdateDiskWarning();
        }

        private void ModelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            downloadButton.IsEnabled = !_isDownloading && modelListBox.SelectedItem is ModelRow;
            UpdateDiskWarning();
        }

        private void ShowEnglishOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Fires during InitializeComponent before the list exists
            if (modelListBox == null) return;
            RefreshList();
        }

        private void ModelListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isDownloading && modelListBox.SelectedItem is ModelRow) StartDownload();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e) => StartDownload();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Warn when the selected model would not fit on the drive holding app/AudioModel,
        /// or when it is already installed (so a re-download is a deliberate choice).
        /// </summary>
        private void UpdateDiskWarning()
        {
            if (diskWarningTextBlock == null) return;

            if (modelListBox.SelectedItem is not ModelRow row)
            {
                diskWarningTextBlock.Text = "";
                return;
            }

            if (row.Model.IsInstalled)
            {
                diskWarningTextBlock.Text = LocalizationManager.Instance.Strings["Msg_WhisperModelAlreadyInstalled"];
                return;
            }

            try
            {
                string dir = ConfigManager.Instance._audioProcessingModelFolderPath;
                string? root = Path.GetPathRoot(Path.GetFullPath(dir));
                if (!string.IsNullOrEmpty(root))
                {
                    long free = new DriveInfo(root).AvailableFreeSpace;
                    // Ask for a little headroom beyond the file itself.
                    if (free < row.Model.SizeBytes + 100L * 1024 * 1024)
                    {
                        diskWarningTextBlock.Text = string.Format(
                            LocalizationManager.Instance.Strings["Msg_WhisperModelNoDiskSpace"],
                            WhisperModelDownloader.FormatSize(free));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disk space check failed: {ex.Message}");
            }

            diskWarningTextBlock.Text = "";
        }

        private async void StartDownload()
        {
            if (_isDownloading) return;
            if (modelListBox.SelectedItem is not ModelRow row) return;

            try
            {
                _isDownloading = true;
                downloadButton.IsEnabled = false;
                modelListBox.IsEnabled = false;

                var downloader = new WhisperModelDownloader();
                bool ok = await downloader.DownloadAsync(row.Model);

                if (ok)
                {
                    DownloadedModelBaseName = row.Model.FileBaseName;
                    RefreshList();
                    MessageBox.Show(this,
                        string.Format(
                            LocalizationManager.Instance.Strings["Msg_WhisperModelDownloadComplete"],
                            row.Model.ShortName),
                        LocalizationManager.Instance.Strings["Msg_DownloadComplete"],
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error: {ex.Message}", "Download error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isDownloading = false;
                modelListBox.IsEnabled = true;
                downloadButton.IsEnabled = modelListBox.SelectedItem is ModelRow;
            }
        }
    }
}
