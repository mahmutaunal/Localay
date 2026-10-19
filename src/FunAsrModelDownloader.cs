// FunAsrModelDownloader - one-click download of the SenseVoiceSmall ONNX model
// (int8 quantized, ~239 MB) from Hugging Face for use with sherpa-onnx.
//
// Pattern: mirrors SupertonicModelDownloader (popup progress window, resume support).
// Downloads model.int8.onnx + tokens.txt into app/AudioModel/FunASR/.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace AlpWareStudio.Localay
{
    public class FunAsrModelDownloader
    {
        private const string BaseUrl =
            "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/";

        // Download both int8 (fast, ~239 MB) and fp32 (accurate, ~938 MB) so the user can
        // switch precision in Settings without re-downloading. tokens.txt is shared.
        private static readonly string[] RequiredFiles =
        {
            "model.int8.onnx",
            "model.onnx",
            "tokens.txt"
        };

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30) // per-file timeout (fp32 ~938 MB)
        };

        private ProgressBar? _progressBar;
        private TextBlock? _statusText;
        private Window? _statusWindow;
        private bool _isDownloading;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Downloads the SenseVoice int8 model + tokens into app/AudioModel/FunASR/.
        /// Skips files that already exist and match the remote size.
        /// Returns true if every required file is present at the end.
        /// </summary>
        public async Task<bool> DownloadAsync()
        {
            if (_isDownloading)
            {
                MessageBox.Show("A FunASR model download is already in progress.",
                    "Download in progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            string modelRoot = ConfigManager.Instance._funAsrModelFolderPath;
            try
            {
                _isDownloading = true;
                _cts = new CancellationTokenSource();
                ShowStatusWindow();

                UpdateStatus("Checking model files...", 0, indeterminate: true);

                var fileInfos = new System.Collections.Generic.List<(string Rel, long Size)>();
                foreach (var rel in RequiredFiles)
                {
                    long size = await GetRemoteSizeAsync(rel);
                    if (size <= 0)
                        throw new InvalidOperationException($"Could not determine size of {rel}");
                    fileInfos.Add((rel, size));
                }

                long totalBytes = fileInfos.Sum(f => f.Size);
                long completedBytes = 0;
                int fileIndex = 0;
                int fileCount = fileInfos.Count;

                Directory.CreateDirectory(modelRoot);

                foreach (var (rel, size) in fileInfos)
                {
                    fileIndex++;
                    string localPath = Path.Combine(modelRoot, rel);

                    if (File.Exists(localPath) && new FileInfo(localPath).Length == size)
                    {
                        Console.WriteLine($"FunASR: {rel} already present ({size} bytes), skipping");
                        completedBytes += size;
                        UpdateStatus($"[{fileIndex}/{fileCount}] {rel} (cached)",
                            (int)(completedBytes * 100 / totalBytes));
                        continue;
                    }

                    UpdateStatus($"[{fileIndex}/{fileCount}] Downloading {rel} ({FormatSize(size)})...",
                        (int)(completedBytes * 100 / totalBytes));

                    long written = await DownloadFileAsync(rel, localPath, size, fileIndex, fileCount,
                        completedBytes, totalBytes, _cts.Token);
                    completedBytes += written;
                }

                UpdateStatus("Download complete!", 100);
                await Task.Delay(1500);
                CloseStatusWindow();
                return true;
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Download cancelled.", 0);
                await Task.Delay(1500);
                CloseStatusWindow();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FunASR download error: {ex.Message}");
                CloseStatusWindow();
                MessageBox.Show(
                    $"Error downloading FunASR model: {ex.Message}",
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

        /// <summary>
        /// Returns true if a usable SenseVoice model (model.int8.onnx or model.onnx + tokens.txt)
        /// is present in the FunASR model folder (root or any subfolder).
        /// </summary>
        public static bool IsModelInstalled()
        {
            string root = ConfigManager.Instance._funAsrModelFolderPath;
            if (!Directory.Exists(root)) return false;

            if (HasModelInDir(root)) return true;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (HasModelInDir(dir)) return true;
            }
            return false;
        }

        private static bool HasModelInDir(string dir) =>
            File.Exists(Path.Combine(dir, "tokens.txt")) &&
            (File.Exists(Path.Combine(dir, "model.int8.onnx")) ||
             File.Exists(Path.Combine(dir, "model.onnx")));

        // ============================================================
        // Internal
        // ============================================================

        private async Task<long> GetRemoteSizeAsync(string relPath)
        {
            string url = BaseUrl + relPath;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                        return resp.Content.Headers.ContentLength ?? 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"HEAD {relPath} attempt {attempt} failed: {ex.Message}");
                }
                await Task.Delay(1000 * attempt);
            }
            return 0;
        }

        private async Task<long> DownloadFileAsync(string relPath, string localPath, long expectedSize,
            int fileIndex, int fileCount, long baseBytes, long totalBytes, CancellationToken token)
        {
            string url = BaseUrl + relPath;
            const int maxRetries = 3;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    long existing = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existing > 0 && expectedSize > 0)
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    resp.EnsureSuccessStatusCode();

                    bool isResume = existing > 0 && existing < expectedSize;
                    using var src = await resp.Content.ReadAsStreamAsync(token);

                    FileStream fs;
                    if (isResume)
                    {
                        fs = new FileStream(localPath, FileMode.Append, FileAccess.Write, FileShare.None);
                    }
                    else
                    {
                        fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        existing = 0;
                    }

                    try
                    {
                        byte[] buffer = new byte[64 * 1024];
                        long fileWritten = existing;
                        int read;
                        long lastReport = 0;
                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read, token);
                            fileWritten += read;
                            long overall = baseBytes + fileWritten;
                            int overallPct = totalBytes > 0 ? (int)(overall * 100 / totalBytes) : 0;
                            if (fileWritten - lastReport > 256 * 1024 || fileWritten == expectedSize)
                            {
                                lastReport = fileWritten;
                                UpdateStatus(
                                    $"[{fileIndex}/{fileCount}] {relPath} - {FormatSize(fileWritten)}/{FormatSize(expectedSize)}",
                                    overallPct);
                            }
                        }
                        return fileWritten;
                    }
                    finally
                    {
                        await fs.FlushAsync(token);
                        fs.Close();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Download {relPath} attempt {attempt} failed: {ex.Message}");
                    if (attempt == maxRetries) throw;
                    await Task.Delay(1500 * attempt, token);
                }
            }
            return 0;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
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
                Title = "FunASR Model Download",
                Width = 460,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = true
            };
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Preparing download...",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = System.Windows.Media.Brushes.Black
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

            grid.Children.Add(_statusText);
            grid.Children.Add(_progressBar);
            _statusWindow.Content = grid;
            _statusWindow.Show();
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
                _statusWindow?.Close();
                _statusWindow = null;
                _progressBar = null;
                _statusText = null;
            });
        }
    }
}