using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;


namespace AlpWareStudio.Localay
{
    public partial class MonitorWindow : Window
    {
        // Add P/Invoke declarations for window attribute
        private const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        private const int DWMWA_CLOAK = 13;
        private const int DWMWA_CLOAKED = 14;
        private const int DWM_TNP_VISIBLE = 8;
        private const int DWM_TNP_OPACITY = 4;
        private const int DWM_TNP_RECTDESTINATION = 1;
        private const int WDA_NONE = 0x00000000;
        private const int WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        
        // Extended window styles for click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public double dpiScale = 1;

        [DllImport("user32.dll")]
        private static extern int SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private double currentZoom = 1.0;
        private const double zoomIncrement = 0.1;
        private string lastImagePath = string.Empty;
        
        // Singleton pattern to match application style
        private static MonitorWindow? _instance;
        public static MonitorWindow Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MonitorWindow();
                }
                return _instance;
            }
        }

        public static void ResetInstance()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }

        // Settle time is stored in ConfigManager, no need for a local variable

        // Visibility queries must not create a new overlay while another window is closing.
        internal static bool IsResultSurfaceVisible => _instance != null && _instance.IsVisible &&
            _instance.WindowState != WindowState.Minimized &&
            _instance.imageScrollViewer.Visibility == Visibility.Visible;

        private readonly SubtitlePanelPolicy _subtitlePolicy = new();
        private readonly HashSet<TextObject> _subtitleMembers = new();
        private Border? _subtitlePanel;
        private TextBlock? _subtitleText;
        private System.Windows.Size? _subtitleMeasureConstraint;
        private System.Windows.Size _subtitleMeasuredSize;

        internal static void ResetGamePresentationIfCreated()
        {
            var window = _instance;
            if (window == null) return;
            window._subtitlePolicy.Reset();
            window._subtitleMembers.Clear();
            window._subtitleMeasureConstraint = null;
            if (window._subtitlePanel != null)
            {
                window._subtitlePanel.Visibility = Visibility.Collapsed;
                window.textOverlayCanvas.Children.Remove(window._subtitlePanel);
            }
        }

        internal static void HideForGameSessionIfCreated()
        {
            if (_instance == null) return;
            _instance.imageScrollViewer.Visibility = Visibility.Collapsed;
            _instance.Hide();
        }

        internal static void ApplyCapturePreferencesIfCreated(CapturePreferenceValues values)
        {
            var window = _instance;
            if (window == null) return;
            bool initializing = _isInitializing;
            _isInitializing = true;
            try
            {
                window.ocrMethodComboBox.SelectedItem = window.ocrMethodComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                    .FirstOrDefault(item => item.Content?.ToString() == values.OcrMethod);
                window.autoTranslateCheckBox.IsChecked = values.AutoTranslate;
            }
            finally { _isInitializing = initializing; }
        }

        internal static void RefreshAdvancedPreferencesIfCreated()
        {
            var window = _instance;
            if (window == null) return;
            bool initializing = _isInitializing;
            _isInitializing = true;
            try
            {
                window.blockDetectionPowerTextBox.Text = ConfigManager.Instance.GetBlockDetectionScale().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            finally { _isInitializing = initializing; }
        }

        internal static void RefreshResultAppearanceIfCreated()
        {
            var window = _instance;
            if (window == null) return;
            if (!ConfigManager.Instance.IsShowIconSignalEnabled()) window.HideTranslationStatus();
            window.RefreshOverlays();
        }

        public MonitorWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MonitorWindow constructor: Setting _isInitializing to true");

            InitializeComponent();
            IsVisibleChanged += (_, _) => MainWindow.NotifyResultSurfaceChanged();
            StateChanged += (_, _) => MainWindow.NotifyResultSurfaceChanged();
            imageScrollViewer.IsVisibleChanged += (_, _) => MainWindow.NotifyResultSurfaceChanged();
            foreach (var item in ocrMethodComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
                if (item.Content?.ToString() == "OneOCR") item.IsEnabled = NativeOcrAssets.IsAvailable;

            Console.WriteLine("MonitorWindow constructor started");
            this.Topmost = true;
            this.Focusable = false;
            this.ShowActivated = false;
            this.ShowInTaskbar = false;

            // Subscribe to TextObject events from Logic
            //Logic.Instance.TextObjectAdded += CreateMonitorOverlayFromTextObject;

            // Set initial status
            UpdateStatus("Status_Ready");

            // Add loaded event handler
            this.Loaded += MonitorWindow_Loaded;

            // Add location changed handler to update DPI scale when window moves between monitors
            this.LocationChanged += MonitorWindow_LocationChanged;
            this.DpiChanged += (_, _) =>
            {
                TextObject.ClearCache();
                _subtitleMeasureConstraint = null;
                _subtitleText?.InvalidateMeasure();
                RefreshOverlays();
            };
            this.SizeChanged += (_, _) =>
            {
                if (IsLoaded && ReferenceEquals(_instance, this)) RefreshOverlays();
            };

            // Add size changed handler to update scrollbars
            // this.SizeChanged += MonitorWindow_SizeChanged;

            // // Manually connect events (to ensure we have control over when they're attached)
            // ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            // autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            // autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;

            // // Add event handlers for the zoom TextBox
            // zoomTextBox.TextChanged += ZoomTextBox_TextChanged;
            // zoomTextBox.LostFocus += ZoomTextBox_LostFocus;

            // // Add KeyDown event handlers for TextBoxes to handle Enter key
            // zoomTextBox.KeyDown += TextBox_KeyDown;

            // Set default size if not already set
            if (this.Width == 0)
                this.Width = 600;
            if (this.Height == 0)
                this.Height = 500;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            Console.WriteLine("MonitorWindow constructor completed");
            if (MainWindow.Instance.Windows_Version != "Windows 10")
            {
                // Add SourceInitialized event handler to set window attributes
                this.SourceInitialized += MonitorWindow_SourceInitialized;
                Console.WriteLine("Exclude MonitorWindow from capture success");
            }
            else
            {
                // For Windows 10, just enable click-through
                this.SourceInitialized += (s, e) => EnableClickThrough();
            }
        }
        
        // Add a new method to handle SourceInitialized event
        private void MonitorWindow_SourceInitialized(object? sender, EventArgs e)
        {
            EnableExcludeFromCapture();
            EnableClickThrough();
        }

        public void EnableExcludeFromCapture()
        {
            // Get window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Set the window to be excluded from capture
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            Console.WriteLine("MonitorWindow set to be excluded from screen capture");
        }
        
        public void EnableClickThrough()
        {
            // Get window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            
            // Get current extended window style
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            
            // Add WS_EX_TRANSPARENT to allow click-through
            // Also add WS_EX_LAYERED to support transparency
            // WS_EX_NOACTIVATE keeps focus on the game; WS_EX_TOOLWINDOW hides the overlay from Alt+Tab
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            
            Console.WriteLine("MonitorWindow click-through enabled");
        }

        public void DisableExcludeFromCapture()
        {
            // Get window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Set the window to be excluded from capture
            SetWindowDisplayAffinity(hwnd, WDA_NONE);

            Console.WriteLine("MonitorWindow set to be included in screen capture");
        }

        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        // Timer for updating translation status
        private DispatcherTimer? _translationStatusTimer;
        private DateTime _translationStartTime;
        
        // OCR Method Selection Changed
        public void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            Console.WriteLine($"MonitorWindow.OcrMethodComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            if (_isInitializing)
            {
                Console.WriteLine("Skipping OCR method change during initialization");
                return;
            }
            
            if (ocrMethodComboBox.SelectedItem == null) return;
            
            string? ocrMethod = (ocrMethodComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(ocrMethod)) return;
            
            // Reset the OCR hash to force a fresh comparison after changing OCR method
            Logic.Instance.ResetHash();
            
            Console.WriteLine($"OCR method changed to: {ocrMethod}");
            
            // Clear any existing text objects
            Logic.Instance.ClearAllTextObjects();
            
            UpdateStatus("Status_UsingBuiltInOcr", ocrMethod);

            // Sync the OCR method selection with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetOcrMethod(ocrMethod);
            }
            
            // Only save if not initializing
            if (!_isInitializing)
            {
                Console.WriteLine($"Saving OCR method to config: '{ocrMethod}'");
                ConfigManager.Instance.SetOcrMethod(ocrMethod);
            }
            else
            {
                Console.WriteLine($"Skipping save during initialization for OCR method: '{ocrMethod}'");
            }
        }
        
        // Auto Translate Checkbox Changed
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool isAutoTranslateEnabled = autoTranslateCheckBox.IsChecked ?? false;
            
            Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
            
            // Clear text objects
            Logic.Instance.ClearAllTextObjects();
            Logic.Instance.ResetHash();
            
            // Force OCR to run again
            MainWindow.Instance.SetOCRCheckIsWanted(true);
            
            // Refresh overlays
            RefreshOverlays();
            
            // Sync the checkbox state with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetAutoTranslateEnabled(isAutoTranslateEnabled);
            }
        }
        
        private void MonitorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("MonitorWindow_Loaded: Starting initialization");
            
            // Set initialization flag to true to prevent saving during setup
            _isInitializing = true;
            
            // Make sure keyboard shortcuts work from this window too
            // PreviewKeyDown -= Application_KeyDown;
            // PreviewKeyDown += Application_KeyDown;

            UpdateScreenshotFromBitmap();

            // // Initialize controls from MainWindow
            // if (MainWindow.Instance != null)
            // {
            //     // Get OCR method from config
            //     string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            //     Console.WriteLine($"MonitorWindow_Loaded: Loading OCR method from config: '{ocrMethod}'");

            //     // Temporarily remove the event handler to prevent triggering
            //     // a new connection while initializing
            //     ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;

            //     // Find the matching ComboBoxItem
            //     bool foundMatch = false;
            //     foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
            //     {
            //         string itemText = comboItem.Content.ToString() ?? "";
            //         Console.WriteLine($"Comparing OCR method: '{itemText}' with config value: '{ocrMethod}'");

            //         if (string.Equals(itemText, ocrMethod, StringComparison.OrdinalIgnoreCase))
            //         {
            //             Console.WriteLine($"Found matching OCR method: '{itemText}'");
            //             ocrMethodComboBox.SelectedItem = comboItem;
            //             foundMatch = true;
            //             break;
            //         }
            //     }

            //     if (!foundMatch)
            //     {
            //         Console.WriteLine($"WARNING: Could not find OCR method '{ocrMethod}' in ComboBox. Available items:");
            //         foreach (ComboBoxItem listItem in ocrMethodComboBox.Items)
            //         {
            //             Console.WriteLine($"  - '{listItem.Content}'");
            //         }
            //     }

            //     // Log what we actually set
            //     if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedItem)
            //     {
            //         Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Content}'");
            //     }

            //     // Re-attach the event handler
            //     ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;

            //     // Make sure MainWindow has the same OCR method
            //     if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedComboItem)
            //     {
            //         string selectedOcrMethod = selectedComboItem.Content.ToString() ?? "";
            //         MainWindow.Instance.SetOcrMethod(selectedOcrMethod);
            //     }

            //     // Get auto-translate state from MainWindow
            //     bool isTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();

            //     // Initialization complete, now we can save settings changes
            //     _isInitializing = false;
            //     Console.WriteLine("MonitorWindow initialization complete. Settings changes will now be saved.");

            //     // Force the OCR method to match the config again
            //     // This ensures the config value is preserved and not overwritten
            //     string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
            //     Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");

            //     // Now that initialization is complete, save the OCR method from config
            //     ConfigManager.Instance.SetOcrMethod(configOcrMethod);

            //     // Temporarily remove event handler
            //     autoTranslateCheckBox.Checked -= AutoTranslateCheckBox_CheckedChanged;
            //     autoTranslateCheckBox.Unchecked -= AutoTranslateCheckBox_CheckedChanged;

            //     autoTranslateCheckBox.IsChecked = isTranslateEnabled;

            //     // Re-attach event handlers
            //     autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            //     autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;

            GetDpiScale();
            if (ReferenceEquals(_instance, this)) RefreshOverlays();
            // }
            
            Console.WriteLine("MonitorWindow initialization complete");
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }
        
        private void MonitorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update scrollbars when window size changes
            UpdateScrollViewerSettings();
        }
        
        // Update the monitor with a bitmap directly (no file saving required)
        public void UpdateScreenshotFromBitmap()
        {
            if (!MainWindow.IsGameSessionActive) return;
            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        try
                        {
                            
                            if (!MainWindow.IsGameSessionActive) return;
                            // Show window if needed
                            if (!IsVisible)
                            {
                                Show();
                            }
                            
                            // Update scrollbars
                            UpdateScrollViewerSettings();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error in UI thread bitmap update: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating bitmap source: {ex.Message}");
                }
                return;
            }

            // Direct UI thread path
            try
            {
                
                // Show the window if not visible
                if (!IsVisible)
                {
                    Show();
                }
                
                // Make sure scroll bars appear when needed
                UpdateScrollViewerSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating screenshot from bitmap: {ex.Message}");
                UpdateStatus("Msg_UnexpectedError", ex.Message);
            }
        }
        
        
        // Ensure scroll bars appear when needed
        private void UpdateScrollViewerSettings()
        {
            if (captureImage.Source != null)
            {
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    double width = bitmapSource.PixelWidth;
                    double height = bitmapSource.PixelHeight;
                    
                    textOverlayCanvas.Width = width;
                    textOverlayCanvas.Height = height;
                    imageContainer.Width = width;
                    imageContainer.Height = height;
                    
                    captureImage.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void GetDpiScale()
        {
            // Use MonitorFromWindow to get DPI based on actual window position,
            // not cursor position. This avoids wrong DPI at screen boundaries.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                DpiHelper.GetDpiForWindowHandle(hwnd, out double scaleX, out double scaleY);
                dpiScale = scaleX;
            }
            else
            {
                DpiHelper.GetCurrentScreenDpi(out double scaleX, out double scaleY);
                dpiScale = scaleX;
            }

            Console.WriteLine($"MonitorWindow: Got DPI scale: {dpiScale:F2}x");
        }
        
        /// <summary>
        /// Handle window location change - update DPI scale when window moves between monitors
        /// </summary>
        private void MonitorWindow_LocationChanged(object? sender, EventArgs e)
        {
            // Use MonitorFromWindow to get DPI based on actual window position,
            // not cursor position. This avoids wrong DPI at screen boundaries.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            
            DpiHelper.GetDpiForWindowHandle(hwnd, out double currentScaleX, out double currentScaleY);
            
            // Check if DPI has changed significantly
            if (Math.Abs(currentScaleX - dpiScale) > 0.01)
            {
                Console.WriteLine($"MonitorWindow: DPI scale changed from {dpiScale:F2}x to {currentScaleX:F2}x");
                dpiScale = currentScaleX;
                
                // Refresh overlays to update positions
                RefreshOverlays();
            }
        }
        
        // Handle TextObject added event
        public void CreateMonitorOverlayFromTextObject(object? sender, TextObject textObject)
        {
            try
            {
                if (textObject == null || !textObject.SourceFrameCurrent || !textObject.LayoutAdmitted) return;
                // Check for null references
                if (textObject == null || textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: TextObject or Canvas is null in OnTextObjectAdded");
                    return;
                }

                // Missing translations must leave the application's original text visible.
                if (MainWindow.Instance.GetTranslateEnabled() && string.IsNullOrWhiteSpace(textObject.TextTranslated))
                    return;

                // We need to create a NEW UI element with positioning appropriate for Canvas
                // but we'll use the existing Border and TextBlock references so updates work
                // Erased original background goes underneath every translation (index 0), so a translation
                // that grows or is pushed over another box's original text never shows that text
                if (textObject.EraserElement != null && textObject.TextBlock != null && textObject.EraserElement.Parent == null)
                {
                    textObject.PositionEraser();
                    textOverlayCanvas.Children.Insert(0, textObject.EraserElement);
                }

                if (textObject.Border != null)
                {
                    // Reset margin to zero - we'll position with Canvas instead
                    textObject.Border.Margin = new Thickness(0);
                    
                    // Position the element on the canvas using Canvas.SetLeft/Top
                    // Placement was prepared by ResolveOverlayCollisions before attachment.

                    // Keep unchanged visuals attached; only new/replaced borders enter the canvas.
                    if (textObject.Border.Parent == null) textOverlayCanvas.Children.Add(textObject.Border);
                    textObject.Border.Visibility = Visibility.Visible;
                    textObject.Border.IsHitTestVisible = false;
                }
                else
                {
                    Console.WriteLine("Warning: TextObject.Border is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text to monitor: {ex.Message}");
                UpdateStatus("Msg_OverlayError", ex.Message);
            }
        }
        
        // Zoom controls
        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom += zoomIncrement;
            ApplyZoom();
        }
        
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
            ApplyZoom();
        }
        
        // Removed ResetZoomButton_Click as it's no longer needed with the TextBox
        
        private void ApplyZoom()
        {
            // Set the transform on the container to scale both image and overlays together
            ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
            imageContainer.LayoutTransform = scaleTransform;
            
            // Make sure scroll bars are correctly shown after zoom change
            UpdateScrollViewerSettings();
            
            // Update zoom textbox
            zoomTextBox.Text = ((int)(currentZoom * 100)).ToString();
            UpdateStatus("Status_Zoom", (int)(currentZoom * 100));
            Console.WriteLine($"Zoom level changed to {(int)(currentZoom * 100)}%");
        }
        
        // Method to refresh text overlays
        public void RefreshOverlays()
        {
            try
            {
                // Check if we need to invoke on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    // Use Invoke to ensure we wait for completion
                    Dispatcher.Invoke(() => RefreshOverlays());
                    return;
                }
                
                // Check if canvas is initialized
                if (textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: textOverlayCanvas is null. Overlay refresh skipped.");
                    return;
                }
                
                // Now we're on the UI thread, safe to update UI elements
                var timing = Logic.Instance.SceneTiming;
                using var uiTiming = timing?.Measure(PipelineWork.UiApply);
                

                // Individual invalid patches stay hidden; unrelated stationary blocks remain visible.
                // Check if Logic is initialized
                if (Logic.Instance == null)
                {
                    Console.WriteLine("Warning: Logic.Instance is null. Cannot refresh text objects.");
                    return;
                }
                
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects == null)
                {
                    Console.WriteLine("Warning: Text objects collection is null.");
                    return;
                }
                
                DpiHelper.GetWindowRenderDpi(this, out double renderScaleX, out double renderScaleY);
                foreach (var text in textObjects)
                {
                    text.ApplyRenderDpi(renderScaleX, renderScaleY);
                    if (MainWindow.Instance.IsCapturingGameWindow)
                        text.ApplyGameAppearance(SubtitlePanelPolicy.UiFontSize(ActualHeight));
                    // Inherit the real WPF DPI before fitting a new visual. Hidden
                    // borders can be measured but cannot expose an intermediate layout.
                    if (text.SourceFrameCurrent && text.Border.Parent == null)
                    {
                        text.Border.Visibility = Visibility.Hidden;
                        textOverlayCanvas.Children.Add(text.Border);
                    }
                }
                ResolveOverlayCollisions(textObjects);
                var visibleObjects = textObjects.Where(t => t.SourceFrameCurrent && t.LayoutAdmitted && !_subtitleMembers.Contains(t)).ToArray();
                var desired = new HashSet<System.Windows.UIElement>();
                if (_subtitleMembers.Count > 0 && _subtitlePanel != null) desired.Add(_subtitlePanel);
                foreach (var text in visibleObjects)
                {
                    desired.Add(text.Border);
                    if (text.EraserElement != null) desired.Add(text.EraserElement);
                }
                // Remove obsolete borders and patches together. Unchanged UI elements
                // retain their parent, layout and background; no clear/re-add frame.
                foreach (var child in textOverlayCanvas.Children.Cast<System.Windows.UIElement>().ToArray())
                    if (!desired.Contains(child)) textOverlayCanvas.Children.Remove(child);
                foreach (var text in visibleObjects) CreateMonitorOverlayFromTextObject(this, text);
                if (_subtitleMembers.Count > 0 && _subtitlePanel != null)
                {
                    if (_subtitlePanel.Parent == null) textOverlayCanvas.Children.Add(_subtitlePanel);
                    _subtitlePanel.Visibility = Visibility.Visible;
                }

                if (timing != null && !timing.IsFinished)
                {
                    int translated = textObjects.Count(t => !string.IsNullOrWhiteSpace(t.TextTranslated));
                    int visible = textOverlayCanvas.IsVisible && WindowState != WindowState.Minimized ? textObjects.Count(t =>
                        !string.IsNullOrWhiteSpace(t.TextTranslated) &&
                        ((_subtitleMembers.Contains(t) && _subtitlePanel?.IsVisible == true && ReferenceEquals(_subtitlePanel.Parent, textOverlayCanvas)) ||
                         (t.Border.IsVisible && ReferenceEquals(t.Border.Parent, textOverlayCanvas)))) : 0;
                    timing.SetCoverage(textObjects.Count, translated, visible);
                    if (textObjects.Count > 0 && visible == textObjects.Count)
                        timing.Mark(PipelineStage.UiApplied);
                }

                // Draw exclude regions if enabled
                DrawExcludeRegions();
                
                //UpdateStatus("Text overlays refreshed");
                //Console.WriteLine($"Monitor window refreshed {textOverlayCanvas.Children.Count} text overlays");
            }
            catch (Exception ex)
            {
                // A failed preparation must never leave stale eraser pixels behind.
                textOverlayCanvas?.Children.Clear();
                Console.WriteLine($"Error refreshing overlays: {ex.Message}");
            }
        }

        // Use one stable caption panel when evidence is sufficient; other UI stays
        // anchored to source boxes. A mask and its complete translation are admitted together.
        private void ResolveOverlayCollisions(IReadOnlyList<TextObject> textObjects)
        {
            foreach (var text in textObjects) text.LayoutAdmitted = false;
            _subtitleMembers.Clear();
            var sources = textObjects.Where(t => t.SourceFrameCurrent && t.Width > 0 && t.Height > 0).ToArray();
            var boxes = sources.Select(t => new OverlayBox(t.X, t.Y, t.Width, t.Height)).ToArray();
            var directions = sources.Select(t => t.FlowDirection == System.Windows.FlowDirection.RightToLeft).ToArray();
            double width = ActualWidth, height = ActualHeight;
            bool translate = MainWindow.Instance.GetTranslateEnabled();
            var occupied = new List<OverlayBox>();
            if (MainWindow.Instance.IsCapturingGameWindow && translate && TryPrepareSubtitlePanel(sources, boxes, width, height, out var captionBounds))
                occupied.Add(captionBounds);
            else if (_subtitlePanel != null)
                _subtitlePanel.Visibility = Visibility.Collapsed;
            for (int i = 0; i < sources.Length; i++)
            {
                var text = sources[i];
                if (_subtitleMembers.Contains(text)) continue;
                if (text.TextBlock == null || (translate && string.IsNullOrWhiteSpace(text.TextTranslated))) continue;
                bool rtl = text.FlowDirection == System.Windows.FlowDirection.RightToLeft;
                double available = OverlayPlacementPolicy.AvailableWidth(i, boxes, width, rtl, directions);
                text.FitForAvailableWidth(available);
                if (!text.TextFitsBox) continue;
                text.Border.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                var measured = text.Border.DesiredSize;
                var placement = OverlayPlacementPolicy.Anchor(boxes[i], measured.Width, measured.Height, rtl);
                if (!OverlayPlacementPolicy.CanPresent(i, boxes, placement, width, height) ||
                    occupied.Any(box => SubtitlePanelPolicy.Overlaps(box, placement))) continue;
                occupied.Add(placement);
                Canvas.SetLeft(text.Border, placement.X); Canvas.SetTop(text.Border, placement.Y);
                text.LayoutAdmitted = true;
            }
        }

        private bool TryPrepareSubtitlePanel(IReadOnlyList<TextObject> sources, IReadOnlyList<OverlayBox> boxes,
            double width, double height, out OverlayBox bounds)
        {
            bounds = default;
            var plan = _subtitlePolicy.Observe(boxes, sources.Select(t => t.Text).ToArray(), width, height);
            if (plan == null || plan.Members.Any(i => string.IsNullOrWhiteSpace(sources[i].TextTranslated))) return false;
            var first = sources[plan.Members[0]];
            if (plan.Members.Any(i => sources[i].FlowDirection != first.FlowDirection)) return false;
            if (_subtitleText == null)
            {
                _subtitleText = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
                    TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                _subtitlePanel = new Border { Child = _subtitleText, Padding = new Thickness(8),
                    IsHitTestVisible = false, Visibility = Visibility.Hidden };
            }
            string caption = string.Join("\n", plan.Members.Select(i => sources[i].TextTranslated));
            var limits = OverlayFontPolicy.Bounds(ConfigManager.Instance.GetLanguageFontSizeMin(), ConfigManager.Instance.GetLanguageFontSizeMax());
            double fontSize = Math.Clamp(plan.FontSize, limits.Min, limits.Max);
            var constraint = new System.Windows.Size(plan.Bounds.Width - 16, double.PositiveInfinity);
            bool fontChanged = _subtitleText.FontFamily.Source != first.PresentationFontFamily;
            bool contentChanged = _subtitleText.Text != caption || _subtitleText.FlowDirection != first.FlowDirection ||
                fontChanged || _subtitleText.FontSize != fontSize;
            // A stable admitted caption stays visible. Hide only while preparing new
            // content/layout, and attach before measuring so WPF inherits the real DPI.
            if (contentChanged || _subtitleMeasureConstraint != constraint || !_subtitleText.IsMeasureValid || _subtitlePanel!.Parent == null)
                _subtitlePanel!.Visibility = Visibility.Hidden;
            if (_subtitlePanel!.Parent == null) textOverlayCanvas.Children.Add(_subtitlePanel);
            if (_subtitleText.Text != caption) _subtitleText.Text = caption;
            if (_subtitleText.FlowDirection != first.FlowDirection) _subtitleText.FlowDirection = first.FlowDirection;
            if (fontChanged) _subtitleText.FontFamily = new System.Windows.Media.FontFamily(first.PresentationFontFamily);
            _subtitleText.Foreground = first.TextColor;
            _subtitlePanel!.Background = first.BackgroundColor;
            _subtitleText.FontSize = fontSize;
            _subtitleText.LineHeight = _subtitleText.FontSize * OverlayFontPolicy.LineHeightRatio;
            // WPF invalidates this measurement when text/font/inherited layout changes.
            // Reuse only a valid measurement with exactly the same available width.
            if (_subtitleMeasureConstraint != constraint || !_subtitleText.IsMeasureValid)
            {
                _subtitleText.Measure(constraint);
                // Keep the unconstrained-height result: a subsequent parent layout
                // pass may measure the TextBlock with the panel's finite height.
                _subtitleMeasuredSize = _subtitleText.DesiredSize;
                _subtitleMeasureConstraint = constraint;
            }
            // Never truncate meaning or cover source text with an incomplete/unreadable panel.
            if (_subtitleMeasuredSize.Width > plan.Bounds.Width - 16 ||
                _subtitleMeasuredSize.Height > plan.Bounds.Height - 16) return false;
            if (_subtitlePanel.Width != plan.Bounds.Width) _subtitlePanel.Width = plan.Bounds.Width;
            if (_subtitlePanel.Height != plan.Bounds.Height) _subtitlePanel.Height = plan.Bounds.Height;
            if (Canvas.GetLeft(_subtitlePanel) != plan.Bounds.X) Canvas.SetLeft(_subtitlePanel, plan.Bounds.X);
            if (Canvas.GetTop(_subtitlePanel) != plan.Bounds.Y) Canvas.SetTop(_subtitlePanel, plan.Bounds.Y);
            foreach (int member in plan.Members) _subtitleMembers.Add(sources[member]);
            bounds = plan.Bounds;
            return true;
        }

        // Draw exclude regions on the overlay
        private void DrawExcludeRegions()
        {
            try
            {
                // Check if show exclude regions is enabled
                if (!MainWindow.Instance.GetShowExcludeRegions())
                    return;

                // Get exclude regions from MainWindow
                var excludeRegions = MainWindow.Instance.excludeRegions;
                if (excludeRegions == null || excludeRegions.Count == 0)
                    return;

                // Get MonitorWindow's physical screen position via Win32 GetWindowRect
                // This is reliable regardless of DPI — no WPF coordinate conversion needed
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                var (physLeft, physTop, _, _) = DpiHelper.GetWindowPhysicalRect(hwnd);
                
                // Get DPI scale for converting physical sizes to canvas (logical) units
                DpiHelper.GetWindowRenderDpi(this, out double dpiScaleX, out double dpiScaleY);

                // Draw each exclude region
                foreach (Rect region in excludeRegions)
                {
                    // Create a border for the exclude region
                    // Canvas uses device-independent pixels, so we need to convert from physical pixels
                    System.Windows.Shapes.Rectangle excludeRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = region.Width / dpiScaleX,  // Convert physical to logical
                        Height = region.Height / dpiScaleY,
                        Stroke = System.Windows.Media.Brushes.Red,
                        StrokeThickness = 3,
                        Fill = System.Windows.Media.Brushes.Transparent,
                        Opacity = 1.0
                    };

                    // Calculate position - convert from screen coordinates (physical) to canvas coordinates (logical)
                    double canvasX = (region.X - physLeft) / dpiScaleX;
                    double canvasY = (region.Y - physTop) / dpiScaleY;

                    // Position the rectangle on the canvas
                    Canvas.SetLeft(excludeRect, canvasX);
                    Canvas.SetTop(excludeRect, canvasY);

                    // Add to canvas
                    textOverlayCanvas.Children.Add(excludeRect);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error drawing exclude regions: {ex.Message}");
            }
        }
        
        // Update status message
        private void UpdateStatus(string key, params object[] args)
        {
            // Check if we need to invoke on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                // We're on a background thread, marshal the call to the UI thread
                Dispatcher.Invoke(() => UpdateStatus(key, args));
                return;
            }
            
            // Now we're on the UI thread, safe to update
            if (statusText != null)
                UiText.Bind(statusText, TextBlock.TextProperty, key, args);
        }
        
        // Initialize the translation status timer
        private void InitializeTranslationStatusTimer()
        {
            _translationStatusTimer = new DispatcherTimer();
            _translationStatusTimer.Interval = TimeSpan.FromSeconds(1);
            _translationStatusTimer.Tick += TranslationStatusTimer_Tick;
        }
        
        // Update the translation status timer
        private void TranslationStatusTimer_Tick(object? sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _translationStartTime;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                UiText.Bind(translationStatusLabel, TextBlock.TextProperty, "Status_WaitingServiceTimer", service, (int)elapsed.TotalMinutes, elapsed.Seconds);
            });
        }

        // Show the translation status
        public void ShowTranslationStatus(bool bSettling)
        {

            if (bSettling)
            {
                Dispatcher.Invoke(() =>
                {
                    UiText.Bind(translationStatusLabel, TextBlock.TextProperty, "Status_Settling");
                    if (ConfigManager.Instance.IsShowIconSignalEnabled())
                    {
                        translationStatusBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        translationStatusBorder.Visibility = Visibility.Collapsed;
                    }
                });

                return;
            }


            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();

            Dispatcher.Invoke(() =>
            {
                UiText.Bind(translationStatusLabel, TextBlock.TextProperty, "Status_WaitingServiceTimer", service, 0, 0);
                if (ConfigManager.Instance.IsShowIconSignalEnabled())
                {
                    translationStatusBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    translationStatusBorder.Visibility = Visibility.Collapsed;
                }

                // Start the timer if not already running
                if (_translationStatusTimer == null)
                {
                    InitializeTranslationStatusTimer();
                }

                if (!_translationStatusTimer!.IsEnabled)
                {
                    _translationStatusTimer.Start();
                }
            });
        }

        public void HideOverlay()
        {
            if (imageScrollViewer != null)
            imageScrollViewer.Opacity = 0.0;
        }
        
        public void ShowOverlay()
        {
            if (imageScrollViewer != null)
            imageScrollViewer.Opacity = 1.0;
        }
        
        // Hide the translation status
        public void HideTranslationStatus()
        {
            Dispatcher.Invoke(() =>
            {
                translationStatusBorder.Visibility = Visibility.Collapsed;
                
                // Stop the timer
                if (_translationStatusTimer != null && _translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Stop();
                }
            });
        }
        
        private bool _forceClose = false;

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceClose)
            {
                // Cho phép đóng cửa sổ
                e.Cancel = false;
                Console.WriteLine("Monitor window force closed");
            }
            else
            {
                // Chỉ ẩn cửa sổ thay vì đóng
                e.Cancel = true;
                Hide();
                Console.WriteLine("Monitor window closing operation converted to hide");
            }
        }
        
       
        
        // Handle Enter key press in TextBoxes
        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender == zoomTextBox)
                {
                    ApplyZoomFromTextBox();
                }
                
                // Remove focus from the TextBox
                System.Windows.Input.Keyboard.ClearFocus();
                
                // Mark the event as handled
                e.Handled = true;
            }
        }
        
        // Handle TextChanged event for zoom TextBox
        private void ZoomTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only check format, but don't apply yet - will apply on LostFocus
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Valid number
                if (value < 10 || value > 1000)
                {
                    // Out of range - highlight but don't change yet
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                }
                else
                {
                    // Valid range
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                }
            }
            else if (!string.IsNullOrWhiteSpace(zoomTextBox.Text))
            {
                // Invalid number
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
            }
        }
        
        // Apply zoom value when focus is lost
        private void ZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyZoomFromTextBox();
        }
        
        // Apply zoom from TextBox value
        private void ApplyZoomFromTextBox()
        {
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Clamp to valid range
                value = Math.Max(10, Math.Min(1000, value));
                
                // Update value and display
                zoomTextBox.Text = value.ToString();
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply zoom
                currentZoom = value / 100.0;
                
                // Set the transform on the container to scale both image and overlays together
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Make sure scroll bars are correctly shown after zoom change
                UpdateScrollViewerSettings();
                
                // Update status
                UpdateStatus("Status_Zoom", value);
                Console.WriteLine($"Zoom level changed to {value}%");
            }
            else
            {
                // Invalid input, revert to 100%
                zoomTextBox.Text = "100";
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply default zoom
                currentZoom = 1.0;
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Update status
                UpdateStatus("Status_ZoomReset");
            }
        }
        
       
    }
}
