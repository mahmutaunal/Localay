using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using System.IO;
using System.Text.Json;

using Application = System.Windows.Application;

namespace AlpWareStudio.Localay
{
    class WindowsOCRManager
    {
        private static WindowsOCRManager? _instance;
        private bool _windowsOcrUnavailable;
        private string? _lastWindowsOcrFailureSignature;
        private readonly object _engineGate = new();
        private readonly SemaphoreSlim _recognitionGate = new(1, 1);
        private readonly SingleResourceCache<string, OcrEngine> _engines = new(_ => { });
        private byte[] _pixelBytes = Array.Empty<byte>();

        public string? _currentLanguageCode = null;


        public static WindowsOCRManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowsOCRManager();
                }
                return _instance;
            }
        }

        public bool CheckLanguagePackInstall(string languageCode) => GetLanguageStatus(languageCode).IsReady;

        internal OcrLanguageStatus GetLanguageStatus(string languageCode)
        {
            bool queried = TryGetAvailableRecognizerLanguages(out var languages);
            var status = OcrLanguageSetup.Evaluate(languageCode, languages.Select(l => l.LanguageTag), queried);
            _currentLanguageCode = status.RecognizerTag ?? status.CapabilityTag;
            return status;
        }

        // Convert a System.Drawing.Bitmap to a Windows.Graphics.Imaging.SoftwareBitmap
        private SoftwareBitmap ConvertBitmapToSoftwareBitmap(System.Drawing.Bitmap bitmap)
        {
            // Keep the existing contrast/brightness preprocessing. Avoid BMP encoding,
            // a stream and BitmapDecoder just to move the same BGRA pixels to WinRT.
            using var enhanced = OptimizeImageForOcr(bitmap);
            int rowBytes = checked(enhanced.Width * 4);
            int length = checked(rowBytes * enhanced.Height);
            if (_pixelBytes.Length != length) _pixelBytes = new byte[length];
            var pixels = enhanced.LockBits(new System.Drawing.Rectangle(0, 0, enhanced.Width, enhanced.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < enhanced.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Scan0 + y * pixels.Stride, _pixelBytes, y * rowBytes, rowBytes);
            }
            finally { enhanced.UnlockBits(pixels); }
            var result = new SoftwareBitmap(BitmapPixelFormat.Bgra8, enhanced.Width, enhanced.Height, BitmapAlphaMode.Premultiplied);
            try
            {
                result.CopyFromBuffer(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(_pixelBytes));
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        // Optimize the image for OCR
        private System.Drawing.Bitmap OptimizeImageForOcr(System.Drawing.Bitmap source)
        {
            // Create a new bitmap to hold the optimized image
            var result = new System.Drawing.Bitmap(source.Width, source.Height);
            
            try
            {
                // Create a graphics object for drawing on the result bitmap
                using (var graphics = System.Drawing.Graphics.FromImage(result))
                {
                    // Set the graphics object to use high-quality interpolation and smoothing
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    
                    // Create a color matrix to adjust brightness and contrast
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        // Analyze image brightness (fast method)
                        var brightnessInfo = AnalyzeImageBrightnessFast(source);
                        
                        // Calculate optimal contrast and brightness based on image analysis
                        float contrast = CalculateOptimalContrast(brightnessInfo);
                        float brightness = CalculateOptimalBrightness(brightnessInfo);
                        
                        // Create a color matrix to adjust brightness and contrast
                        float[][] colorMatrix = {
                            new float[] {contrast, 0, 0, 0, 0},
                            new float[] {0, contrast, 0, 0, 0},
                            new float[] {0, 0, contrast, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {brightness, brightness, brightness, 0, 1}
                        };
                        
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(colorMatrix));
                        
                        // Increase gamma to make the image brighter
                        attributes.SetGamma(1.1f);
                        
                        // Apply sharpening using an unsharp mask
                        if (source.Width > 100 && source.Height > 100) // Only apply to reasonably sized images
                        {
                            ApplyUnsharpMask(attributes);
                        }
                        
                        // Draw the source bitmap onto the result bitmap, applying all enhancements at once
                        graphics.DrawImage(
                            source,
                            new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                            0, 0, source.Width, source.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Image optimization failed: {ex.Message}");
                result.Dispose();
                return new System.Drawing.Bitmap(source);
            }
        }
        
        // Fast image brightness analysis using sampling
        private (double AverageBrightness, double BrightnessRange) AnalyzeImageBrightnessFast(System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Sample size - analyze fewer pixels for better performance
                int sampleSize = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 20);
                
                // Lock the bitmap data for faster access
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    bitmap.PixelFormat);
                
                try
                {
                    int bytesPerPixel = System.Drawing.Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                    int stride = bitmapData.Stride;
                    IntPtr scan0 = bitmapData.Scan0;
                    
                    byte[] pixelValues = new byte[bitmap.Height * stride];
                    System.Runtime.InteropServices.Marshal.Copy(scan0, pixelValues, 0, pixelValues.Length);
                    
                    // Sample pixels and calculate brightness
                    int totalSamples = 0;
                    double totalBrightness = 0;
                    double minBrightness = 255;
                    double maxBrightness = 0;
                    
                    for (int y = 0; y < bitmap.Height; y += sampleSize)
                    {
                        for (int x = 0; x < bitmap.Width; x += sampleSize)
                        {
                            int position = y * stride + x * bytesPerPixel;
                            
                            // Ensure we don't go out of bounds
                            if (position + 2 < pixelValues.Length)
                            {
                                byte blue = pixelValues[position];
                                byte green = pixelValues[position + 1];
                                byte red = pixelValues[position + 2];
                                
                                // Calculate brightness using the luminance formula
                                double brightness = 0.299 * red + 0.587 * green + 0.114 * blue;
                                
                                totalBrightness += brightness;
                                minBrightness = Math.Min(minBrightness, brightness);
                                maxBrightness = Math.Max(maxBrightness, brightness);
                                
                                totalSamples++;
                            }
                        }
                    }
                    
                    double averageBrightness = totalSamples > 0 ? totalBrightness / totalSamples : 128;
                    double brightnessRange = maxBrightness - minBrightness;
                    
                    return (averageBrightness, brightnessRange);
                }
                finally
                {
                    // Always unlock the bitmap
                    bitmap.UnlockBits(bitmapData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Image analysis failed: {ex.Message}");
                return (128, 128); // Default middle values
            }
        }
        
        // Calculate optimal contrast based on image brightness analysis
        private float CalculateOptimalContrast((double AverageBrightness, double BrightnessRange) brightnessInfo)
        {
            try
            {
                double range = brightnessInfo.BrightnessRange;
                
                // Low range means low contrast
                if (range < 50)
                {
                    return 1.5f; // Higher contrast for low contrast images
                }
                else if (range < 100)
                {
                    return 1.3f; // Moderate contrast boost
                }
                else if (range < 150)
                {
                    return 1.2f; // Slight contrast boost
                }
                else
                {
                    return 1.1f; // Minimal adjustment for already high contrast images
                }
            }
            catch
            {
                return 1.2f; // Default value if calculation fails
            }
        }
        
        // Calculate optimal brightness based on image brightness analysis
        private float CalculateOptimalBrightness((double AverageBrightness, double BrightnessRange) brightnessInfo)
        {
            try
            {
                double average = brightnessInfo.AverageBrightness;
                
                // Target brightness (128 is middle gray)
                double targetBrightness = 128;
                
                // Calculate brightness adjustment (normalized)
                double adjustment = (targetBrightness - average) / 255.0;
                
                // Limit adjustment range
                adjustment = Math.Max(-0.2, Math.Min(0.2, adjustment));
                
                return (float)adjustment;
            }
            catch
            {
                return 0.02f; // Default value if calculation fails
            }
        }
        
        // Apply unsharp mask to enhance edges (integrated with ImageAttributes for performance)
        private void ApplyUnsharpMask(System.Drawing.Imaging.ImageAttributes attributes)
        {
            try
            {
                // Create a color matrix that enhances edges
                // This simulates an unsharp mask by boosting contrast along edges
                float amount = 0.5f; // Sharpening amount (0.5 = 50%)
                
                // Create the color matrix for sharpening
                // This matrix enhances differences between adjacent pixels
                float[][] sharpenMatrix = {
                    new float[] {1 + amount, -amount/4, -amount/4, 0, 0},
                    new float[] {-amount/4, 1 + amount, -amount/4, 0, 0},
                    new float[] {-amount/4, -amount/4, 1 + amount, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                };
                
                // Apply the sharpening matrix
                attributes.SetColorMatrix(
                    new System.Drawing.Imaging.ColorMatrix(sharpenMatrix),
                    System.Drawing.Imaging.ColorMatrixFlag.Default,
                    System.Drawing.Imaging.ColorAdjustType.Bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unsharp mask application failed: {ex.Message}");
                // Continue without sharpening if it fails
            }
        }

        private bool TryGetAvailableRecognizerLanguages(out IReadOnlyList<Language> availableLanguages)
        {
            try
            {
                availableLanguages = OcrEngine.AvailableRecognizerLanguages.ToList();
                _windowsOcrUnavailable = false;
                return true;
            }
            catch (Exception ex)
            {
                HandleWindowsOcrActivationFailure("enumerating available recognizer languages", ex);
                availableLanguages = Array.Empty<Language>();
                return false;
            }
        }

        private OcrEngine? TryCreateOcrEngine(string languageCode)
        {
            lock (_engineGate)
            {
                try
                {
                    return _engines.GetOrCreate(languageCode.ToLowerInvariant(), () =>
                    {
                        var status = GetLanguageStatus(languageCode);
                        if (!status.IsReady || status.RecognizerTag == null)
                            throw new InvalidOperationException("The requested OCR language is not ready.");
                        var engine = OcrEngine.TryCreateFromLanguage(new Language(status.RecognizerTag));
                        if (engine == null) throw new InvalidOperationException("Windows did not create an OCR engine.");
                        _windowsOcrUnavailable = false;
                        return engine;
                    });
                }
                catch (Exception ex)
                {
                    HandleWindowsOcrActivationFailure($"creating an OCR engine for language {languageCode}", ex);
                    return null;
                }
            }
        }

        private void HandleWindowsOcrActivationFailure(string operation, Exception ex)
        {
            lock (_engineGate) _engines.Clear();
            _windowsOcrUnavailable = true;
            _currentLanguageCode = null;

            string signature = $"{operation}|0x{ex.HResult:X8}|{ex.GetType().FullName}";
            if (_lastWindowsOcrFailureSignature == signature)
            {
                return;
            }

            _lastWindowsOcrFailureSignature = signature;

            Console.WriteLine($"Windows OCR is unavailable while {operation}. HRESULT: 0x{ex.HResult:X8}. Message: {ex.Message}");

            if (ex.HResult == unchecked((int)0x80040154))
            {
                Console.WriteLine("Windows OCR activation failed because the Windows OCR runtime components are not available on this machine. The app will skip Windows OCR instead of crashing.");
            }
        }

        // Get OCR engine for the specified language
        private OcrEngine GetOcrEngine(string languageCode)
        {
            var ocrEngine = TryCreateOcrEngine(languageCode);
            if (ocrEngine == null)
            {
                throw new InvalidOperationException($"Windows OCR engine is unavailable for language '{languageCode}'.");
            }

            return ocrEngine;
        }
        
      
        
        // Get OCR lines directly from a bitmap
        public async Task<List<Windows.Media.Ocr.OcrLine>> GetOcrLinesFromBitmapAsync(System.Drawing.Bitmap bitmap, string languageCode = "en")
        {
            await _recognitionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var engine = TryCreateOcrEngine(languageCode);
                if (engine == null)
                {
                    PipelineDiagnostics.Current?.Finish(PipelineOutcome.Failed);
                    return new List<Windows.Media.Ocr.OcrLine>();
                }
                using var softwareBitmap = ConvertBitmapToSoftwareBitmap(bitmap);
                var result = await engine.RecognizeAsync(softwareBitmap);
                return result.Lines.ToList();
            }
            catch (Exception ex)
            {
                lock (_engineGate) _engines.Clear();
                PipelineDiagnostics.Current?.Finish(PipelineOutcome.Failed);
                Console.WriteLine($"Windows OCR error: {ex.GetType().Name}");
                return new List<Windows.Media.Ocr.OcrLine>();
            }
            finally { _recognitionGate.Release(); }
        }

        // Process Windows OCR results
        public Task ProcessWindowsOcrResults(List<Windows.Media.Ocr.OcrLine> textLines, string languageCode = "en", Func<bool>? canProcess = null, Action<string>? receiveResults = null)
        {
            try
            {
                // Create a JSON response similar to what EasyOCR would return, but at character level
                var results = new List<object>();
                
                // Set to true to enable character-level processing for both OCR engines
                bool useCharacterLevel = false;
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    //Console.WriteLine($"Processing line: \"{line.Text}\" with {line.Words.Count} words");
                    
                    if (useCharacterLevel)
                    {
                        // Process at character level by splitting words into characters
                        foreach (var word in line.Words)
                        {
                            var wordRect = word.BoundingRect;
                            string wordText = word.Text;
                            
                            // Skip empty words
                            if (string.IsNullOrWhiteSpace(wordText))
                                continue;
                            
                            // Determine if we should add a space marker after this word
                            bool addSpaceMarker = false;
                            
                            // Check if we should add a space marker after this word based on language
                            // For Western languages and some others, spaces between words are important
                            if (ShouldAddSpaceBetweenWords(languageCode) && word != line.Words.Last())
                            {
                                addSpaceMarker = true;
                            }
                                
                            // Calculate the width of each character in the word
                            double totalWidth = wordRect.Width;
                            double charWidth = totalWidth / wordText.Length;
                            
                    
                            double charPadding = charWidth * 0.15; //15% of character width
                            double effectiveCharWidth = charWidth - charPadding;

                            // Process each character in the word
                            for (int i = 0; i < wordText.Length; i++)
                            {
                                string charText = wordText[i].ToString();
                                
                                // Calculate the X-coordinate of the character
                                double charX = wordRect.X + (i * charWidth) + (charPadding / 2);
                                
                                // Create bounding rectangle for this character
                                var charRect = new Windows.Foundation.Rect(
                                    charX, 
                                    wordRect.Y, 
                                    effectiveCharWidth,
                                    wordRect.Height
                                );
                                
                                // Calculate box coordinates (polygon points) for the character
                                var charBox = new[] {
                                    new[] { (double)charRect.X, (double)charRect.Y },
                                    new[] { (double)(charRect.X + charRect.Width), (double)charRect.Y },
                                    new[] { (double)(charRect.X + charRect.Width), (double)(charRect.Y + charRect.Height) },
                                    new[] { (double)charRect.X, (double)(charRect.Y + charRect.Height) }
                                };
                                
                                // Add the character to the results
                                results.Add(new
                                {
                                    text = charText,
                                    confidence = 0.9, // Windows OCR doesn't provide confidence
                                    rect = charBox,
                                    is_character = true
                                });
                            }
                                
                            
                            // Add space marker after word if needed
                            if (addSpaceMarker)
                            {
                                // Create space character after the word
                                // Position it just to the right of the last character
                                double spaceWidth = charWidth * 0.6; 
                                double spaceX = wordRect.X + wordRect.Width + (charWidth * 0.1);
                                
                                var spaceRect = new Windows.Foundation.Rect(
                                    spaceX,
                                    wordRect.Y,
                                    spaceWidth,
                                    wordRect.Height
                                );
                                
                                // Calculate box coordinates for the space character
                                var spaceBox = new[] {
                                    new[] { (double)spaceRect.X, (double)spaceRect.Y },
                                    new[] { (double)(spaceRect.X + spaceRect.Width), (double)spaceRect.Y },
                                    new[] { (double)(spaceRect.X + spaceRect.Width), (double)(spaceRect.Y + spaceRect.Height) },
                                    new[] { (double)spaceRect.X, (double)(spaceRect.Y + spaceRect.Height) }
                                };
                                
                                // Add the space character to results
                                results.Add(new
                                {
                                    text = " ", // Actual space character
                                    confidence = 0.95, // High confidence for this artificially added space
                                    rect = spaceBox,
                                    is_character = true
                                });
                            }
                        }
                    }
                    else
                    {
                        // Original line-based processing (as a fallback)
                        
                        // Get bounding box coordinates - use the words to build a bounding rectangle
                        var rectBox = line.Words[0].BoundingRect; // Start with first word

                        // Find the complete bounding box for the line (all words)
                        foreach (var word in line.Words)
                        {
                            var wordRect = word.BoundingRect;
                            // Expand the rectangle to include this word
                            rectBox.X = Math.Min(rectBox.X, wordRect.X);
                            rectBox.Y = Math.Min(rectBox.Y, wordRect.Y);
                            rectBox.Width = Math.Max(rectBox.Width, wordRect.X + wordRect.Width - rectBox.X);
                            rectBox.Height = Math.Max(rectBox.Height, wordRect.Y + wordRect.Height - rectBox.Y);
                        }

                        // Calculate box coordinates (polygon points)
                        var box = new[] {
                            new[] { (double)rectBox.X, (double)rectBox.Y },
                            new[] { (double)(rectBox.X + rectBox.Width), (double)rectBox.Y },
                            new[] { (double)(rectBox.X + rectBox.Width), (double)(rectBox.Y + rectBox.Height) },
                            new[] { (double)rectBox.X, (double)(rectBox.Y + rectBox.Height) }
                        };

                        // Process text based on language
                        string processedText = ProcessTextByLanguage(line.Text, languageCode);
                        
                        // Add the text line to results
                        results.Add(new
                        {
                            text = processedText,
                            confidence = 0.9, // Windows OCR doesn't provide confidence
                            rect = box
                        });
                    }
                }

                // If no text blocks were found, try line-by-line approach with simple layout
                if (results.Count == 0)
                {
                    //Console.WriteLine("No lines found with Windows OCR, creating a simple layout");
                }

                // Create a JSON response
                var response = new
                {
                    status = "success",
                    results = results,
                    char_level = useCharacterLevel // Indicate this is character-level data
                };

                // Convert to JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

                //Console.WriteLine($"Generated Windows OCR JSON response with {results.Count} results");

                // Process the JSON response on the UI thread to handle STA requirements
                var timing = PipelineDiagnostics.Current;
                using var queueTiming = timing?.Measure(PipelineWork.DispatcherQueue);
                Application.Current.Dispatcher.Invoke((Action)(() => {
                    queueTiming?.Dispose();
                    using var timingScope = PipelineDiagnostics.Use(timing);
                    if (canProcess != null && !canProcess()) return;
                    if (receiveResults != null) receiveResults(jsonResponse);
                    else Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Windows OCR results: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            // Return a completed task since this method doesn't use await
            return Task.CompletedTask;
        }

        // Determine if spaces should be added between words for the given language
        private bool ShouldAddSpaceBetweenWords(string languageCode)
        {
            // Languages that use spaces between words
            var languagesWithSpaces = new HashSet<string> 
            { 
                "en", // English
                "es", // Spanish
                "fr", // French
                "it", // Italian
                "de", // German
                "hu", // Hungarian
                "ru", // Russian
                "ar", // Arabic
                "pt", // Portuguese
                "nl", // Dutch
                "ko"  // Korean (modern Korean uses spaces between words)
            };
            
            // Languages that don't typically use spaces between words
            var languagesWithoutSpaces = new HashSet<string>
            {
                "ja",     // Japanese
                "ch_sim", // Simplified Chinese
                // "ch_tra"  // Traditional Chinese
            };
            
            // Default to adding spaces if language is not specifically handled
            if (languagesWithoutSpaces.Contains(languageCode))
                return false;
            
            return true; // Default to adding spaces
        }

        // Process text based on language-specific rules
        private string ProcessTextByLanguage(string text, string languageCode)
        {
            string processedText = text;
            
            switch (languageCode)
            {
                case "ja": // Japanese
                    // Remove spaces between Japanese characters
                    for (int i = 0; i < 10; i++)  // Apply multiple passes to catch all instances
                    {
                        string before = processedText;
                        // Remove spaces between Japanese characters with improved regex
                        processedText = System.Text.RegularExpressions.Regex.Replace(
                            processedText, 
                            @"([\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])\s+([\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])", 
                            "$1$2");
                        
                        // If no more changes, break the loop
                        if (before == processedText)
                            break;
                    }
                    break;
                    
                // case "ch_sim": // Simplified Chinese
                // case "ch_tra": // Traditional Chinese
                //     // Remove spaces between Chinese characters
                //     for (int i = 0; i < 10; i++)
                //     {
                //         string before = processedText;
                //         processedText = System.Text.RegularExpressions.Regex.Replace(
                //             processedText, 
                //             @"([\p{IsCJKUnifiedIdeographs}])\s+([\p{IsCJKUnifiedIdeographs}])", 
                //             "$1$2");
                        
                //         if (before == processedText)
                //             break;
                //     }
                //     break;
                    
                // case "th": // Thai
                //     // Thai doesn't use spaces between words, but may have spaces for sentence breaks
                //     // Remove excess spaces but keep sentence breaks
                //     processedText = System.Text.RegularExpressions.Regex.Replace(
                //         processedText,
                //         @"([^\s])\s+([^\s])",
                //         "$1$2");
                //     break;
                    
                // case "ar": // Arabic
                // case "fa": // Persian/Farsi
                //     // For right-to-left languages, ensure proper spacing
                //     // This is a simplified approach; more complex handling might be needed
                //     processedText = System.Text.RegularExpressions.Regex.Replace(
                //         processedText,
                //         @"\s{2,}", // Replace multiple spaces with a single space
                //         " ");
                //     break;
                    
                default:
                    // For other languages, keep the text as is
                    break;
            }
            
            return processedText;
        }

    }
}
