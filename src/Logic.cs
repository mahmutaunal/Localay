using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using MessageBox = System.Windows.MessageBox;

namespace AlpWareStudio.Localay
{
    public class Logic
    {
        private static Logic? _instance;
        private List<TextObject> _textObjects;
        private List<TextObject> _textObjectsOld;
        // Copy of the frame being OCR'd, for sampling the overlay background colour once its result is in
        // Only kept while the auto background colour is on; the capture caller owns the input.
        private Bitmap? _ocrSourceFrame;
        private readonly object _ocrSourceFrameLock = new object();
        private Random _random;
        private Grid? _overlayContainer;
        private DateTime _lastOcrRequestTime = DateTime.MinValue;
        private readonly TimeSpan _minOcrInterval = TimeSpan.FromSeconds(0.2);

        private readonly List<string> _audioBatch = new List<string>();
        private readonly object _audioBatchLock = new object();
        private bool _isProcessingAudioBatch = false;
        private bool _hasNewAudioSinceLastTranslation = false;

        private readonly OcrSceneState _ocrScene = new();
        private readonly TranslationRetryState _translationRetry = new();
        private readonly PipelineLifetime _pipelineLifetime = new();
        private readonly TranslationBatchCoordinator _translationBatches = new();
        private long _translationEpoch;
        private readonly PipelineLifetime _captureLifetime = new();
        private bool _deferredOcrScheduled;
        private readonly PipelineSceneTiming _sceneTiming = new();
        internal PipelineTrace? SceneTiming => _sceneTiming.Current;
        private bool _captureNeedsVerification;
        private OcrFrameObservation? _activeOcrDamage;
        public bool AreOverlaysCurrent => (_ocrScene.Latest == null || _ocrScene.IsCurrent) &&
            _textObjects.All(t => t.SourceFrameCurrent);

        internal void ObserveCaptureChanges(FrameChangeMap changes)
        {
            if (changes.HasChanges)
            {
                _captureNeedsVerification = true;
                // Track dirty regions during OCR. Background animation must not cancel
                // recognition of every stationary text block. A moved/resized capture,
                // however, changes the coordinate system and invalidates the whole lease.
                _activeOcrDamage?.Include(changes);
                if (changes.WholeFrame) _captureLifetime.Invalidate();
                // Queued OCR geometry belongs to the earlier image too. A provider's
                // completion must not replay it after a newer capture has moved on.
                _ocrScene.TakeDeferred();
                _deferredOcrObservation = null;
                _sceneTiming.DiscardDeferred();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
                bool visibilityChanged = false;
                foreach (var text in _textObjects)
                {
                    if (!text.SourceFrameCurrent || !text.IntersectsCaptureChange(changes)) continue;
                    text.SourceFrameCurrent = false;
                    visibilityChanged = true;
                }
                if (visibilityChanged) MonitorWindow.Instance.RefreshOverlays();
            }
            bool hadPending = _settlingOcr.HasPending;
            var ready = _settlingOcr.Observe(changes);
            if (hadPending && !_settlingOcr.HasPending) _lastChangeTime = DateTime.MinValue;
            if (ready != null && MainWindow.Instance.GetIsStarted() && ReferenceEquals(_ocrScene.Latest, ready.Scene))
            {
                try { PublishPreparedOcr(ready); }
                catch (Exception ex)
                {
                    ready.Timing?.Finish(PipelineOutcome.Failed);
                    _captureNeedsVerification = true;
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    OnFinishedThings(true);
                    Console.WriteLine($"Settled OCR publication failed: {ex.Message}");
                }
            }
        }
        internal PipelineLifetime.Lease CaptureSession() => _captureLifetime.Capture();

        private string _lastOcrHash = string.Empty;
        private string _lastTextContent = string.Empty;
        // Last OCR text logged as skipped, so a static screen is logged once instead of every frame
        private string _lastSkipLoggedText = string.Empty;

        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;
        private readonly OcrSettlement<PreparedOcr> _settlingOcr = new();
        private readonly OcrSettleDelay _settleDelay = new();
        private OcrFrameObservation? _deferredOcrObservation;
        internal bool HasPendingOcrSettlement => _settlingOcr.HasPending;
        internal bool NeedsOcrSettleConfirmation => _settlingOcr.NeedsConfirmation;

        // Properties to expose to other classes
        public List<TextObject> TextObjects => _textObjects;
        public List<TextObject> TextObjectsOld => _textObjectsOld;

        // Events
        public event EventHandler<TextObject>? TextObjectAdded;

        // Event when translation is completed
        public event EventHandler<TranslationEventArgs>? TranslationCompleted;

        public bool GetWaitingForTranslationToFinish() => _ocrScene.IsTranslating;

