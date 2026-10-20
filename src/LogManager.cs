using System.IO;
using System.Text.Json;

namespace AlpWareStudio.Localay;

public sealed class LogManager
{
    private static readonly Lazy<LogManager> _instance = new(() => new LogManager());
    public static LogManager Instance => _instance.Value;
    private readonly object _fileLock = new();
    private bool _contentDiagnosticsEnabled;
    private readonly string _contentDirectory = Path.Combine(AppPaths.LogDirectory, "Content");

    private LogManager()
    {
        // Consent lasts for one process only. Do not retain previous-session content diagnostics.
        try { if (Directory.Exists(_contentDirectory)) Directory.Delete(_contentDirectory, true); }
        catch (IOException) { Console.WriteLine("Previous diagnostics could not be removed; review the Localay log folder before sharing."); }
        catch (UnauthorizedAccessException) { Console.WriteLine("Previous diagnostics could not be removed; review the Localay log folder before sharing."); }
    }

    public bool ContentDiagnosticsEnabled { get { lock (_fileLock) return _contentDiagnosticsEnabled; } }

    public void SetContentDiagnostics(bool enabled)
    {
        lock (_fileLock)
        {
            if (enabled) Directory.CreateDirectory(_contentDirectory);
            _contentDiagnosticsEnabled = enabled;
            if (!enabled && Directory.Exists(_contentDirectory)) Directory.Delete(_contentDirectory, true);
        }
    }

    public void WriteContentLine(string message)
    {
        lock (_fileLock)
        {
            if (_contentDiagnosticsEnabled) Console.WriteLine(CredentialRedactor.Redact(message));
        }
    }

    public void WriteDiagnostic(string fileName, string content)
    {
        lock (_fileLock)
        {
            if (!_contentDiagnosticsEnabled) return;
            if (fileName != Path.GetFileName(fileName)) throw new ArgumentException("A diagnostic file name is required.");
            try
            {
                string safe = CredentialRedactor.Redact(content);
                // Keep a bounded last-response snapshot rather than an unbounded history.
                if (safe.Length > 1_000_000) safe = safe[..1_000_000];
                AppPaths.WriteAtomic(Path.Combine(_contentDirectory, fileName), safe);
            }
            catch (IOException) { System.Diagnostics.Debug.WriteLine("Could not save content diagnostics."); }
            catch (UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine("Could not save content diagnostics."); }
        }
    }

    public void LogOcrResponse(string jsonData) => WriteDiagnostic("last_ocr_response.json", jsonData);
    public void LogLlmRequest(string prompt, string jsonData) =>
        WriteDiagnostic("last_llm_request_sent.txt", prompt + Environment.NewLine + jsonData);
    public void LogLlmReply(string jsonResponse) => WriteDiagnostic("last_llm_reply_received.txt", jsonResponse);
}
