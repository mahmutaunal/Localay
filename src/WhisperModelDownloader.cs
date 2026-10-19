// WhisperModelDownloader - in-app download of whisper.cpp ggml models.
//
// Before this existed, Whisper models had to be fetched manually and dropped into
// app/AudioModel/ by hand; the model dropdown in Settings only listed *.bin files that
// were already there.
//
// Pattern: mirrors FunAsrModelDownloader (popup progress window, resume, retry).
// Downloads a single ggml-*.bin into app/AudioModel/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
// Both WPF and WinForms are referenced by this project, so the overlapping control and
// enum names must be disambiguated explicitly.
using MessageBox = System.Windows.MessageBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// One downloadable Whisper model. <see cref="FileBaseName"/> is what the model dropdown and
    /// config store (a *.bin name without the extension), so it must match the remote file name.
    /// </summary>
    public class WhisperModelInfo
    {
        public string FileBaseName { get; }
        /// <summary>
        /// Exact size published by Hugging Face, used for the size column and the free-space check.
        /// The download itself verifies against the live Content-Length, so a repo update only
        /// makes this display value stale — it can never corrupt a download.
        /// </summary>
        public long SizeBytes { get; }
        public bool MultiLingual { get; }
        /// <summary>Localization key describing the speed/accuracy trade-off (WhisperQ_*).</summary>
        public string QualityKey { get; }
        public bool Recommended { get; }

        public WhisperModelInfo(string fileBaseName, long approxBytes, bool multiLingual,
                                string qualityKey, bool recommended = false)
        {
            FileBaseName = fileBaseName;
            SizeBytes = approxBytes;
            MultiLingual = multiLingual;
            QualityKey = qualityKey;
            Recommended = recommended;
        }

        public string FileName => FileBaseName + ".bin";

        /// <summary>Name without the "ggml-" prefix, for display.</summary>
        public string ShortName => FileBaseName.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase)
            ? FileBaseName.Substring(5)
            : FileBaseName;

        public bool IsQuantized => FileBaseName.Contains("-q5_") || FileBaseName.Contains("-q8_");

        public string SizeText => WhisperModelDownloader.FormatSize(SizeBytes);

        public bool IsInstalled => WhisperModelDownloader.IsModelInstalled(FileBaseName);
    }

    public class WhisperModelDownloader
    {
        private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

        // File names verified against the ggerganov/whisper.cpp repo listing, and sizes taken from
        // its tree API. Note the quantization suffix is NOT uniform: tiny/base/small ship q5_1
        // while medium/large ship q5_0 — guessing one scheme for all yields 404s.
        // Quantized builds are listed first at each tier: they are a fraction of the size for a
        // small accuracy cost, which is the right default for real-time game audio.
        private static readonly List<WhisperModelInfo> _catalog = new List<WhisperModelInfo>
        {
            // Multilingual
            new WhisperModelInfo("ggml-tiny-q5_1",             32_152_673, true,  "WhisperQ_Fastest"),
            new WhisperModelInfo("ggml-tiny",                  77_691_713, true,  "WhisperQ_Fastest"),
            new WhisperModelInfo("ggml-base-q5_1",             59_707_625, true,  "WhisperQ_Fast"),
            new WhisperModelInfo("ggml-base",                 147_951_465, true,  "WhisperQ_Fast"),
            new WhisperModelInfo("ggml-small-q5_1",           190_085_487, true,  "WhisperQ_Balanced", recommended: true),
            new WhisperModelInfo("ggml-small",                487_601_967, true,  "WhisperQ_Balanced"),
            new WhisperModelInfo("ggml-medium-q5_0",          539_212_467, true,  "WhisperQ_HighAccuracy"),
            new WhisperModelInfo("ggml-medium",             1_533_763_059, true,  "WhisperQ_HighAccuracy"),
            new WhisperModelInfo("ggml-large-v3-turbo-q5_0",  574_041_195, true,  "WhisperQ_LargeTurbo", recommended: true),
            new WhisperModelInfo("ggml-large-v3-turbo",     1_624_555_275, true,  "WhisperQ_LargeTurbo"),
            new WhisperModelInfo("ggml-large-v3-q5_0",      1_081_140_203, true,  "WhisperQ_BestAccuracy"),
            new WhisperModelInfo("ggml-large-v3",           3_095_033_483, true,  "WhisperQ_BestAccuracy"),

            // English-only: same size as the multilingual build but more accurate on English
            new WhisperModelInfo("ggml-tiny.en-q5_1",          32_166_155, false, "WhisperQ_Fastest"),
            new WhisperModelInfo("ggml-base.en-q5_1",          59_721_011, false, "WhisperQ_Fast"),
            new WhisperModelInfo("ggml-small.en-q5_1",        190_098_681, false, "WhisperQ_Balanced"),
        };

        public static IReadOnlyList<WhisperModelInfo> Catalog => _catalog;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(60) // per-file timeout (large-v3 is ~3 GB)
        };

        private ProgressBar? _progressBar;
        private TextBlock? _statusText;
        private Window? _statusWindow;
        private bool _isDownloading;
        private CancellationTokenSource? _cts;
        // Set while the window is being closed by us, so the Closing handler does not turn a
        // finished download into a cancellation.
        private bool _closingProgrammatically;

        /// <summary>
        /// True if the given model file is present in app/AudioModel/ with a non-zero size.
        /// </summary>
        public static bool IsModelInstalled(string fileBaseName)
        {
            try
            {
                string path = Path.Combine(ConfigManager.Instance._audioProcessingModelFolderPath,
                                           fileBaseName + ".bin");
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True if at least one *.bin model is present, i.e. Whisper can start at all.
        /// </summary>
        public static bool IsAnyModelInstalled()
        {
            try
            {
                string dir = ConfigManager.Instance._audioProcessingModelFolderPath;
                if (!Directory.Exists(dir)) return false;
                // EndsWith guards against 8.3 aliasing making "*.bin" match longer extensions.
                return Directory.GetFiles(dir, "*.bin")
                    .Any(p => p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Download one ggml model into app/AudioModel/. Resumes a partial file and verifies the
        /// final size against the server, because a truncated *.bin crashes whisper.cpp inside
        /// native code rather than failing cleanly.
        /// Returns true when the model is fully present at the end.
        /// </summary>
        public async Task<bool> DownloadAsync(WhisperModelInfo model)
        {
            if (_isDownloading)
            {
                MessageBox.Show("A Whisper model download is already in progress.",
                    "Download in progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            string modelRoot = ConfigManager.Instance._audioProcessingModelFolderPath;
            string localPath = Path.Combine(modelRoot, model.FileName);
            // Stage into *.part and only rename once the size is verified. A cancelled or failed
            // transfer must never leave a truncated *.bin behind: the model dropdown lists every
            // *.bin it finds, and whisper.cpp crashes in native code on a short model file.
            string partPath = localPath + ".part";

            try
            {
                _isDownloading = true;
                _cts = new CancellationTokenSource();
                ShowStatusWindow();

                UpdateStatus($"Checking {model.FileName}...", 0, indeterminate: true);

                long remoteSize = await GetRemoteSizeAsync(model.FileName);
                if (remoteSize <= 0)
                {
                    throw new InvalidOperationException(
                        $"Could not determine the size of {model.FileName}. Check your internet connection.");
                }

                Directory.CreateDirectory(modelRoot);

                if (File.Exists(localPath) && new FileInfo(localPath).Length == remoteSize)
                {
                    Console.WriteLine($"Whisper: {model.FileName} already present ({remoteSize} bytes), skipping");
                    UpdateStatus($"{model.FileName} is already downloaded.", 100);
                    await Task.Delay(1200);
                    CloseStatusWindow();
                    return true;
                }

                UpdateStatus($"Downloading {model.FileName} ({FormatSize(remoteSize)})...", 0);

                await DownloadFileAsync(model.FileName, partPath, remoteSize, _cts.Token);

                // Verify before publishing: a short file means the transfer was truncated.
                long finalSize = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                if (finalSize != remoteSize)
                {
                    try { File.Delete(partPath); } catch { }
                    throw new IOException(
                        $"Download incomplete: got {FormatSize(finalSize)} of {FormatSize(remoteSize)}. " +
                        "The partial file was removed; please try again.");
                }

                File.Move(partPath, localPath, overwrite: true);

                UpdateStatus("Download complete!", 100);
                await Task.Delay(1500);
                CloseStatusWindow();
                return true;
            }
            catch (OperationCanceledException)
            {
                // The partial file is kept on purpose so the next attempt can resume it.
                UpdateStatus("Download cancelled.", 0);
                await Task.Delay(1500);
                CloseStatusWindow();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Whisper model download error: {ex.Message}");
                CloseStatusWindow();
                MessageBox.Show(
                    $"Error downloading {model.FileName}: {ex.Message}",
                    "Download error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                _isDownloading = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        // ============================================================
        // Internal
        // ============================================================

        private async Task<long> GetRemoteSizeAsync(string fileName)
        {
            string url = BaseUrl + fileName;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                        return resp.Content.Headers.ContentLength ?? 0;
                    Console.WriteLine($"HEAD {fileName} returned {(int)resp.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"HEAD {fileName} attempt {attempt} failed: {ex.Message}");
                }
                await Task.Delay(1000 * attempt);
            }
            return 0;
        }

        private async Task<long> DownloadFileAsync(string fileName, string localPath, long expectedSize,
            CancellationToken token)
        {
            string url = BaseUrl + fileName;
            const int maxRetries = 3;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    long existing = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
                    if (existing > expectedSize) existing = 0; // stale/corrupt file, start over
                    if (existing == expectedSize)
                    {
                        // A previous run finished the transfer but did not get to publish it.
                        // Requesting a range at EOF would return 416 and fail every retry.
                        Console.WriteLine($"Whisper: {fileName} already fully staged, skipping transfer");
                        return existing;
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existing > 0)
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    resp.EnsureSuccessStatusCode();

                    // Only append when the server actually honoured the range request; otherwise
                    // it is sending the whole file again and appending would corrupt the output.
                    bool isResume = existing > 0 &&
                                    resp.StatusCode == System.Net.HttpStatusCode.PartialContent;

                    using var src = await resp.Content.ReadAsStreamAsync(token);

                    FileStream fs;
                    if (isResume)
                    {
                        Console.WriteLine($"Whisper: resuming {fileName} at {FormatSize(existing)}");
                        fs = new FileStream(localPath, FileMode.Append, FileAccess.Write, FileShare.None);
                    }
                    else
                    {
                        fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        existing = 0;
                    }

                    try
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        long written = existing;
                        long lastReport = 0;
                        int read;
                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read, token);
                            written += read;
                            if (written - lastReport > 2 * 1024 * 1024 || written == expectedSize)
                            {
                                lastReport = written;
                                int pct = expectedSize > 0 ? (int)(written * 100 / expectedSize) : 0;
                                UpdateStatus(
                                    $"{fileName} - {FormatSize(written)} / {FormatSize(expectedSize)}",
                                    pct);
                            }
                        }
                        return written;
                    }
                    finally
                    {
                        await fs.FlushAsync(CancellationToken.None);
                        fs.Close();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Download {fileName} attempt {attempt} failed: {ex.Message}");
                    if (attempt == maxRetries) throw;
                    UpdateStatus($"Retrying ({attempt + 1}/{maxRetries})...", 0, indeterminate: true);
                    await Task.Delay(1500 * attempt, token);
                }
            }
            return 0;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private void ShowStatusWindow()
        {
            if (_statusWindow != null)
            {
                _statusWindow.Show();
                return;
            }

            _statusWindow = new Window
            {
                Title = "Whisper Model Download",
                Width = 460,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = true,
                Background = TryBrush("SurfaceBrush", System.Windows.Media.Brushes.White),
                Foreground = TryBrush("TextBrush", System.Windows.Media.Brushes.Black)
            };

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Preparing download...",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = TryBrush("TextBrush", System.Windows.Media.Brushes.Black)
            };
            Grid.SetRow(_statusText, 0);

            _progressBar = new ProgressBar
            {
                Height = 22,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Grid.SetRow(_progressBar, 1);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            cancelButton.Click += (s, e) => Cancel();
            Grid.SetRow(cancelButton, 2);

            grid.Children.Add(_statusText);
            grid.Children.Add(_progressBar);
            grid.Children.Add(cancelButton);
            _statusWindow.Content = grid;

            // Closing the window cancels the transfer instead of leaving it running invisibly.
            _statusWindow.Closing += (s, e) =>
            {
                if (!_closingProgrammatically) Cancel();
            };
            _statusWindow.Show();
        }

        private static System.Windows.Media.Brush TryBrush(string key, System.Windows.Media.Brush fallback)
        {
            try
            {
                if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.Brush b)
                    return b;
            }
            catch { }
            return fallback;
        }

        private void UpdateStatus(string message, int percent, bool indeterminate = false)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_statusText != null) _statusText.Text = message;
                if (_progressBar != null)
                {
                    _progressBar.IsIndeterminate = indeterminate;
                    if (!indeterminate) _progressBar.Value = percent;
                }
            });
        }

        private void CloseStatusWindow()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _closingProgrammatically = true;
                try { _statusWindow?.Close(); } catch { }
                _closingProgrammatically = false;
                _statusWindow = null;
                _progressBar = null;
                _statusText = null;
            });
        }
    }
}