        // Singleton pattern
        public static Logic Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Logic();
                }
                return _instance;
            }
        }

        // Constructor
        private Logic()
        {
            // Private constructor to enforce singleton pattern
            _textObjects = new List<TextObject>();
            _textObjectsOld = new List<TextObject>();
            _random = new Random();
        }

        // Called with each frame sent to OCR. Takes ownership of the copy.
        public void SetOcrSourceFrame(Bitmap? frame)
        {
            // A newer OCR input supersedes any uncommitted geometry/background.
            if (_settlingOcr.HasPending) _lastChangeTime = DateTime.MinValue;
            _settlingOcr.Cancel();
            lock (_ocrSourceFrameLock)
            {
                _ocrSourceFrame?.Dispose();
                _ocrSourceFrame = frame;
            }
        }

        // Set reference to the overlay container
        public void SetOverlayContainer(Grid overlayContainer)
        {
            _overlayContainer = overlayContainer;
        }

        // Get the list of current text objects
        public IReadOnlyList<TextObject> GetTextObjects()
        {
            return _textObjects.AsReadOnly();
        }
        public IReadOnlyList<TextObject> GetTextObjectsOld()
        {
            return _textObjectsOld.AsReadOnly();
        }

        // Called when the application starts
        public async void Init()
        {
            try
            {
                // Initialize resources, settings, etc.
                Console.WriteLine("Logic initialized");

                // Load configuration
                string geminiApiKey = ConfigManager.Instance.GetGeminiApiKey();
                Console.WriteLine($"Loaded Gemini API key: {(string.IsNullOrEmpty(geminiApiKey) ? "Not set" : "Set")}");

                // Load LLM prompt
                string llmPrompt = ConfigManager.Instance.GetLlmPrompt();
                Console.WriteLine($"Loaded LLM prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : $"{llmPrompt.Length} chars")}");

                // Load force cursor visible setting
                // Use the normal Windows/game cursor visibility.

                Console.WriteLine($"Using {MainWindow.Instance.GetSelectedOcrMethod()} (built-in)");

                // Update status message in the UI
                MainWindow.Instance.SetOcrStatus(MainWindow.Instance.GetSelectedOcrMethod());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorInitialization"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        void OnFinishedThings(bool bResetTranslationStatus)
        {
            // Individual response parsers/manga batches also call this method. Only the
            // top-level translation's finally block may release the queued OCR result.
            if (_ocrScene.IsTranslating)
            {
                MonitorWindow.Instance.RefreshOverlays();
                return;
            }
            MonitorWindow.Instance.RefreshOverlays();

            // Hide translation status
            if (bResetTranslationStatus)
            {
                MonitorWindow.Instance.HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();
            }

            if (_ocrScene.HasDeferred && !_deferredOcrScheduled)
            {
                _deferredOcrScheduled = true;
                long generation = _ocrScene.Generation;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (generation != _ocrScene.Generation) return;
                    _deferredOcrScheduled = false;
                    if (GetWaitingForTranslationToFinish()) return;
                    string? latest = _ocrScene.TakeDeferred();
                    var timing = _sceneTiming.TakeDeferred();
                    using var timingScope = PipelineDiagnostics.Use(timing);
                    var observation = _deferredOcrObservation;
                    _deferredOcrObservation = null;
                    if (latest != null && MainWindow.Instance.GetIsStarted())
                        ProcessReceivedTextJsonData(latest, observation);
                }, DispatcherPriority.Background);
            }
        }

        // The last OCR result is fully handled: shown (or being translated) and not waiting to settle,
        // so OCR of an identical frame would change nothing
        public bool IsOcrResultSettled => !_captureNeedsVerification && !_translationRetry.IsDue(DateTimeOffset.UtcNow) && _lastChangeTime == DateTime.MinValue && _lastOcrHash.Length > 0;

        private void InvalidatePendingWork(bool discardSourceFrame = true)
        {
            _settlingOcr.Cancel();
            _deferredOcrObservation = null;
            if (discardSourceFrame) _settleDelay.Reset();
            _sceneTiming.Invalidate(discardSourceFrame);
            _ocrScene.CancelTranslation();
            _deferredOcrScheduled = false;
            if (discardSourceFrame) _captureLifetime.Invalidate();
            _pipelineLifetime.Invalidate();
            _translationEpoch++;
            _prefetchTask = null;
            if (discardSourceFrame) SetOcrSourceFrame(null);
        }

        public void ResetHash()
        {
            // A settings reset must also invalidate rendered text; otherwise identical OCR
            // could reuse a translation made with the previous model/prompt/language.
            ClearAllTextObjects();
            _lastOcrHash = "";
            _lastTextContent = string.Empty;
            _lastChangeTime = DateTime.Now;
        }

        /// <summary>
        /// Retry the current translation bypassing similarity checks.
        /// Clears the last OCR hash/text and triggers translation for the current text objects.
        /// </summary>
        public void RetryCurrentTranslation()
        {
            // Ensure this runs on the UI thread
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!MainWindow.Instance.GetIsStarted()) return;
                    if (_textObjects == null || _textObjects.Count == 0)
                    {
                        Console.WriteLine("No text objects available to retry translation.");
                        return;
                    }

                    if (GetWaitingForTranslationToFinish())
                    {
                        Console.WriteLine("Translation already in progress; retry request ignored.");
                        return;
                    }

                    Console.WriteLine("Retrying current translation (bypassing similarity checks and the translation cache).");
                    _translationRetry.Reset();
                    _bypassTranslationCacheOnce = true;
                    foreach (var textObject in _textObjects) textObject.TextTranslated = "";
                    MonitorWindow.Instance.RefreshOverlays();

                    // Clear the last-hash / last-text so similarity gating is bypassed
                    _lastOcrHash = string.Empty;
                    _lastTextContent = string.Empty;
                    _lastChangeTime = DateTime.MinValue;

                    // Start translation asynchronously
                    _ = TranslateTextObjectsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in RetryCurrentTranslation: {ex.Message}");
                }
            });
        }


        // Process Google Translate JSON response
        private void ProcessGoogleTranslateJson(JsonElement rootElement, bool addToChatBox = true)
        {
            try
            {
                Console.WriteLine("Processing Google Translate response");

                if (rootElement.TryGetProperty("translations", out JsonElement translationsArray) &&
                    translationsArray.ValueKind == JsonValueKind.Array)
                {
                    // Get current target language
                    string targetLanguage = ConfigManager.Instance.GetTargetLanguage().ToLower();

                    // Define RTL (Right-to-Left) languages
                    HashSet<string> rtlLanguages = new HashSet<string> {
                        "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
                    };

                    // Check if target language is RTL
                    bool isRtlLanguage = rtlLanguages.Contains(targetLanguage);

                    // Process each translation
                    for (int i = 0; i < translationsArray.GetArrayLength(); i++)
                    {
                        var translation = translationsArray[i];

                        if (translation.TryGetProperty("id", out JsonElement idElement) &&
                            translation.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                                // Check if this is our combined text block with ID=999
                                if (id == "999")
                                {
                                    // Split the translated text using the separator
                                    string[]? translatedParts = TranslationParts.SplitExact(translatedText, _textObjects.Count);
                                    if (translatedParts == null) continue;

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < translatedParts.Length; j++)
                                    {
                                        // Clean up the translated text - remove any remaining (possibly mangled) ##|||## separators.
                                        // Only fragments around "|||" are removed, so real text such as "#1" is left intact.
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"#{0,2}\s*\|{3}\s*#{0,2}",
                                            ""
                                        );
                                        if (string.IsNullOrWhiteSpace(cleanTranslatedText)) continue;

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!cleanTranslatedText.StartsWith('\u200F'))
                                            {
                                                cleanTranslatedText = "\u200F" + cleanTranslatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the text object with the cleaned translated text
                                        _textObjects[j].TextTranslated = cleanTranslatedText;
                                        _textObjects[j].UpdateUIElement();
                                        Console.WriteLine($"Updated text object at index {j} with translation from Google Translate");
                                    }

                                    // We've processed the combined text block, so we can break out of the loop
                                    break;
                                }
                                else
                                {
                                    // Handle the old way for backward compatibility
                                    // Find the matching text object by ID
                                    var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                    if (matchingTextObj != null)
                                    {

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!translatedText.StartsWith('\u200F'))
                                            {
                                                translatedText = "\u200F" + translatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the corresponding text object
                                        matchingTextObj.TextTranslated = translatedText;
                                        matchingTextObj.UpdateUIElement();
                                        Console.WriteLine($"Updated text object {id} with translation from Google Translate");
                                    }

                                }
                            }
                        }
                    }

                    if (addToChatBox)
                    {
                        AddTranslatedTextObjectsToChatBox();
                    }
                }
                else
                {
                    Console.WriteLine("No translations array found in Google Translate response");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Google Translate JSON: {ex.Message}");
            }
        }

        private string ExtractTextContent(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            StringBuilder textBuilder = new StringBuilder();

            foreach (JsonElement element in resultsElement.EnumerateArray())
            {
                if (element.TryGetProperty("text", out JsonElement textElement))
                {
                    string text = textElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textBuilder.Append(text);
                        textBuilder.Append(' ');
                    }
                }
            }

            return textBuilder.ToString().Trim();
        }

        //! Process the OCR text data, this is before it's been translated
        public void ProcessReceivedTextJsonData(string data) => ProcessReceivedTextJsonData(data, null);

        private void ProcessReceivedTextJsonData(string data, OcrFrameObservation? observation)
        {
            using var sceneProcessingTiming = PipelineDiagnostics.Measure(PipelineWork.SceneProcessing);
            if (!MainWindow.Instance.GetIsStarted()) return;
            if (_settlingOcr.HasPending)
            {
                _settlingOcr.Cancel();
                _lastChangeTime = DateTime.MinValue;
            }
            MainWindow.Instance.SetOCRCheckIsWanted(true);

            try
            {
                // Check if the data is JSON
                if (data.StartsWith("{") && data.EndsWith("}"))
                {
                    // Try to parse as JSON
                    try
                    {
                        // Parse JSON with options that preserve Unicode characters
                        var options = new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        };

                        using JsonDocument doc = JsonDocument.Parse(data, options);
                        JsonElement root = doc.RootElement;

                        // Check if it's an OCR response
                        if (root.TryGetProperty("status", out JsonElement statusElement))
                        {
                            string status = statusElement.GetString() ?? "unknown";

                            if (status == "success" && root.TryGetProperty("results", out JsonElement resultsElement))
                            {
                                // Pre-filter low-confidence characters before block detection
                                JsonElement filteredResults = FilterLowConfidenceCharacters(resultsElement);

                                // Process character-level OCR data using CharacterBlockDetectionManager
                                // Use the filtered results for consistency
                                JsonElement modifiedResults = CharacterBlockDetectionManager.Instance.ProcessCharacterResults(filteredResults);

                                // Filter out text objects that should be ignored based on ignore phrases
                                modifiedResults = FilterIgnoredPhrases(modifiedResults);
                                bool fullyCurrent = true;
                                if (observation != null)
                                    (modifiedResults, fullyCurrent) = observation.FilterResults(modifiedResults);
                                _captureNeedsVerification = !fullyCurrent;

                                // Generate content hash AFTER block detection and filtering
                                string contentHash = GenerateContentHash(modifiedResults);
                                string textContent = ExtractTextContent(modifiedResults);
                                OnboardingTips.OnOcrResult(!string.IsNullOrWhiteSpace(textContent));

                                var scene = OcrSceneSnapshot.FromResults(modifiedResults, ConfigManager.Instance.GetMinTextFragmentSize());
                                bool growing = TextGrowth.IsGrowth(_lastTextContent, textContent);
                                // The configured similarity tolerance may settle noisy OCR at stationary
                                // coordinates, but never authorizes showing a translation for different text.
                                bool sameCandidateText = _lastOcrHash.Length > 0 && _ocrScene.Latest != null &&
                                    (_ocrScene.Latest.HasSameText(scene) ||
                                     (!growing && _ocrScene.Latest.HasSameGeometry(scene) &&
                                      IsTextSimilar(textContent, _lastTextContent, ConfigManager.Instance.GetTextSimilarThreshold(), out _)));
                                bool supersededGameText = MainWindow.Instance.IsCapturingGameWindow &&
                                    _ocrScene.Latest is { } previousScene && !previousScene.HasSameTranslationUnits(scene);
                                if (_ocrScene.Observe(scene))
                                {
                                    // OCR can also change without a pixel delta (language/noise).
                                    // Retain only objects whose exact source and position are verified.
                                    foreach (var text in _textObjects)
                                    {
                                        var block = text.SourceBlock;
                                        if (!scene.Blocks.Any(b => b.Text == block.Text &&
                                            Math.Abs(b.X - block.X) <= 2 && Math.Abs(b.Y - block.Y) <= 2 &&
                                            Math.Abs(b.Width - block.Width) <= 2 && Math.Abs(b.Height - block.Height) <= 2))
                                            text.SourceFrameCurrent = false;
                                    }
                                    _translationRetry.Reset();
                                    // A stationary HUD must not keep an obsolete dialogue request
                                    // alive. Preserve verified visuals, but release translation and
                                    // prefetch ownership so only the latest game text proceeds.
                                    if (supersededGameText || !_textObjects.Any(t => t.SourceFrameCurrent))
                                        InvalidatePendingWork(discardSourceFrame: false);
                                }
                                if (!AreOverlaysCurrent)
                                    MonitorWindow.Instance.RefreshOverlays();

                                // Retain the first observation across settling/rechecks. Each OCR
                                // keeps its own operation scope; a new capture never resets this clock.
                                var observationTiming = PipelineDiagnostics.Current;
                                _sceneTiming.Observe(observationTiming, !AreOverlaysCurrent, scene.BlockCount);
                                using var sceneTimingScope = PipelineDiagnostics.Use(_sceneTiming.Current ?? observationTiming);

                                // Translation is serial, but observation is not blocked by the network.
                                // Preserve the active request's objects until it finishes; hide them if stale.
                                if (GetWaitingForTranslationToFinish())
                                {
                                    // A partially stale result must not be replayed from its
                                    // original unfiltered JSON when the provider completes.
                                    if (fullyCurrent)
                                    {
                                        _ocrScene.Defer(data);
                                        _deferredOcrObservation = observation;
                                        _sceneTiming.Defer(observationTiming);
                                    }
                                    return;
                                }
                                _ocrScene.TakeDeferred(); // A fresh OCR result supersedes any queued one.
                                _deferredOcrObservation = null;
                                _sceneTiming.DiscardDeferred();

                                if (scene.IsEmpty)
                                {
                                    _sceneTiming.Current?.Finish(fullyCurrent ? PipelineOutcome.Empty : PipelineOutcome.Partial);
                                    ClearRenderedTextObjects();
                                    _ocrScene.MarkRendered(scene);
                                    _lastOcrHash = contentHash;
                                    _lastTextContent = textContent;
                                    _lastChangeTime = DateTime.MinValue;
                                    OnFinishedThings(true);
                                    return;
                                }

                                // Text identity controls translation reuse; geometry controls presentation.
                                // Rebuild backgrounds and boxes at their new coordinates without an API call.
                                if (TryRefreshOcrLayout(modifiedResults, scene))
                                {
                                    LogSimilaritySkip(contentHash == _lastOcrHash, 1.0,
                                        ConfigManager.Instance.GetTextSimilarThreshold(), textContent);
                                    _lastOcrHash = contentHash;
                                    _lastTextContent = textContent;
                                    _lastChangeTime = DateTime.MinValue;
                                    OnFinishedThings(true);
                                    FinishSceneTiming();
                                    return;
                                }

                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                TimeSpan quietTime = _settleDelay.Observe(textContent, growing, TextGrowth.EndsSentence(textContent), settleTime);
                                if (settleTime > 0 && observation != null && !_translationRetry.IsDue(DateTimeOffset.UtcNow))
                                {
                                    _lastChangeTime = DateTime.Now;
                                    _lastOcrHash = contentHash;
                                    _lastTextContent = textContent;
                                    _settlingOcr.Begin(new(modifiedResults.Clone(), scene, contentHash, textContent,
                                        resultsElement.GetArrayLength(), settleTime, _sceneTiming.Current), modifiedResults,
                                        observation, _captureLifetime.Capture(), quietTime);
                                    if (!growing || TextGrowth.EndsSentence(textContent)) StartTranslationPrefetch(modifiedResults);
                                    MonitorWindow.Instance.ShowTranslationStatus(true);
                                    ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                    OnFinishedThings(false);
                                    return;
                                }
                                // Legacy inputs without an associated capture cannot use pixel confirmation.
                                if (settleTime > 0 && observation == null && !_translationRetry.IsDue(DateTimeOffset.UtcNow))
                                {
                                    if (sameCandidateText && _lastChangeTime != DateTime.MinValue)
                                    {
                                        double requiredTime = quietTime.TotalSeconds;
                                        if ((DateTime.Now - _lastChangeTime).TotalSeconds < requiredTime)
                                        {
                                            OnFinishedThings(false);
                                            return;
                                        }
                                        _lastChangeTime = DateTime.MinValue;
                                    }
                                    else
                                    {
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;
                                        _lastTextContent = textContent;
                                        _lastSkipLoggedText = string.Empty;
                                        OcrTiming.NewTextDetected(growing
                                            ? $"still typing out, waiting for it to settle: '{OcrTiming.Preview(textContent)}'"
                                            : $"waiting {settleTime:F2}s for it to settle: '{OcrTiming.Preview(textContent)}'");
                                        if (!growing || TextGrowth.EndsSentence(textContent))
                                            StartTranslationPrefetch(modifiedResults);
                                        if (MainWindow.Instance.GetIsStarted())
                                        {
                                            MonitorWindow.Instance.ShowTranslationStatus(true);
                                            ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                        }
                                        OnFinishedThings(false);
                                        return;
                                    }
                                }
                                PublishPreparedOcr(new(modifiedResults, scene, contentHash, textContent,
                                    resultsElement.GetArrayLength(), settleTime, _sceneTiming.Current));
                            }
                            else if (status == "error" && root.TryGetProperty("message", out JsonElement messageElement))
                            {
                                // Display error message
                                string errorMsg = messageElement.GetString() ?? "Unknown error";
                                Console.WriteLine(errorMsg);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"JSON parsing error: {ex.Message}");
                        AddTextObject($"JSON Error: {ex.Message}");
                    }
                }
                else
                {
                    // Just display the raw data
                    AddTextObject(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing socket data: {ex.Message}");
                _sceneTiming.Current?.Finish(PipelineOutcome.Failed);
            }

        }

        private sealed record PreparedOcr(JsonElement Results, OcrSceneSnapshot Scene,
            string ContentHash, string TextContent, int CharacterCount, double SettleTime, PipelineTrace? Timing);

        private void PublishPreparedOcr(PreparedOcr prepared)
        {
            using var scope = PipelineDiagnostics.Use(prepared.Timing);
            var publicationTimer = Stopwatch.StartNew();
            var modifiedResults = prepared.Results;
            var scene = prepared.Scene;
            string contentHash = prepared.ContentHash, textContent = prepared.TextContent;
            double settleTime = prepared.SettleTime;
            // Looks like new stuff
            if (string.IsNullOrWhiteSpace(textContent))
            {
                // Text disappeared: nothing to time
            }
            else if (settleTime > 0)
                OcrTiming.Log("2. Text settled");
            else
                OcrTiming.NewTextDetected($"'{OcrTiming.Preview(textContent)}'");
            _lastOcrHash = contentHash;
            _lastTextContent = textContent;
            _lastSkipLoggedText = string.Empty;
            double scale = BlockDetectionManager.Instance.GetBlockDetectionScale();
            Console.WriteLine($"Character-level processing (scale={scale:F2}): {prepared.CharacterCount} characters → {modifiedResults.GetArrayLength()} blocks");

            _sceneTiming.Current?.Mark(PipelineStage.Settled);
            if (!DisplayOcrResults(modifiedResults))
            {
                _sceneTiming.Current?.Finish(PipelineOutcome.Failed);
                return;
            }
            _ocrScene.MarkRendered(scene);
            MonitorWindow.Instance.RefreshOverlays();
            Console.WriteLine($"OCR publication took {publicationTimer.ElapsedMilliseconds} ms");

            // Add the detected text to the ChatBox
            if (_textObjects.Count > 0)
            {
                // Build a string with all the detected text
                StringBuilder detectedText = new StringBuilder();
                foreach (var textObject in _textObjects)
                {
                    detectedText.AppendLine(textObject.Text);
                }

                // Add to ChatBox with empty translation if translate is disabled
                string combinedText = detectedText.ToString().Trim();
                if (!string.IsNullOrEmpty(combinedText))
                {
                    if (MainWindow.Instance.GetTranslateEnabled())
                    {
                        // If translation is enabled, translate the text
                        if (!GetWaitingForTranslationToFinish())
                        {
                            //Console.WriteLine($"Translating text: {combinedText}");
                            // Translate the text objects
                            _lastChangeTime = DateTime.MinValue;
                            _ = TranslateTextObjectsAsync();
                            return;
                        }
                    }
                    else
                    {
                        // Only add to chat history if translation is disabled.
                        // Note: AddTranslationToHistory already calls
                        // ChatBoxWindow.OnTranslationWasAdded internally, so we must NOT
                        // call it again here — doing so would enqueue the TTS request
                        // twice and cause the text to be spoken twice.
                        _lastChangeTime = DateTime.MinValue;
                        MainWindow.Instance.AddTranslationToHistory(combinedText, combinedText);
                        _sceneTiming.Current?.Finish(PipelineOutcome.RecognitionOnly);

                        // Mirror the one-shot latch normally set at the end of
                        // TranslateTextObjectsAsync: when auto-OCR is off, stop
                        // after this OCR pass so the capture loop does not keep
                        // re-running OCR (OCR once per trigger, no translation).
                        if (!ConfigManager.Instance.IsAutoOCREnabled())
                        {
                            MainWindow.Instance.isStopOCR = true;
                        }
                    }
                }

                OnFinishedThings(true);
            }
            else
            {
                OnFinishedThings(true);
            }
        }

        /// <summary>
        /// Determines if two text strings are similar using normalized Levenshtein similarity.
        /// Order-aware, so it tolerates small OCR noise but does not treat two different
        /// sentences that merely share common words/characters as the same text.
        /// </summary>
        /// <param name="s1">First text string to compare</param>
        /// <param name="s2">Second text string to compare</param>
        /// <param name="threshold">Similarity threshold (0.0-1.0) to consider texts as similar</param>
        /// <param name="similarity">The computed similarity score (0.0-1.0)</param>
        /// <returns>True if texts are considered similar, false otherwise</returns>
        private bool IsTextSimilar(string s1, string s2, double threshold, out double similarity)
        {
            similarity = 0.0;

            // Handle special cases
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) { similarity = 1.0; return true; }
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return false;

            string n1 = NormalizeTextForComparison(s1);
            string n2 = NormalizeTextForComparison(s2);
            if (n1 == n2) { similarity = 1.0; return true; }

            // For very short strings, only exact matching (already checked above) counts
            if (n1.Length < 5 || n2.Length < 5) return false;

            // Levenshtein similarity can never exceed the length ratio, so skip the expensive
            // computation when the lengths alone already rule out a match
            double lengthRatio = Math.Min(n1.Length, n2.Length) / (double)Math.Max(n1.Length, n2.Length);
            if (lengthRatio < threshold)
            {
                similarity = lengthRatio;
                return false;
            }

            similarity = CalculateTextSimilarity(n1, n2);
            return similarity >= threshold;
        }

        /// <summary>
        /// Logs why an OCR result was skipped as similar to the previous one, so false positives can be diagnosed.
        /// Each distinct OCR text is logged only once, since a static screen is skipped on every frame.
        /// </summary>
        private void LogSimilaritySkip(bool hashMatch, double similarity, double threshold, string textContent)
        {
            static string Truncate(string text) =>
                text.Length <= 80 ? text : text.Substring(0, 80) + "...";

            if (textContent == _lastSkipLoggedText) return;
            _lastSkipLoggedText = textContent;

            if (hashMatch)
            {
                Console.WriteLine("Content is similar to previous, skipping translation (hash match)");
                return;
            }

            Console.WriteLine($"Content is similar to previous, skipping translation (similarity={similarity:F2} >= threshold={threshold:F2})");
            LogManager.Instance.WriteContentLine($"  prev: '{Truncate(_lastTextContent)}'");
            LogManager.Instance.WriteContentLine($"  new:  '{Truncate(textContent)}'");
        }

        // Filter results array to remove objects that should be ignored based on ignore phrases
        private JsonElement FilterIgnoredPhrases(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;

            try
            {
                // Create a new JSON array for filtered results
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();

                        // Process each element in the array
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];

                            // Skip items that don't have the text property
                            if (!item.TryGetProperty("text", out var textElement))
                            {
                                // Include items without text values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }

                            // Get the text from the element
                            string text = textElement.GetString() ?? "";

                            // Check if we should ignore this text
                            var (shouldIgnore, filteredText) = ShouldIgnoreText(text);

                            if (shouldIgnore)
                            {
                                // Skip this element entirely
                                continue;
                            }

                            // If the text was filtered but not ignored completely
                            if (filteredText != text)
                            {
                                // We need to create a new JSON object with the filtered text
                                writer.WriteStartObject();

                                // Copy all properties except 'text'
                                foreach (var property in item.EnumerateObject())
                                {
                                    if (property.Name != "text")
                                    {
                                        property.WriteTo(writer);
                                    }
                                }

                                // Write the filtered text
                                writer.WritePropertyName("text");
                                writer.WriteStringValue(filteredText);

                                writer.WriteEndObject();
                            }
                            else
                            {
                                // No change to the text, write the entire item
                                item.WriteTo(writer);
                            }
                        }

                        writer.WriteEndArray();
                        writer.Flush();

                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error filtering ignored phrases: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        // Check if a text block should be ignored based on ignore phrases.
        // - Exact match / regex on the whole block drop the block.
        // - Contains phrases are removed from the text.
        // - Exact-match phrases also drop single lines: block detection joins nearby lines with '\n',
        //   so a UI label ("Skip") next to dialogue would otherwise never match.
        // Exact match and regex are then checked again on what is left, so "HP Menu" with
        // Contains "HP" and Exact "Menu" is dropped.
        private (bool ShouldIgnore, string FilteredText) ShouldIgnoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (true, string.Empty);

            var ignoreSet = ConfigManager.Instance.GetIgnorePhraseSet();
            if (ignoreSet.Phrases.Count == 0)
                return (false, text); // No phrases to check, keep the text as is

            if (MatchesWholeBlock(text, ignoreSet))
                return (true, string.Empty);

            // 1. Remove "Contains" phrases
            string filteredText = text;
            foreach (var (phrase, matchType) in ignoreSet.Phrases)
            {
                if (matchType == IgnorePhraseMatchType.Contains)
                    filteredText = RemoveContainsPhrase(filteredText, phrase);
            }

            // 2. Drop lines that are exactly an exact-match phrase
            if (filteredText.Contains('\n'))
            {
                var keptLines = filteredText.Split('\n').Where(line => !ignoreSet.Phrases.Any(p =>
                    p.MatchType == IgnorePhraseMatchType.ExactMatch && IsExactIgnoreMatch(line, p.Phrase)));
                filteredText = string.Join("\n", keptLines);
            }

            if (filteredText == text)
                return (false, text);

            filteredText = CleanUpFilteredText(filteredText);
            // Only punctuation left over from a removed phrase ("Loading..." -> "...") is noise too
            if (TrimIgnoreNoise(filteredText).Length == 0)
            {
                Console.WriteLine("Ignoring text because it's empty after filtering");
                return (true, string.Empty);
            }

            if (MatchesWholeBlock(filteredText, ignoreSet))
                return (true, string.Empty);

            return (false, filteredText);
        }

        private static bool MatchesWholeBlock(string text, IgnorePhraseSet ignoreSet)
        {
            foreach (var (phrase, matchType) in ignoreSet.Phrases)
            {
                switch (matchType)
                {
                    case IgnorePhraseMatchType.ExactMatch:
                        if (IsExactIgnoreMatch(text, phrase))
                            return true;
                        break;

                    case IgnorePhraseMatchType.RegularExpression:
                        if (ignoreSet.Regexes.TryGetValue(phrase, out var regex))
                        {
                            try
                            {
                                if (regex.IsMatch(text))
                                    return true;
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                LogManager.Instance.WriteContentLine($"Regex timed out for phrase '{phrase}' on text '{text}'");
                            }
                        }
                        break;
                }
            }
            return false;
        }

        // Remove every occurrence of a phrase (case-insensitive). In space-separated scripts only
        // whole words are removed, so "HP" leaves "WHIP" alone but still strips "HP100" -> "100".
        // Scripts written without spaces (Japanese, Chinese, Thai, Korean particles) match anywhere.
        private static string RemoveContainsPhrase(string text, string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return text;

            var sb = new StringBuilder(text.Length);
            int copyFrom = 0;
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int index = text.IndexOf(phrase, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                int end = index + phrase.Length;
                bool joinsWordBefore = index > 0 && ContinuesWord(text[index - 1], phrase[0]);
                bool joinsWordAfter = end < text.Length && ContinuesWord(phrase[phrase.Length - 1], text[end]);

                if (joinsWordBefore || joinsWordAfter)
                {
                    searchFrom = index + 1;
                    continue;
                }

                sb.Append(text, copyFrom, index - copyFrom);
                copyFrom = end;
                searchFrom = end;
            }

            if (copyFrom == 0)
                return text;
            sb.Append(text, copyFrom, text.Length - copyFrom);
            return sb.ToString();
        }

        // True when two adjacent characters belong to the same word of a space-separated script
        private static bool ContinuesWord(char a, char b)
        {
            static bool IsSpacedScriptLetter(char c) =>
                char.IsLetter(c) && (c < 0x0590 || (c >= 0x1E00 && c <= 0x1FFF)); // Latin incl. Vietnamese, Greek, Cyrillic, Armenian

            return (IsSpacedScriptLetter(a) && IsSpacedScriptLetter(b)) ||
                   (char.IsDigit(a) && char.IsDigit(b));
        }

        // Tidy up after removals: collapse the spaces left behind, trim lines, drop empty lines
        private static string CleanUpFilteredText(string text)
        {
            var lines = text.Split('\n')
                .Select(line => MultipleSpacesRegex.Replace(line, " ").Trim())
                .Where(line => line.Length > 0);
            return string.Join("\n", lines);
        }

        private static readonly Regex MultipleSpacesRegex = new Regex(@"[ \t\u3000]{2,}", RegexOptions.Compiled);

        // Exact match that tolerates what OCR adds around a word: surrounding whitespace and
        // punctuation ("Menu " / "Menu." match "Menu"). A phrase made only of punctuation
        // ("...") is compared on whitespace-trimmed text instead.
        private static bool IsExactIgnoreMatch(string text, string phrase)
        {
            string core = TrimIgnoreNoise(phrase);
            if (core.Length == 0)
                return text.Trim().Equals(phrase.Trim(), StringComparison.OrdinalIgnoreCase);
            return TrimIgnoreNoise(text).Equals(core, StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimIgnoreNoise(string value)
        {
            int start = 0, end = value.Length;
            while (start < end && (char.IsWhiteSpace(value[start]) || char.IsPunctuation(value[start]))) start++;
            while (end > start && (char.IsWhiteSpace(value[end - 1]) || char.IsPunctuation(value[end - 1]))) end--;
            return value.Substring(start, end - start);
        }

        private bool TryRefreshOcrLayout(JsonElement results, OcrSceneSnapshot scene)
        {
            if (_textObjects.Count == 0) return false;
            var texts = GetTranslatableTexts(results);
            if (!texts.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(
                _textObjects.Select(t => t.Text).OrderBy(t => t, StringComparer.Ordinal), StringComparer.Ordinal)) return false;
            if (_translationRetry.IsDue(DateTimeOffset.UtcNow) &&
                _textObjects.Any(t => string.IsNullOrWhiteSpace(t.TextTranslated))) return false;
            if (_ocrScene.IsCurrent && _textObjects.All(t => t.SourceFrameCurrent)) return true;
            // Repeated labels need the normal matcher/translation path; list position
            // alone does not identify which occurrence has moved into the viewport.
            if (texts.Distinct(StringComparer.Ordinal).Count() != texts.Count) return false;
            if (!DisplayOcrResults(results)) return false;
            _ocrScene.MarkRendered(scene);
            OcrTiming.Log("2. OCR layout updated", "existing translations repositioned without a service request");
            return true;
        }

        // Display OCR results from JSON - processes character-level blocks
        private bool DisplayOcrResults(JsonElement resultsElement)
        {
            // Taken over from the field so a frame stored meanwhile cannot dispose it while it is in use
            Bitmap? sourceBitmap = null;
            if (ConfigManager.Instance.IsAutoSetOverlayBackground())
            {
                lock (_ocrSourceFrameLock)
                {
                    sourceBitmap = _ocrSourceFrame;
                    _ocrSourceFrame = null;
                }
            }

            try
            {
                if (resultsElement.ValueKind != JsonValueKind.Array) return false;
                int minimum = ConfigManager.Instance.GetMinTextFragmentSize();
                var raw = new List<TempTextBlock>();
                foreach (var item in resultsElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("text", out var textValue) || !item.TryGetProperty("confidence", out var confidence)) continue;
                    string text = textValue.GetString() ?? "";
                    if (text.Length < minimum || string.IsNullOrWhiteSpace(text)) continue;
                    var (x, y, width, height) = ReadRect(item);
                    int? bubble = item.TryGetProperty("bubble_id", out var bubbleValue) && bubbleValue.ValueKind == JsonValueKind.Number
                        ? bubbleValue.GetInt32() : null;
                    raw.Add(new TempTextBlock { Text = text, X = x, Y = y, Width = width, Height = height,
                        Confidence = confidence.GetDouble(), BubbleId = bubble,
                        LineHeights = new() { ReadSourceLineHeight(item, text, width, height) } });
                }
                var blocks = raw;
                if (ConfigManager.Instance.IsAutoMergeOverlappingTextEnabled() && raw.Count > 0)
                {
                    var (merged, bounds) = MergeOverlappingTextBlocks(raw);
                    blocks = merged.Select((block, i) => new TempTextBlock { Text = block.Text,
                        X = bounds[i].MinX, Y = bounds[i].MinY, Width = bounds[i].MaxX - bounds[i].MinX,
                        Height = bounds[i].MaxY - bounds[i].MinY, Confidence = block.Confidence,
                        BubbleId = block.BubbleId, LineHeights = block.LineHeights }).ToList();
                }
                var previous = _textObjects.ToArray();
                var matches = OcrBlockMatcher.Match(previous.Select(t => t.SourceBlock).ToArray(),
                    blocks.Select(b => new OcrSceneBlock(b.Text, b.X, b.Y, b.Width, b.Height)).ToArray());
                var prepared = new List<TextObject>(blocks.Count);
                for (int i = 0; i < blocks.Count; i++)
                {
                    var block = blocks[i];
                    double lineHeight = OverlayFontPolicy.Median(block.LineHeights);
                    var old = matches[i] >= 0 ? previous[matches[i]] : null;
                    var source = new OcrSceneBlock(block.Text, block.X, block.Y, block.Width, block.Height);
                    if (old != null && old.CanReuseSource(source, lineHeight)) prepared.Add(old);
                    else
                    {
                        var created = CreateTextObjectAtPosition(block.Text, block.X, block.Y, block.Width, block.Height,
                            block.Confidence, sourceBitmap, lineHeight, old,
                            blocks.Where((b, index) => index != i).Select(b => new OverlayBox(b.X, b.Y, b.Width, b.Height)).ToArray());
                        if (created == null) { _captureNeedsVerification = true; return false; }
                        prepared.Add(created);
                    }
                }
                // Publish a complete prepared set. No factory attaches partial borders or
                // erased patches while the rest of this observation is still being built.
                _textObjects = prepared;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing OCR visuals: {ex.GetType().Name}");
                _captureNeedsVerification = true;
                return false;
            }
            finally { sourceBitmap?.Dispose(); }
        }

        // Keep the source line metric even when a paragraph has multiple lines/columns.
        private static double ReadSourceLineHeight(JsonElement item, string text, double width, double height)
        {
            int lines = item.TryGetProperty("line_count", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int n) ? n : 0;
            double measured = item.TryGetProperty("source_line_height", out var metric) && metric.ValueKind == JsonValueKind.Number && metric.TryGetDouble(out double value) ? value : 0;
            bool vertical = item.TryGetProperty("is_vertical", out var orientation) && orientation.ValueKind == JsonValueKind.True;
            return OverlayFontPolicy.SourceLineHeight(width, height, text, lines, vertical, measured);
        }

        // Bounding box of an OCR result item from its "rect" points (zero size when missing)
        private static (double X, double Y, double Width, double Height) ReadRect(JsonElement item)
        {
            if (!item.TryGetProperty("rect", out JsonElement boxElement) || boxElement.ValueKind != JsonValueKind.Array)
                return (0, 0, 0, 0);

            try
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                for (int p = 0; p < boxElement.GetArrayLength(); p++)
                {
                    if (boxElement[p].ValueKind == JsonValueKind.Array &&
                        boxElement[p].GetArrayLength() >= 2)
                    {
                        double pointX = boxElement[p][0].GetDouble();
                        double pointY = boxElement[p][1].GetDouble();

                        minX = Math.Min(minX, pointX);
                        minY = Math.Min(minY, pointY);
                        maxX = Math.Max(maxX, pointX);
                        maxY = Math.Max(maxY, pointY);
                    }
                }

                if (minX > maxX)
                    return (0, 0, 0, 0);
                return (minX, minY, maxX - minX, maxY - minY);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing rect: {ex.Message}");
                return (0, 0, 0, 0);
            }
        }

        // Match DisplayOcrResults' fragment filter and merging so prefetch cache keys
        // stay identical. Game prefetch may prioritize caption candidates before batching.
        private List<string> GetTranslatableTexts(JsonElement resultsElement, bool prioritizeGame = false)
        {
            var texts = new List<string>();
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return texts;

            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();
            bool autoMergeEnabled = ConfigManager.Instance.IsAutoMergeOverlappingTextEnabled();
            var tempBlocks = new List<TempTextBlock>();

            foreach (JsonElement item in resultsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("text", out JsonElement textElement) ||
                    !item.TryGetProperty("confidence", out JsonElement confElement))
                    continue;

                string text = textElement.GetString() ?? "";
                if (text.Length < minTextFragmentSize)
                    continue;

                if (autoMergeEnabled || prioritizeGame)
                {
                    var (x, y, width, height) = ReadRect(item);
                    int? bubbleId = item.TryGetProperty("bubble_id", out JsonElement bubbleElement) && bubbleElement.ValueKind == JsonValueKind.Number
                        ? bubbleElement.GetInt32()
                        : null;
                    tempBlocks.Add(new TempTextBlock { Text = text, X = x, Y = y, Width = width, Height = height, Confidence = confElement.GetDouble(), BubbleId = bubbleId, LineHeights = new() { ReadSourceLineHeight(item, text, width, height) } });
                }
                else
                {
                    texts.Add(text);
                }
            }

            if (autoMergeEnabled && tempBlocks.Count > 0)
            {
                var (merged, bounds) = MergeOverlappingTextBlocks(tempBlocks);
                // Match DisplayOcrResults: priority must use original source bounds,
                // not any geometry expanded while merging adjacent text blocks.
                tempBlocks = merged.Select((block, i) => new TempTextBlock { Text = block.Text,
                    X = bounds[i].MinX, Y = bounds[i].MinY, Width = bounds[i].MaxX - bounds[i].MinX,
                    Height = bounds[i].MaxY - bounds[i].MinY }).ToList();
            }

            if (prioritizeGame)
                return OrderGameTranslationTexts(tempBlocks.Select(b => new OcrSceneBlock(b.Text, b.X, b.Y, b.Width, b.Height)))
                    .Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (autoMergeEnabled) texts.AddRange(tempBlocks.Select(b => b.Text));

            // CreateTextObjectAtPosition skips blank text
            return texts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private string[] OrderGameTranslationTexts(IEnumerable<OcrSceneBlock> sourceBlocks)
        {
            var blocks = sourceBlocks.ToArray();
            var texts = blocks.Select(b => b.Text).ToArray();
            if (!MainWindow.Instance.IsCapturingGameWindow) return texts;
            // SourceBlock/OCR rectangles are physical capture pixels. Use the same
            // render DPI as the overlay rather than comparing them with logical DIPs.
            var monitor = MonitorWindow.Instance;
            DpiHelper.GetWindowRenderDpi(monitor, out double scaleX, out double scaleY);
            var boxes = blocks.Select(b => new OverlayBox(b.X, b.Y, b.Width, b.Height)).ToArray();
            return SubtitlePanelPolicy.TranslationOrder(boxes, texts, monitor.ActualWidth * scaleX, monitor.ActualHeight * scaleY)
                .Select(i => texts[i]).ToArray();
        }

        // Create a text object at the specified position with confidence info
        private TextObject? CreateTextObjectAtPosition(string text, double x, double y, double width, double height, double confidence, Bitmap? sourceBitmap, double sourceLineHeightPixels = 0, TextObject? previous = null, IReadOnlyList<OverlayBox>? protectedSources = null)
        {
            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    return Application.Current.Dispatcher.Invoke(() =>
                        CreateTextObjectAtPosition(text, x, y, width, height, confidence, sourceBitmap, sourceLineHeightPixels, previous, protectedSources));
                }

                // Store current capture position with the text object
                var captureGeometry = new OverlayPixelGeometry(x, y, width, height, sourceLineHeightPixels);
                DpiHelper.GetWindowRenderDpi(MonitorWindow.Instance, out double renderScaleX, out double renderScaleY);
                var logical = captureGeometry.ToDips(renderScaleX, renderScaleY);
                (x, y, width, height) = (logical.X, logical.Y, logical.Width, logical.Height);

                int captureX = _currentCaptureX;
                int captureY = _currentCaptureY;

                // Validate input parameters
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot create text object with empty text");
                    return null;
                }

                // Ensure width and height are valid
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
                {
                    width = 0; // Let the text determine natural width
                }

                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0)
                {
                    height = 0; // Let the text determine natural height
                }

                // Ensure coordinates are valid
                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    x = 10; // Default x position
                }

                if (double.IsNaN(y) || double.IsInfinity(y))
                {
                    y = 10; // Default y position
                }

                // Configured colors are the default; auto background only overrides them when sampling succeeds
                Color textColor = ConfigManager.Instance.GetOverlayTextColor();
                Color bgColor = ConfigManager.Instance.GetOverlayBackgroundColor();
                System.Windows.Media.Imaging.BitmapSource? erasedPatch = null;
                Rect erasedBounds = Rect.Empty;
                if (!MainWindow.Instance.IsCapturingGameWindow && ConfigManager.Instance.IsAutoSetOverlayBackground() && sourceBitmap != null)
                {
                    try
                    {
                        // Back from MonitorWindow DIPs to capture pixels (same scale as the conversion above)
                        DpiHelper.GetWindowRenderDpi(MonitorWindow.Instance, out double dpiScaleX, out double dpiScaleY);

                        int bitmapX = (int)(x * dpiScaleX);
                        int bitmapY = (int)(y * dpiScaleY);
                        int bitmapWidth = Math.Max(1, (int)(width * dpiScaleX));
                        int bitmapHeight = Math.Max(1, (int)(height * dpiScaleY));
                        var textBox = new System.Drawing.Rectangle(bitmapX, bitmapY, bitmapWidth, bitmapHeight);

                        (bgColor, textColor) = ColorUtils.SampleOverlayColors(sourceBitmap, textBox);

                        // Original text removed from its own background, shown under the translation
                        using var backgroundTiming = PipelineDiagnostics.Measure(PipelineWork.Background);
                        erasedPatch = TextEraser.CreateErasedPatch(sourceBitmap, textBox, bgColor, out var patchPixels, protectedSources);
                        erasedBounds = new Rect(patchPixels.X / dpiScaleX, patchPixels.Y / dpiScaleY,
                                                patchPixels.Width / dpiScaleX, patchPixels.Height / dpiScaleY);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sampling overlay colors: {ex.Message}");
                    }
                }

                SolidColorBrush textBrush = new SolidColorBrush(textColor);
                SolidColorBrush bgBrush = new SolidColorBrush(bgColor);

                // Add the text object to the UI
                TextObject textObject = new TextObject(
                    text,  // Just the text, without confidence
                    x, y, width, height,
                    textBrush,
                    bgBrush,
                    captureX, captureY  // Store original capture coordinates
                );
                if (previous != null)
                {
                    textObject.ID = previous.ID;
                    textObject.TextTranslated = previous.TextTranslated;
                    textObject.TranslationContext = previous.TranslationContext;
                    textObject.FlowDirection = previous.FlowDirection;
                }
                captureGeometry = new OverlayPixelGeometry(x * renderScaleX, y * renderScaleY,
                    width * renderScaleX, height * renderScaleY, sourceLineHeightPixels);
                textObject.BindCaptureGeometry(captureGeometry, renderScaleX, renderScaleY);
                // New blocks retain their GUID; reused blocks receive their previous ID below.
                if (erasedPatch != null)
                {
                    textObject.SetErasedBackground(erasedPatch, erasedBounds);
                }

                textObject.UpdateUIElement();
                return textObject;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating text object: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return null;
            }
        }


        // Set the current capture position
        public void SetCurrentCapturePosition(int x, int y)
        {
            _currentCaptureX = x;
            _currentCaptureY = y;
        }

        // Screen position of the top-left pixel of the last captured bitmap
        public (int X, int Y) CurrentCapturePosition => (_currentCaptureX, _currentCaptureY);

        // Update text object positions based on capture position changes
        public void UpdateTextObjectPositions(int offsetX, int offsetY)
        {
            try
            {
                // Only proceed if we have text objects
                if (_textObjects.Count == 0) return;

                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure UI updates are thread-safe
                    Application.Current.Dispatcher.Invoke(() =>
                        UpdateTextObjectPositions(offsetX, offsetY));
                    return;
                }

                foreach (TextObject textObj in _textObjects)
                {
                    // Calculate new position based on original capture position and current offset
                    // Use negative offset since we want text to move in opposite direction of the window
                    double newX = textObj.X - offsetX;
                    double newY = textObj.Y - offsetY;

                    // Update position
                    textObj.X = newX;
                    textObj.Y = newY;

                    // Update UI element
                    textObj.UpdateUIElement();
                }

                // Refresh the monitor window to show updated positions
                if (MonitorWindow.Instance.IsVisible)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text positions: {ex.Message}");
            }
        }

        /// <summary>
        /// Filters out low-confidence characters from the OCR results
        /// </summary>
        private JsonElement FilterLowConfidenceCharacters(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;

            try
            {
                // Get minimum confidence threshold from config
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence();

                // Create output array for high-confidence results only
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();

                        // Process each element
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];

                            // Skip items that don't have required properties
                            if (!item.TryGetProperty("confidence", out var confElement))
                            {
                                // Include items without confidence values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }

                            // Get confidence value
                            double confidence = confElement.GetDouble();

                            // Only include elements with confidence above threshold
                            if (confidence >= minLetterConfidence)
                            {
                                item.WriteTo(writer);
                            }
                        }

                        writer.WriteEndArray();
                        writer.Flush();

                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error filtering low-confidence characters: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        static readonly HashSet<char> g_charsToStripFromHash =
             new(" \n\r\t,.-:;ー・…。、~』!^へ?\"'`()[]{}【】「」『』<>+=*/\\|_@#$%&");


        private string GenerateContentHash(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            StringBuilder contentBuilder = new();

            // Add each character to the content builder
            contentBuilder.Append(resultsElement.GetArrayLength());
            contentBuilder.Append('|');

            foreach (JsonElement element in resultsElement.EnumerateArray())
            {
                if (!element.TryGetProperty("text", out JsonElement textElement))
                {
                    continue;
                }

                string text = textElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // Normalize the text
                string normalizedText = NormalizeTextForHash(text);
                if (!string.IsNullOrEmpty(normalizedText))
                {
                    contentBuilder.Append(normalizedText);
                    // Add a separator between characters
                    contentBuilder.Append('|');
                }
            }

            string hash = contentBuilder.ToString();
            //Console.WriteLine($"Generated hash: {hash}");
            return hash;
        }

        /// <summary>
        /// Normalizes the text for hashing by removing certain characters and converting to lowercase.
        /// </summary>
        private string NormalizeTextForHash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;


            text = text.ToLowerInvariant();

            StringBuilder sb = new StringBuilder();
            bool lastWasSpace = true; // Start with a space to handle leading characters

            foreach (char c in text)
            {
                if (c == 'ツ')
                {
                    sb.Append('ッ');
                    lastWasSpace = false;
                }
                // Handle whitespace
                else if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' '); // Only add a space if the last character was not a space
                        lastWasSpace = true;
                    }
                }

                else if (!g_charsToStripFromHash.Contains(c))
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            // Remove whiespace if it's at the end
            string result = sb.ToString();
            if (result.Length > 0 && result[result.Length - 1] == ' ')
            {
                result = result.Substring(0, result.Length - 1);
            }

            return result;
        }

        // Process bitmap directly with Windows OCR (no file saving)
        public async Task ProcessWithWindowsOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            var timing = PipelineDiagnostics.Current;
            using var operation = _captureLifetime.Capture();
            var damage = new OcrFrameObservation(bitmap.Width, bitmap.Height);
            _activeOcrDamage = damage;
            try
            {
                //Console.WriteLine("Starting Windows OCR processing directly from bitmap...");

                try
                {
                    // Get the text lines from Windows OCR directly from the bitmap
                    List<Windows.Media.Ocr.OcrLine> textLines;
                    using (timing?.Measure(PipelineWork.Ocr))
                        textLines = await Task.Run(() => WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, sourceLanguage), operation.Token);
                    timing?.Mark(PipelineStage.OcrCompleted);
                    if (!operation.IsCurrent || !MainWindow.Instance.GetIsStarted())
                    {
                        timing?.Finish(PipelineOutcome.Canceled);
                        return;
                    }
                    // Console.WriteLine($"Windows OCR found {textLines.Count} text lines");

                    // Process the OCR results with language code
                    await WindowsOCRManager.Instance.ProcessWindowsOcrResults(textLines, sourceLanguage,
                        () => operation.IsCurrent && MainWindow.Instance.GetIsStarted(),
                        data => ProcessReceivedTextJsonData(data, damage));
                }
                catch (OperationCanceledException) when (!operation.IsCurrent)
                {
                    timing?.Finish(PipelineOutcome.Canceled);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Windows OCR error: {ex.Message}");
                    timing?.Finish(PipelineOutcome.Failed);
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing bitmap with Windows OCR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                if (!operation.IsCurrent) timing?.Finish(PipelineOutcome.Canceled);
                timing?.FinishObservation();
                // The awaited capture owns the input bitmap, including cancellation/error paths.
                if (ReferenceEquals(_activeOcrDamage, damage)) _activeOcrDamage = null;

                if (operation.IsCurrent && MainWindow.Instance.GetIsStarted())
                    MainWindow.Instance.SetOCRCheckIsWanted(true);

            }
        }

        private bool IsDuplicateAudio(string newText, List<string> recentTexts, int checkLastN = 3)
        {
            if (recentTexts.Count == 0) return false;

            string normalizedNew = NormalizeTextForComparison(newText);

            int checkCount = Math.Min(checkLastN, recentTexts.Count);
            for (int i = recentTexts.Count - checkCount; i < recentTexts.Count; i++)
            {
                string normalizedExisting = NormalizeTextForComparison(recentTexts[i]);

                if (normalizedNew == normalizedExisting)
                {
                    Console.WriteLine($"[DUPLICATE] Exact match: '{newText}'");
                    return true;
                }

                double similarity = CalculateTextSimilarity(normalizedNew, normalizedExisting);
                if (similarity > 0.9)
                {
                    Console.WriteLine($"[DUPLICATE] High similarity ({similarity:P0}): '{newText}' vs '{recentTexts[i]}'");
                    return true;
                }
            }

            return false;
        }

        private string NormalizeTextForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return System.Text.RegularExpressions.Regex.Replace(
                text.ToLower().Trim(),
                @"\s+",
                " "
            );
        }

        private double CalculateTextSimilarity(string s1, string s2)
        {
            if (s1 == s2) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            // Levenshtein distance
            int maxLen = Math.Max(s1.Length, s2.Length);
            int distance = LevenshteinDistance(s1, s2);

            return 1.0 - ((double)distance / maxLen);
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            // Two-row DP: same result as the full matrix but O(n) memory, since this runs on every OCR frame
            int[] prev = new int[s2.Length + 1];
            int[] curr = new int[s2.Length + 1];

            for (int j = 0; j <= s2.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(prev[j] + 1, curr[j - 1] + 1),
                        prev[j - 1] + cost
                    );
                }
                (prev, curr) = (curr, prev);
            }

            return prev[s2.Length];
        }

        public void AddAudioTextObject(string audioText)
        {
            if (string.IsNullOrEmpty(audioText)) return;

            lock (_audioBatchLock)
            {

                if (IsDuplicateAudio(audioText, _audioBatch, checkLastN: 3))
                {
                    Console.WriteLine($"[SKIP] Duplicate audio detected, ignoring: '{audioText}'");
                    return;
                }


                _audioBatch.Add(audioText);
                Console.WriteLine($"Added audio to batch: '{audioText}'. Batch size: {_audioBatch.Count}");
                _hasNewAudioSinceLastTranslation = true;

                // Lines that arrive while a translation is in flight are batched and sent together
                // as soon as it finishes (see ProcessAudioBatchAsync). No fixed debounce: the VAD
                // already cuts at sentence pauses, so waiting here only added latency.
                if (_isProcessingAudioBatch)
                {
                    Console.WriteLine("Translation in progress, line queued for the next batch");
                    return;
                }
            }

            ProcessAudioBatchCallback(null);
        }

        private void ProcessAudioBatchCallback(object? state)
        {
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await ProcessAudioBatchAsync();
            });
        }

        private async Task ProcessAudioBatchAsync()
        {
            List<string> batchToProcess;

            lock (_audioBatchLock)
            {
                if (!_hasNewAudioSinceLastTranslation)
                {
                    Console.WriteLine("[SKIP] No new audio since last translation, ignoring timer trigger");
                    return;
                }

                if (_isProcessingAudioBatch || _audioBatch.Count == 0)
                {
                    return;
                }

                _isProcessingAudioBatch = true;

                batchToProcess = new List<string>(_audioBatch);
                _audioBatch.Clear();
                _hasNewAudioSinceLastTranslation = false;

                Console.WriteLine($"Processing audio batch with {batchToProcess.Count} items");
            }

            try
            {
                _textObjects.Clear();

                string combinedAudio = string.Join(" ", batchToProcess);

                LogManager.Instance.WriteContentLine($"Combined audio text: '{combinedAudio}'");

                var audioTextObject = new TextObject(
                    text: combinedAudio,
                    x: 100,
                    y: 100,
                    width: 800,
                    height: 100,
                    textColor: null,
                    backgroundColor: null,
                    captureX: 0,
                    captureY: 0
                );

                _textObjects.Add(audioTextObject);

                AudioTiming.Log("4. Translation start", $"{batchToProcess.Count} line(s), {ConfigManager.Instance.GetCurrentTranslationService()}");
                var translateTimer = System.Diagnostics.Stopwatch.StartNew();
                await TranslateTextObjectsAsync();
                AudioTiming.Log("5. Translation done", $"{translateTimer.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing audio batch: {ex.Message}");
            }
            finally
            {
                bool hasPending;
                lock (_audioBatchLock)
                {
                    _isProcessingAudioBatch = false;
                    hasPending = _audioBatch.Count > 0;
                }

                // Lines queued during this translation would otherwise wait for the next line
                // of dialogue to trigger them — forever, if the game goes quiet.
                if (hasPending) ProcessAudioBatchCallback(null);
            }
        }

        // Process bitmap directly with Windows OCR (no file saving)
        public async Task ProcessWithOneOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            var timing = PipelineDiagnostics.Current;
            using var operation = _captureLifetime.Capture();
            var damage = new OcrFrameObservation(bitmap.Width, bitmap.Height);
            _activeOcrDamage = damage;
            try
            {

                try
                {
                    bool preprocessing = ConfigManager.Instance.IsHDRSupportEnabled();
                    List<OneOCRManager.Line> textLines;
                    using (timing?.Measure(PipelineWork.Ocr))
                        textLines = await Task.Run(() => OneOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, preprocessing), operation.Token);
                    timing?.Mark(PipelineStage.OcrCompleted);

                    if (!operation.IsCurrent || !MainWindow.Instance.GetIsStarted())
                    {
                        timing?.Finish(PipelineOutcome.Canceled);
                        return;
                    }
                    // Process the OCR results with language code
                    await OneOCRManager.Instance.ProcessOneOcrResults(textLines, sourceLanguage, bitmap,
                        () => operation.IsCurrent && MainWindow.Instance.GetIsStarted(),
                        data => ProcessReceivedTextJsonData(data, damage));
                }
                catch (OperationCanceledException) when (!operation.IsCurrent)
                {
                    timing?.Finish(PipelineOutcome.Canceled);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"One OCR error: {ex.Message}");
                    timing?.Finish(PipelineOutcome.Failed);
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing bitmap with One OCR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                if (!operation.IsCurrent) timing?.Finish(PipelineOutcome.Canceled);
                timing?.FinishObservation();
                // The awaited capture owns the input bitmap, including cancellation/error paths.
                if (ReferenceEquals(_activeOcrDamage, damage)) _activeOcrDamage = null;

                if (operation.IsCurrent && MainWindow.Instance.GetIsStarted())
                    MainWindow.Instance.SetOCRCheckIsWanted(true);

            }
        }

        // Called when the application is closing
        public void Finish()
        {
            try
            {
                // Clean up resources
                Console.WriteLine("Logic finalized");

                SetOcrSourceFrame(null);

                // Clear all text objects
                ClearAllTextObjects();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.Format("Msg_CleanupError", ex.Message), UiText.Get("Title_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // Add a text object with specific text and optional position and styling
        public void AddTextObject(string text,
                                 double x = 0,
                                 double y = 0,
                                 double width = 0,
                                 double height = 0,
                                 SolidColorBrush? textColor = null,
                                 SolidColorBrush? backgroundColor = null)
        {
            try
            {
                // Validate text
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot add text object with empty text");
                    return;
                }

                // Ensure position and dimensions are valid
                if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0) width = 0;
                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0) height = 0;

                backgroundColor ??= new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)); // Half-transparent black

                // Create the text object with specified parameters
                TextObject textObject = new TextObject(
                    text,
                    x, y,
                    width, height,
                    textColor,
                    backgroundColor);

                // Add to our collection
                _textObjects.Add(textObject);

                // Don't add to main window UI anymore
                // Just raise the event to notify MonitorWindow
                TextObjectAdded?.Invoke(this, textObject);

                LogManager.Instance.WriteContentLine($"Added text '{text}' at position {x}, {y}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text: {ex.Message}");
            }
        }


        // Clear all text objects
        private void ClearRenderedTextObjects()
        {
            _textObjects.Clear();
        }

        // Called only on the UI thread with screen processing disabled. Keep rendered text,
        // but invalidate both leases so a request from before pause cannot publish after resume.
        internal void SuspendScreenTranslation()
        {
            InvalidatePendingWork();
            _translationRetry.Reset();
            MonitorWindow.Instance.HideTranslationStatus();
            ChatBoxWindow.Instance?.HideTranslationStatus();
        }

        internal void PrepareScreenTranslationResume()
        {
            SuspendScreenTranslation();
            _ocrScene.Reset();
            _lastOcrHash = string.Empty;
            _lastTextContent = string.Empty;
            _lastChangeTime = DateTime.MinValue;
        }

        public void ClearAllTextObjects()
        {
            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() => ClearAllTextObjects());
                    return;
                }

                InvalidatePendingWork();
                _ocrScene.Reset();
                _translationRetry.Reset();
                _lastOcrHash = string.Empty;
                _lastTextContent = string.Empty;
                _lastChangeTime = DateTime.Now;
                ClearRenderedTextObjects();
                MainWindow.Instance.SetOCRCheckIsWanted(MainWindow.Instance.GetIsStarted());
                MonitorWindow.Instance.RefreshOverlays();
                MonitorWindow.Instance.HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();

                Console.WriteLine("All text objects cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing text objects: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        //! Process structured JSON translation from ChatGPT or other services
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot, bool addToChatBox = true)
        {
            try
            {
                Console.WriteLine("Processing structured JSON translation");
                // Check if we have text_blocks array in the translated JSON
                if (translatedRoot.TryGetProperty("text_blocks", out JsonElement textBlocksElement) &&
                    textBlocksElement.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"Found {textBlocksElement.GetArrayLength()} text blocks in translated JSON");

                    // Get current target language
                    string targetLanguage = ConfigManager.Instance.GetTargetLanguage().ToLower();
                    Console.WriteLine($"Target language: ----------------------------{targetLanguage}");
                    // Define RTL (Right-to-Left) languages
                    HashSet<string> rtlLanguages = new HashSet<string> {
                        "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
                    };

                    // Check if target language is RTL
                    bool isRtlLanguage = rtlLanguages.Contains(targetLanguage);

                    if (isRtlLanguage)
                    {
                        Console.WriteLine($"Detected RTL language: {targetLanguage}");
                    }

                    // Process each translated block
                    for (int i = 0; i < textBlocksElement.GetArrayLength(); i++)
                    {
                        var block = textBlocksElement[i];

                        if (block.TryGetProperty("id", out JsonElement idElement) &&
                            block.TryGetProperty("text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                                // Check if this is our combined text block with ID=1
                                if (id == "999")
                                {
                                    // Split the translated text using the separator
                                    string[]? translatedParts = TranslationParts.SplitExact(translatedText, _textObjects.Count);
                                    if (translatedParts == null) continue;

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < translatedParts.Length; j++)
                                    {
                                        // Clean up the translated text - remove any remaining separators that might have been part of the translation
                                        // (legacy |||RST||| and mangled ##|||## variants). Only fragments around "|||" are removed,
                                        // so real text such as "FIRST" or "#1" is left intact.
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"\|{3}\s*RST\s*\|{3}|#{0,2}\s*\|{3}\s*#{0,2}",
                                            ""
                                        );
                                        if (string.IsNullOrWhiteSpace(cleanTranslatedText)) continue;

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!cleanTranslatedText.StartsWith('\u200F'))
                                            {
                                                cleanTranslatedText = "\u200F" + cleanTranslatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the text object with the cleaned translated text
                                        _textObjects[j].TextTranslated = cleanTranslatedText;
                                        _textObjects[j].UpdateUIElement();
                                        Console.WriteLine($"Updated text object at index {j} with translation from Google Translate");
                                    }

                                    // We've processed the combined text block, so we can break out of the loop
                                    break;
                                }
                                else
                                {
                                    // Handle the old way for backward compatibility
                                    // Find the matching text object by ID
                                    var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                    if (matchingTextObj != null)
                                    {

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            // This can help with mixed content
                                            if (!translatedText.StartsWith('\u200F'))
                                            {
                                                translatedText = "\u200F" + translatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the corresponding text object
                                        matchingTextObj.TextTranslated = translatedText;
                                        matchingTextObj.UpdateUIElement();
                                        Console.WriteLine($"Updated text object {id} with translation");
                                    }

                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No text_blocks array found in translated JSON");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing structured JSON translation: {ex.Message}");
            }

            if (addToChatBox)
            {
                AddTranslatedTextObjectsToChatBox();
            }
        }

        //!Process the finished translation into text blocks and the chatbox
        void ProcessTranslatedJSON(string translationResponse, bool addToChatBox = true)
        {
            try
            {
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();

                // Use shared method to extract inner content
                var (innerContent, isGoogleTranslate) = ExtractInnerContentFromResponse(translationResponse);

                if (string.IsNullOrEmpty(innerContent))
                {
                    Console.WriteLine($"Could not extract content from {currentService} response");
                    OnFinishedThings(true);
                    return;
                }

                if (isGoogleTranslate)
                {
                    Console.WriteLine("Translation response with 'translations' format detected (Google Translate / Yandex / compatible)");
                    using JsonDocument doc = JsonDocument.Parse(innerContent);
                    ProcessGoogleTranslateJson(doc.RootElement, addToChatBox);
                    return;
                }

                // Parse inner content and process structured JSON
                try
                {
                    using JsonDocument translatedDoc = JsonDocument.Parse(innerContent);
                    ProcessStructuredJsonTranslation(translatedDoc.RootElement, addToChatBox);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error parsing translation JSON: {ex.Message}");
                    OnFinishedThings(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ProcessTranslatedJSON: {ex.Message}");
                OnFinishedThings(true);
            }
        }

        private void AddTranslatedTextObjectsToChatBox()
        {
            var sortedTextObjects = _textObjects.OrderBy(t => t.Y).ToList();

            foreach (var textObject in sortedTextObjects)
            {
                string originalText = textObject.Text;
                string translatedText = textObject.TextTranslated;

                if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                {
                    TranslationCompleted?.Invoke(this, new TranslationEventArgs
                    {
                        OriginalText = originalText,
                        TranslatedText = translatedText
                    });
                    if (_sceneTiming.Current is { IsFinished: false } timing && AreOverlaysCurrent &&
                        ChatBoxWindow.Instance is { IsVisible: true, WindowState: not WindowState.Minimized })
                        timing.RecordChatBoxBlock(textObject.ID, _textObjects.Count,
                            _textObjects.Count(t => !string.IsNullOrWhiteSpace(t.TextTranslated)));
                }
                else
                {
                    LogManager.Instance.WriteContentLine($"Skipping empty translation - Original: '{originalText}', Translated: '{translatedText}'");
                }
            }
        }

        private string MapLanguageCode(string language)
        {
            return language.ToLower() switch
            {
                "ja" => "japanese",
                "en" => "english",
                "ch_sim" => "chinese",
                "ko" => "korean",
                "vi" => "vietnamese",
                "fr" => "french",
                "de" => "german",
                "es" => "spanish",
                "it" => "italian",
                "ru" => "russian",
                "hi" => "hindi",
                "pt" => "portuguese",
                "ar" => "arabic",
                "bg" or "bg-bg" => "bulgarian",
                "nl" => "dutch",
                "hr" => "Croatian",
                "pl" => "Polish",
                "ro" => "Romanian",
                "fa" => "Farsi",
                "cs" => "Czech",
                "id" => "Indonesian",
                "th" => "ThaiLand",
                "tr" => "Turkish",
                "ch_tra" => "Traditional Chinese",
                "hu" => "Hungarian",
                "si" => "Sinhala",
                "da" => "Danish",
                "uk" => "ukrainian",
                "fi" => "Finnish",
                "ckb" => "Central Kurdish",
                "bn" => "Bengali",
                "el" => "Greek",
                "he" => "Hebrew",
                _ => language
            };
        }

        //!Convert textobjects to json and send for translation
        public async Task TranslateTextObjectsAsync()
        {
            if (!MainWindow.Instance.GetIsStarted()) return;
            if (!_ocrScene.TryBeginTranslation(out long translationId)) return;
            var timing = _sceneTiming.Current;
            var translationStopwatch = new Stopwatch();
            using var timingScope = PipelineDiagnostics.Use(timing);
            timing?.Mark(PipelineStage.TranslationStarted);
            using var operation = _pipelineLifetime.Capture();
            string? requestContext = null;
            string? attemptedService = null;
            TranslationFailure? failure = null;
            bool evaluateOutcome = false;
            bool wasCanceled = false;
            try
            {
                // Show translation status at the beginning
                MonitorWindow.Instance.ShowTranslationStatus(false);

                // Also show translation status in ChatBoxWindow if it's open
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.ShowTranslationStatus(false);
                }

                if (_textObjects.Count == 0)
                {
                    Console.WriteLine("No text objects to translate");
                    OnFinishedThings(true);
                    return;
                }

                // Get previous context if enabled
                var previousContext = GetPreviousContext();

                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();

                // Get the prompt template
                string prompt = GetLlmPrompt();
                prompt = prompt.Replace("source_language", MapLanguageCode(GetSourceLanguage())).Replace("target_language", MapLanguageCode(GetTargetLanguage()));

                translationStopwatch.Restart();

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                string currentModel = ConfigManager.Instance.GetCurrentTranslationModel();

                string sourceLanguage = GetSourceLanguage();
                string targetLanguage = GetTargetLanguage();
                requestContext = BuildTranslationCacheContext(currentService, currentModel, prompt, gameInfo);
                EnsureCurrentTranslation(operation, requestContext);

                attemptedService = currentService;
                evaluateOutcome = true;
                bool cacheEnabled = ConfigManager.Instance.IsTranslationCacheEnabled();
                bool readCache = cacheEnabled && !_bypassTranslationCacheOnce;
                bool bypass = _bypassTranslationCacheOnce;
                _bypassTranslationCacheOnce = false;
                string cacheContext = BuildBlockCacheContext(requestContext, previousContext, currentService);
                if (cacheEnabled) TranslationCache.Instance.UseContext(cacheContext);
                var objects = _textObjects.ToArray();
                foreach (var textObject in objects)
                {
                    if (bypass || textObject.TranslationContext != requestContext) textObject.TextTranslated = "";
                }
                if (readCache) ApplyCachedTranslations(cacheContext, requestContext);
                var missing = objects.Where(t => string.IsNullOrWhiteSpace(t.TextTranslated)).ToArray();
                MonitorWindow.Instance.RefreshOverlays();
                if (missing.Length > 0)
                {
                    bool presentReadyBatches = MainWindow.Instance.IsCapturingGameWindow;
                    void ApplyReadyTranslations(IReadOnlyDictionary<string, TranslationBlockResult> translations)
                    {
                        EnsureCurrentTranslation(operation, requestContext);
                        foreach (var textObject in missing)
                        {
                            if (!translations.TryGetValue(textObject.Text, out var result)) continue;
                            if (string.IsNullOrWhiteSpace(result.Text)) { failure ??= result.Failure; continue; }
                            // Both stable identity and exact source must still belong to this scene.
                            var current = _textObjects.FirstOrDefault(t => t.ID == textObject.ID && t.Text == textObject.Text);
                            if (current == null) continue;
                            current.TextTranslated = result.Text;
                            current.TranslationContext = requestContext;
                            current.FlowDirection = result.Text.StartsWith('\u200F') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                            current.UpdateUIElement();
                        }
                    }
                    Task PresentReadyTranslations(IReadOnlyDictionary<string, TranslationBlockResult> translations) =>
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // Recheck after dispatch: focus, scene or settings may have changed
                            // while this UI update was queued. Rendering retains freshness guards.
                            ApplyReadyTranslations(translations);
                            MonitorWindow.Instance.RefreshOverlays();
                        }).Task;
                    LogTranslationRequest(currentService, currentModel, missing,
                        (missing.Length + GameTranslationProfile.BatchSize(currentService, ConfigManager.Instance.GetOllamaModel()) - 1) /
                            GameTranslationProfile.BatchSize(currentService, ConfigManager.Instance.GetOllamaModel()), objects.Length - missing.Length);
                    // The foreground joins matching units already owned by prefetch,
                    // including partial overlap and a different OCR order.
                    var translations = await TranslateBlocksAsync(OrderGameTranslationTexts(missing.Select(t => t.SourceBlock)),
                        cacheContext, _translationEpoch, translationService, currentService,
                        prompt, previousContext, gameInfo, sourceLanguage, targetLanguage, cacheEnabled, readCache, operation.Token,
                        forceRefresh: bypass, onReady: presentReadyBatches ? PresentReadyTranslations : null);
                    EnsureCurrentTranslation(operation, requestContext);
                    if (!presentReadyBatches) ApplyReadyTranslations(translations);
                }
                else OcrTiming.Log("3. Translation reused", $"{objects.Length} block(s)");
                EnsureCurrentTranslation(operation, requestContext);
                AddTranslatedTextObjectsToChatBox();

                translationStopwatch.Stop();
                Console.WriteLine($"Translation took {translationStopwatch.ElapsedMilliseconds} ms");
                timing?.Mark(PipelineStage.TranslationReady);
                OcrTiming.Log("5. Translation prepared", $"translation step took {translationStopwatch.ElapsedMilliseconds} ms; physical presentation not measured");
                if (!ConfigManager.Instance.IsAutoOCREnabled() && _textObjects.All(t => !string.IsNullOrWhiteSpace(t.TextTranslated)))
                {
                    MainWindow.Instance.isStopOCR = true;
                }

            }
            catch (OperationCanceledException)
            {
                timing?.Finish(PipelineOutcome.Canceled);
                wasCanceled = true;
                Console.WriteLine("Translation discarded because its session or settings changed.");
            }
            catch (Exception ex)
            {
                if (!operation.IsCurrent) return;
                Console.WriteLine($"Error translating text objects: {ex.Message}");
                failure ??= TranslationFailure.FromException(ex);
                OnFinishedThings(true);
            }

            finally
            {
                try
                {
                    if (evaluateOutcome && !wasCanceled && operation.IsCurrent && MainWindow.Instance.GetIsStarted())
                    {
                        bool complete = _textObjects.Count > 0 && _textObjects.All(t => !string.IsNullOrWhiteSpace(t.TextTranslated));
                        if (complete) _translationRetry.Reset();
                        else _translationRetry.Failed(failure ?? new(TranslationFailureKind.InvalidResponse), DateTimeOffset.UtcNow);
                        OnboardingTips.OnTranslationResult(attemptedService!, complete, failure, operation.Token);
                    }
                }
                finally
                {
                    // Only the owner can release busy state or update the new session's UI.
                    // Notification failures must not leave that owner stuck either.
                    if (_ocrScene.EndTranslation(translationId))
                    {
                        OnFinishedThings(true);
                        FinishSceneTiming();
                    }
                }
            }
        }

        private void FinishSceneTiming()
        {
            var timing = _sceneTiming.Current;
            if (timing == null || timing.IsFinished) return;
            var sample = timing.Snapshot();
            int translated = _textObjects.Count(t => !string.IsNullOrWhiteSpace(t.TextTranslated));
            timing.SetCoverage(_textObjects.Count, translated, sample.VisibleBlocks);
            if (_captureNeedsVerification)
            {
                timing.Finish(PipelineOutcome.Partial);
                return;
            }
            sample = timing.Snapshot();
            timing.Finish(sample.VisibleBlocks == sample.ExpectedBlocks && sample.ExpectedBlocks > 0
                ? PipelineOutcome.UiApplied
                : sample.TranslatedBlocks == sample.ExpectedBlocks && sample.ExpectedBlocks > 0 ? PipelineOutcome.NotPresented
                : sample.TranslatedBlocks > 0 ? PipelineOutcome.Partial : PipelineOutcome.Failed);
        }

        // Set when the user asks to retry, so that one translation goes to the service even if cached
        private bool _bypassTranslationCacheOnce;

        // Settings that change a translation: a cached one is only reused under the same settings
        private string BuildTranslationCacheContext(string service, string model, string prompt, string gameInfo) =>
            JsonSerializer.Serialize(new { service, model, source = GetSourceLanguage(), target = GetTargetLanguage(), prompt, gameInfo });

        private static string BuildBlockCacheContext(string settings, object previousContext, string service) =>
            TranslationBlockProtocol.CacheContext(settings, GameTranslationProfile.MeaningContext(service, previousContext,
                ConfigManager.Instance.GetOllamaModel()));

        private void EnsureCurrentTranslation(PipelineLifetime.Lease operation, string requestContext, bool requireStarted = true)
        {
            operation.ThrowIfStale();
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            string prompt = GetLlmPrompt()
                .Replace("source_language", MapLanguageCode(GetSourceLanguage()))
                .Replace("target_language", MapLanguageCode(GetTargetLanguage()));
            string currentContext = BuildTranslationCacheContext(service, ConfigManager.Instance.GetCurrentTranslationModel(),
                prompt, ConfigManager.Instance.GetGameInfo());
            if ((requireStarted && !MainWindow.Instance.GetIsStarted()) || currentContext != requestContext)
                throw new OperationCanceledException(operation.Token);
        }

        // Prefetch and foreground use the same per-context, per-source promises.
        // A prefetch only fills the cache; it never publishes geometry or UI objects.
        private Task? _prefetchTask;

        private void StartTranslationPrefetch(JsonElement results)
        {
            try
            {
                if (!ConfigManager.Instance.IsTranslationPrefetchEnabled() ||
                    !ConfigManager.Instance.IsTranslationCacheEnabled() ||
                    !MainWindow.Instance.GetIsStarted() || !MainWindow.Instance.GetTranslateEnabled() ||
                    _prefetchTask is { IsCompleted: false }) return;
                var texts = GetTranslatableTexts(results, prioritizeGame: MainWindow.Instance.IsCapturingGameWindow);
                if (texts.Count == 0) return;
                string service = ConfigManager.Instance.GetCurrentTranslationService();
                string prompt = GetLlmPrompt().Replace("source_language", MapLanguageCode(GetSourceLanguage()))
                    .Replace("target_language", MapLanguageCode(GetTargetLanguage()));
                string gameInfo = ConfigManager.Instance.GetGameInfo();
                string settings = BuildTranslationCacheContext(service, ConfigManager.Instance.GetCurrentTranslationModel(), prompt, gameInfo);
                var previousContext = GetPreviousContext();
                string cacheContext = BuildBlockCacheContext(settings, previousContext, service);
                TranslationCache.Instance.UseContext(cacheContext);
                var missing = texts.Where(t => !TranslationCache.Instance.TryGet(cacheContext, t, out _)).ToArray();
                if (missing.Length == 0) return;
                var translationService = TranslationServiceFactory.CreateService(service);
                string sourceLanguage = GetSourceLanguage(), targetLanguage = GetTargetLanguage();
                long epoch = _translationEpoch;
                var operation = _pipelineLifetime.Capture();
                _prefetchTask = PrefetchBlocksAsync();
                async Task PrefetchBlocksAsync()
                {
                    using (operation)
                    {
                        try
                        {
                            await TranslateBlocksAsync(missing, cacheContext, epoch, translationService, service,
                                prompt, previousContext, gameInfo, sourceLanguage, targetLanguage, true, true, operation.Token);
                        }
                        catch (OperationCanceledException) when (!operation.IsCurrent) { }
                        catch (Exception ex) { Console.WriteLine($"Translation prefetch failed: {ex.GetType().Name}"); }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error starting translation prefetch: {ex.GetType().Name}"); }
        }

        private Task<IReadOnlyDictionary<string, TranslationBlockResult>> TranslateBlocksAsync(IReadOnlyList<string> texts,
            string cacheContext, long epoch, ITranslationService service, string serviceName,
            string prompt, object previousContext, string gameInfo, string sourceLanguage, string targetLanguage,
            bool cacheEnabled, bool readCache, CancellationToken cancellation, bool forceRefresh = false,
            Func<IReadOnlyDictionary<string, TranslationBlockResult>, Task>? onReady = null)
        {
            string flightContext = JsonSerializer.Serialize(new { cacheContext, epoch, refresh = forceRefresh ? Guid.NewGuid().ToString() : null });
            return _translationBatches.TranslateAsync(flightContext, texts, async (blocks, token) =>
            {
                // A queued batch can become redundant after an earlier one filled the cache.
                var translated = new Dictionary<string, string>(StringComparer.Ordinal);
                var missing = new List<TranslationBlock>();
                foreach (var block in blocks)
                {
                    if (readCache && TranslationCache.Instance.TryGet(cacheContext, block.Text, out var cached)) translated[block.Id] = cached;
                    else missing.Add(block);
                }
                TranslationFailure? failure = null;
                token.ThrowIfCancellationRequested();
                if (missing.Count > 0)
                {
                    string request = BuildTranslationRequestJson(missing.Select(b => (object)new { id = b.Id, text = b.Text }).ToList(),
                        previousContext, gameInfo, sourceLanguage, targetLanguage);
                    string blockPrompt = prompt + "\n" + TranslationBlockProtocol.PromptRule;
                    LogManager.Instance.LogLlmRequest(blockPrompt, request);
                    // Keep the coordinator slot until an uncooperative provider has really stopped.
                    string? response = await TranslationRequestContext.RunAsync(service, request, blockPrompt, token,
                        f => failure = f, awaitProviderCompletion: true).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    var (inner, direct) = string.IsNullOrWhiteSpace(response) ? (null, false)
                        : ExtractInnerContentFromResponse(response, serviceName);
                    var parsed = TranslationBlockProtocol.Parse(inner, direct, missing);
                    foreach (var block in missing)
                    {
                        if (!parsed.TryGetValue(block.Id, out var value)) continue;
                        string final = FinalizeTranslation(value, targetLanguage, isCombinedPart: false);
                        translated[block.Id] = final;
                        if (cacheEnabled) TranslationCache.Instance.SetIfContext(cacheContext, block.Text, final);
                    }
                }
                return new TranslationBatchResult(translated, failure);
            }, cancellation, onReady, batchSize: GameTranslationProfile.BatchSize(serviceName, ConfigManager.Instance.GetOllamaModel()));
        }

        private static readonly HashSet<string> RtlLanguages = new HashSet<string> {
            "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
        };

        // The same post-processing the normal path applies before it stores a translation, so a
        // prefetched translation is identical to one produced by ProcessTranslatedJSON
        private static string FinalizeTranslation(string translated, string targetLanguage, bool isCombinedPart)
        {
            if (isCombinedPart)
            {
                translated = System.Text.RegularExpressions.Regex.Replace(translated, @"\|{3}\s*RST\s*\|{3}|#{0,2}\s*\|{3}\s*#{0,2}", "");
            }
            if (RtlLanguages.Contains(targetLanguage.ToLowerInvariant()) && !translated.StartsWith('\u200F'))
            {
                translated = "\u200F" + translated;
            }
            return translated;
        }

        // Fill text objects from the translation cache; returns the ones that were found
        private HashSet<TextObject> ApplyCachedTranslations(string cacheContext, string settings)
        {
            var found = new HashSet<TextObject>();
            foreach (var textObject in _textObjects)
            {
                if (TranslationCache.Instance.TryGet(cacheContext, textObject.Text, out string translation))
                {
                    // Cached RTL translations keep the right-to-left mark added when they were first shown.
                    // The char overload is ordinal: StartsWith(string) is culture-sensitive and ignores the
                    // zero-width mark, so it matched every translation and turned all of them right-to-left.
                    textObject.FlowDirection = translation.StartsWith('\u200F') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    textObject.TextTranslated = translation;
                    textObject.TranslationContext = settings;
                    textObject.UpdateUIElement();
                    found.Add(textObject);
                }
            }
            return found;
        }

        private static void LogTranslationRequest(string service, string model, IEnumerable<TextObject> blocks, int requests, int fromCache)
        {
            int chars = blocks.Sum(t => t.Text?.Length ?? 0);
            string modelPart = string.IsNullOrEmpty(model) ? "" : $" ({model})";
            OcrTiming.Log("3. Translation request", $"{service}{modelPart}, {requests} request(s), {chars} chars{(fromCache > 0 ? $", {fromCache} block(s) from cache" : "")}");
        }

        private string BuildTranslationRequestJson(List<object> textBlocks, object previousContext, string gameInfo, string sourceLanguage, string targetLanguage)
        {
            var ocrData = new
            {
                block_protocol = TranslationBlockProtocol.Version,
                source_language = MapLanguageCode(sourceLanguage),
                target_language = MapLanguageCode(targetLanguage),
                text_blocks = textBlocks,
                previous_context = previousContext,
                game_info = gameInfo
            };

            // Compact: indentation only adds input tokens
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(ocrData, jsonOptions);
        }

        public string GetGeminiApiKey()
        {
            return ConfigManager.Instance.GetGeminiApiKey();
        }

        public string GetLlmPrompt()
        {
            return ConfigManager.Instance.GetLlmPrompt();
        }

        private string GetSourceLanguage()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetSourceLanguage());
            }
            // return (MainWindow.Instance.sourceLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            // Find the MainWindow instance
            // var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            // // Get the selected ComboBoxItem
            // if (mainWindow!.sourceLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            // {
            //     // Return the content as string
            //     return selectedItem.Content?.ToString()!;
            // }

            return ConfigManager.Instance.GetSourceLanguage();
        }

        // Get target language from MainWindow (for future implementation)
        private string GetTargetLanguage()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetTargetLanguage());
            }

            // // Find the MainWindow instance
            // var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            // // Get the selected ComboBoxItem
            // if (mainWindow!.targetLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            // {
            //     // Return the content as string
            //     return selectedItem.Content?.ToString()!;
            // }
            return ConfigManager.Instance.GetTargetLanguage();
        }

        // Get previous context based on configuration settings
        private List<string> GetPreviousContext()
        {
            // Check if context is enabled
            int maxContextPieces = ConfigManager.Instance.GetMaxContextPieces();
            if (maxContextPieces <= 0)
            {
                return new List<string>(); // Empty list if context is disabled
            }

            int minContextSize = ConfigManager.Instance.GetMinContextSize();

            // Get context from ChatBoxWindow's history
            if (ChatBoxWindow.Instance != null)
            {
                return ChatBoxWindow.Instance.GetRecentOriginalTexts(maxContextPieces, minContextSize);

            }

            return new List<string>();
        }

        /// <summary>
        /// Translates a single text string immediately without OCR pipeline.
        /// Used for clipboard auto-translation feature.
        /// </summary>
        /// <param name="text">Text to translate</param>
        /// <returns>Translated text, or null if translation failed</returns>
        public async Task<string?> TranslateTextImmediateAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using var operation = _captureLifetime.Capture();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                Console.WriteLine($"[ClipboardTranslate] Starting translation of {text.Length} chars");

                // Create the JSON structure for translation
                var textsToTranslate = new List<object>
                {
                    new
                    {
                        id = "clipboard",
                        text = text
                    }
                };

                // Get previous context if enabled
                var previousContext = GetPreviousContext();

                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();

                // Create the full JSON object
                var ocrData = new
                {
                    source_language = MapLanguageCode(GetSourceLanguage()),
                    target_language = MapLanguageCode(GetTargetLanguage()),
                    text_blocks = textsToTranslate,
                    previous_context = previousContext,
                    game_info = gameInfo
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonToTranslate = JsonSerializer.Serialize(ocrData, jsonOptions);

                // Get the prompt template
                string prompt = GetLlmPrompt();
                prompt = prompt.Replace("source_language", MapLanguageCode(GetSourceLanguage())).Replace("target_language", MapLanguageCode(GetTargetLanguage()));

                // Log the LLM request
                LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();

                string requestContext = BuildTranslationCacheContext(currentService,
                    ConfigManager.Instance.GetCurrentTranslationModel(), prompt, gameInfo);
                string? translationResponse = await TranslationRequestContext.RunAsync(translationService,
                    jsonToTranslate, prompt, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                EnsureCurrentTranslation(operation, requestContext, requireStarted: false);

                if (string.IsNullOrEmpty(translationResponse))
                {
                    Console.WriteLine($"[ClipboardTranslate] Translation failed with {currentService} - empty response");
                    return null;
                }

                Console.WriteLine($"[ClipboardTranslate] Got response from {currentService}");

                // Parse the translated text from response
                string? translatedText = ExtractTranslatedTextFromResponse(translationResponse);

                return translatedText;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardTranslate] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts the inner JSON content (text_blocks) from various API response formats.
        /// Reuses the same parsing logic as ProcessTranslatedJSON.
        /// </summary>
        /// <param name="response">Raw API response</param>
        /// <param name="currentService">Current translation service name (optional, will auto-detect if null)</param>
        /// <returns>Tuple of (innerJsonContent, isGoogleTranslate) or (null, false) if extraction failed</returns>
        private (string? content, bool isGoogleTranslate) ExtractInnerContentFromResponse(string response, string? currentService = null)
        {
            try
            {
                currentService ??= ConfigManager.Instance.GetCurrentTranslationService();

                using JsonDocument doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (currentService == "Ollama" && root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("translations", out _))
                    return (response, true);

                // ChatGPT format: {"translated_text": "...", ...}
                if (currentService == "ChatGPT")
                {
                    if (root.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                    {
                        string? translatedTextJson = translatedTextElement.GetString();
                        if (!string.IsNullOrEmpty(translatedTextJson) &&
                            translatedTextJson.StartsWith("{") &&
                            translatedTextJson.EndsWith("}"))
                        {
                            return (translatedTextJson, false);
                        }
                    }
                }
                // Gemini/Ollama/LM Studio format: candidates[0].content.parts[0].text
                else if (currentService == "Gemini" || currentService == "Ollama" || currentService == "LM Studio")
                {
                    if (root.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            var text = parts[0].GetProperty("text").GetString();
                            if (text != null && text.Contains("\"text_blocks\""))
                            {
                                int jsonStart = text.IndexOf('{');
                                int jsonEnd = text.LastIndexOf('}');
                                if (jsonStart >= 0 && jsonEnd > jsonStart)
                                {
                                    return (text.Substring(jsonStart, jsonEnd - jsonStart + 1), false);
                                }
                            }
                            // Return raw text if no JSON found
                            return (text, false);
                        }
                    }
                }
                // Mistral/Groq/Custom API format: choices[0].message.content
                else if (currentService == "Mistral" || currentService == "Groq" || currentService == "Custom API")
                {
                    if (root.TryGetProperty("choices", out JsonElement choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out JsonElement message) &&
                            message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            var content = contentElement.GetString();
                            if (content != null && content.Contains("\"text_blocks\""))
                            {
                                int jsonStart = content.IndexOf('{');
                                int jsonEnd = content.LastIndexOf('}');
                                if (jsonStart >= 0 && jsonEnd > jsonStart)
                                {
                                    return (content.Substring(jsonStart, jsonEnd - jsonStart + 1), false);
                                }
                            }
                            // Return raw content if no JSON found
                            return (content, false);
                        }
                    }
                }
                // Google Translate / Yandex format: {"translations": [...]}
                else if (currentService == "Google Translate" || currentService == "Yandex")
                {
                    if (root.TryGetProperty("translations", out JsonElement _))
                    {
                        return (response, true);
                    }
                }

                // Fallback: check for direct text_blocks or translations in root
                if (root.TryGetProperty("text_blocks", out _))
                {
                    return (response, false);
                }
                if (root.TryGetProperty("translations", out _))
                {
                    return (response, true);
                }

                return (null, false);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing response: {ex.Message}");
                return (null, false);
            }
        }

        /// <summary>
        /// Extracts the translated text from text_blocks JSON.
        /// </summary>
        private string? ExtractTextFromTextBlocks(string jsonContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("text_blocks", out var textBlocks) && textBlocks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in textBlocks.EnumerateArray())
                    {
                        if (block.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString();
                        }
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the translated text from Google Translate response.
        /// </summary>
        private string? ExtractTextFromGoogleTranslate(string jsonContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("translations", out var translations) && translations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in translations.EnumerateArray())
                    {
                        if (block.TryGetProperty("translated_text", out var translatedTextElement))
                        {
                            return translatedTextElement.GetString();
                        }
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the translated text from the JSON response.
        /// Reuses parsing logic from ProcessTranslatedJSON.
        /// </summary>
        private string? ExtractTranslatedTextFromResponse(string response)
        {
            try
            {
                var (content, isGoogleTranslate) = ExtractInnerContentFromResponse(response);

                if (string.IsNullOrEmpty(content))
                {
                    Console.WriteLine("[ClipboardTranslate] Could not extract content from response");
                    return null;
                }

                if (isGoogleTranslate)
                {
                    return ExtractTextFromGoogleTranslate(content);
                }

                // Try to extract from text_blocks
                var result = ExtractTextFromTextBlocks(content);
                if (result != null)
                {
                    return result;
                }

                // If no text_blocks found, return the raw content (might be plain text from LLM)
                return content.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardTranslate] Error: {ex.Message}");
                // Response might be plain text, return as-is
                return response.Trim();
            }
        }

        #region Auto Merge Overlapping Text

        /// <summary>
        /// Temporary class to hold text block data for merging
        /// </summary>
        private class TempTextBlock
        {
            public string Text { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Confidence { get; set; }
            public List<double> LineHeights { get; set; } = new();
            // Manga speech bubble the block sits in, null when unknown
            public int? BubbleId { get; set; }
        }

        /// <summary>
        /// Holds original bounding box info for merged blocks
        /// </summary>
        private class OriginalBounds
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
        }

        /// <summary>
        /// Returns blocks sorted in natural reading order.
        /// Detects vertical (tategaki) CJK text and orders columns right-to-left,
        /// lines top-to-bottom within a column. Otherwise orders top-to-bottom.
        /// </summary>
        private List<TempTextBlock> GetBlocksInReadingOrder(List<TempTextBlock> blocks)
        {
            if (blocks.Count <= 1)
                return new List<TempTextBlock>(blocks);

            // Detect vertical text orientation:
            //  - Source language is East Asian (ja / zh_sim / zh_tra / ko)
            //  - Majority of blocks are taller than wide (Height > Width)
            string sourceLang = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsian = sourceLang == "ja" ||
                               sourceLang == "ch_sim" ||
                               sourceLang == "ch_tra" ||
                               sourceLang == "ko";

            bool isVertical = false;
            if (isEastAsian)
            {
                int verticalCount = blocks.Count(b => b.Height > b.Width && b.Height > 0);
                isVertical = verticalCount * 2 >= blocks.Count; // >= 50% of blocks
            }

            if (!isVertical)
            {
                // Horizontal text: top-to-bottom by vertical center.
                return blocks
                    .OrderBy(b => b.Y + (b.Height / 2.0))
                    .ToList();
            }

            // Vertical text (tategaki): group blocks into columns by horizontal overlap,
            // order columns right-to-left, and within each column top-to-bottom.
            // Each block is treated as belonging to one column (typical for vertical CJK OCR).
            var byY = blocks
                .OrderBy(b => b.Y + (b.Height / 2.0))
                .ToList();

            var columns = new List<List<TempTextBlock>>();
            foreach (var block in byY)
            {
                double blockCenterX = block.X + (block.Width / 2.0);
                double blockLeft = block.X;
                double blockRight = block.X + block.Width;

                // Find an existing column whose X range overlaps this block.
                List<TempTextBlock>? targetColumn = null;
                foreach (var col in columns)
                {
                    var rep = col[0];
                    double repLeft = rep.X;
                    double repRight = rep.X + rep.Width;
                    // Overlap test on X axis (with small tolerance)
                    bool xOverlap = (blockRight + 5.0 >= repLeft) && (blockLeft - 5.0 <= repRight);
                    if (xOverlap)
                    {
                        targetColumn = col;
                        break;
                    }
                }

                if (targetColumn == null)
                {
                    targetColumn = new List<TempTextBlock>();
                    columns.Add(targetColumn);
                }
                targetColumn.Add(block);
            }

            // Order columns right-to-left (descending by max X), within column top-to-bottom.
            var ordered = new List<TempTextBlock>();
            foreach (var col in columns.OrderByDescending(c => c.Max(b => b.X + b.Width)))
            {
                // Column already sorted by Y above; preserve that order.
                ordered.AddRange(col);
            }
            return ordered;
        }

        /// <summary>
        /// Merges text blocks that physically overlap with each other
        /// Returns tuple of merged blocks and their original bounding boxes
        /// </summary>
        private (List<TempTextBlock> blocks, List<OriginalBounds> originalBounds) MergeOverlappingTextBlocks(List<TempTextBlock> blocks)
        {
            if (blocks.Count <= 1)
            {
                var bounds = blocks.Select(b => new OriginalBounds
                {
                    MinX = b.X,
                    MinY = b.Y,
                    MaxX = b.X + b.Width,
                    MaxY = b.Y + b.Height
                }).ToList();
                return (blocks, bounds);
            }

            // Sort blocks to ensure correct reading order before merging.
            // Two orientation modes are supported:
            //  - Horizontal text (LTR, top-to-bottom lines): sort by CenterY ascending.
            //  - Vertical text (Japanese tategaki / vertical CJK): columns are read
            //    right-to-left, and within a column top-to-bottom.
            //
            // Vertical orientation is detected when the source language is East Asian
            // (ja/zh/ko) AND most blocks are taller than they are wide (Height > Width).
            // This avoids mis-detecting horizontal CJK dialogue boxes as vertical.
            //
            // Using CenterY (instead of Bounds.Y / top) is more robust against OCR jitter
            // and skewed/curved text where the top edge doesn't reflect the true vertical
            // position. This fixes the issue where merged multi-line text came out in the
            // wrong order (e.g. 2-1-3) because the merge order depended on the OCR array order.
            var sortedBlocks = GetBlocksInReadingOrder(blocks);

            var result = new List<TempTextBlock>(sortedBlocks);
            var boundsResult = sortedBlocks.Select(b => new OriginalBounds
            {
                MinX = b.X,
                MinY = b.Y,
                MaxX = b.X + b.Width,
                MaxY = b.Y + b.Height
            }).ToList();
            bool merged;

            do
            {
                merged = false;
                for (int i = 0; i < result.Count; i++)
                {
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        if (DoBlocksOverlap(result[i], result[j]))
                        {
                            // Merge the two blocks
                            var mergedBlock = MergeTwoBlocks(result[i], result[j]);
                            // Combine original bounding boxes
                            var combinedBounds = new OriginalBounds
                            {
                                MinX = Math.Min(boundsResult[i].MinX, boundsResult[j].MinX),
                                MinY = Math.Min(boundsResult[i].MinY, boundsResult[j].MinY),
                                MaxX = Math.Max(boundsResult[i].MaxX, boundsResult[j].MaxX),
                                MaxY = Math.Max(boundsResult[i].MaxY, boundsResult[j].MaxY)
                            };

                            // Remove higher index first to keep lower index valid
                            result.RemoveAt(j);
                            boundsResult.RemoveAt(j);
                            result.RemoveAt(i);
                            boundsResult.RemoveAt(i);

                            result.Add(mergedBlock);
                            boundsResult.Add(combinedBounds);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged);

            return (result, boundsResult);
        }

        /// <summary>
        /// Checks if two text blocks physically overlap (intersect) with each other
        /// </summary>
        private bool DoBlocksOverlap(TempTextBlock a, TempTextBlock b)
        {
            // Skip if either block has zero dimensions
            if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
                return false;

            // Text in two different manga speech bubbles never merges, however close the bubbles are
            if (a.BubbleId != null && b.BubbleId != null && a.BubbleId != b.BubbleId)
                return false;

            // Preserve headings and body text as distinct font-size groups.
            double sizeA = OverlayFontPolicy.Median(a.LineHeights), sizeB = OverlayFontPolicy.Median(b.LineHeights);
            if (!OverlayFontPolicy.CanMergeSourceSizes(sizeA, sizeB))
                return false;

            // Larger threshold to better detect adjacent/overlapping text blocks
            double overlapThreshold = 5.0; // pixels

            // Calculate the intersection rectangle
            double leftA = a.X;
            double rightA = a.X + a.Width;
            double topA = a.Y;
            double bottomA = a.Y + a.Height;

            double leftB = b.X;
            double rightB = b.X + b.Width;
            double topB = b.Y;
            double bottomB = b.Y + b.Height;

            // Check if rectangles overlap (with threshold for adjacent blocks)
            bool xOverlap = (rightA + overlapThreshold >= leftB) && (leftA - overlapThreshold <= rightB);
            bool yOverlap = (bottomA + overlapThreshold >= topB) && (topA - overlapThreshold <= bottomB);

            return xOverlap && yOverlap;
        }

        /// <summary>
        /// Merges two text blocks into one
        /// </summary>
        private TempTextBlock MergeTwoBlocks(TempTextBlock a, TempTextBlock b)
        {
            // Determine merge order based on vertical center position.
            // CenterY is more stable than the top edge (Y) for skewed/curved text
            // and avoids wrong line ordering when OCR bounding boxes overlap vertically.
            double centerA = a.Y + (a.Height / 2.0);
            double centerB = b.Y + (b.Height / 2.0);
            if (centerA > centerB)
            {
                var temp = a;
                a = b;
                b = temp;
            }

            return new TempTextBlock
            {
                Text = a.Text + " " + b.Text,
                X = Math.Min(a.X, b.X),
                Y = Math.Min(a.Y, b.Y),
                Width = Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X),
                Height = Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y),
                LineHeights = a.LineHeights.Concat(b.LineHeights).ToList(),
                Confidence = Math.Min(a.Confidence, b.Confidence),
                BubbleId = a.BubbleId ?? b.BubbleId
            };
        }

        #endregion
    }
}
