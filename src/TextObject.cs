using MenuItem = System.Windows.Controls.MenuItem;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Media;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Clipboard = System.Windows.Forms.Clipboard;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

namespace AlpWareStudio.Localay
{
    public struct TranslationAreaInfo
    {
        public Rect Bounds { get; set; }
        public int ScreenIndex { get; set; }
        public double DpiScaleX { get; set; }
        public double DpiScaleY { get; set; }

        public TranslationAreaInfo(Rect bounds, int screenIndex, double dpiScaleX, double dpiScaleY)
        {
            Bounds = bounds;
            ScreenIndex = screenIndex;
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
        }

        public double X => Bounds.X;
        public double Y => Bounds.Y;
        public double Width => Bounds.Width;
        public double Height => Bounds.Height;
    }

    public class TextObject
    {
        // Properties
        public string Text { get; set; }
        public string ID { get; set; } = Guid.NewGuid().ToString();  // Initialize with a unique ID
        internal string? TranslationContext { get; set; }
        public string TextTranslated { get; set; } = string.Empty;  // Initialize with empty string
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SolidColorBrush TextColor { get; set; }
        public SolidColorBrush BackgroundColor { get; set; }
        public UIElement? UIElement { get; set; }
        public TextBlock TextBlock { get; private set; } = null!;  // Will be initialized in CreateUIElement
        public Border Border { get; private set; } = new Border();  // Initialize with a new Border
        public System.Windows.FlowDirection FlowDirection { get; set; } = System.Windows.FlowDirection.LeftToRight;
        // Store the original capture position
        public double CaptureX { get; set; }
        public double CaptureY { get; set; }

        // Width the translation may wrap at; the overlay layout pass widens it into free space
        public double LayoutWidth { get; private set; }
        // Lowest font size allowed, as a fraction of the estimated source font size
        public double MinSourceSizeRatio { get; private set; } = DefaultMinSourceSizeRatio;
        // Whether the text fit the original box height at the chosen font size
        public bool TextFitsBox { get; private set; } = true;

        // Sticky until fresh OCR rebuilds this patch; an old translation cannot re-enable it.
        internal bool SourceFrameCurrent { get; set; } = true;
        internal bool LayoutAdmitted { get; set; }
        private readonly record struct RenderState(string Text, System.Windows.FlowDirection Direction, Color Foreground, Color Background,
            double X, double Y, double Width, double Height, double LineHeight, double ScaleX, double ScaleY,
            OverlayTextOptions Options, object? Eraser);
        private RenderState? _appliedState;
        private long _contentVersion, _fitVersion = -1;
        private double _fitWidth;
        private System.Windows.Media.Effects.DropShadowEffect? _halo;

        internal bool CanReuseSource(OcrSceneBlock source, double lineHeight)
        {
            var previous = SourceBlock;
            return SourceFrameCurrent && _appliedState?.Options == Options && previous.Text == source.Text &&
                OverlayPlacementPolicy.SameSource(new(previous.X, previous.Y, previous.Width, previous.Height),
                    new(source.X, source.Y, source.Width, source.Height)) &&
                Math.Abs(SourceLineHeight * _renderScaleY - lineHeight) <= 2;
        }

        internal void FitForAvailableWidth(double width)
        {
            if (_fitVersion == _contentVersion && _fitWidth == width) return;
            Relayout(width, DefaultMinSourceSizeRatio);
            if (!TextFitsBox) Relayout(width, DefaultMinSourceSizeRatio, allowShrink: true);
            _fitWidth = width;
            _fitVersion = _contentVersion;
        }
        private double? _gameFontSize;
        private double LayoutHeight => _gameFontSize is double size
            ? Math.Max(Height, size * OverlayFontPolicy.LineHeightRatio * 2) : Height;
        internal string PresentationFontFamily => Options.FontFamily;

        internal void ApplyGameAppearance(double preferredSize)
        {
            bool changed = _gameFontSize != preferredSize || EraserElement != null || BackgroundColor.Color.A != 255;
            _gameFontSize = preferredSize;
            EraserElement = null;
            _eraserPixelBounds = null;
            Border.IsHitTestVisible = false;
            Border.Cursor = Cursors.Arrow;
            if (BackgroundColor.Color.A != 255)
            {
                var color = BackgroundColor.Color;
                BackgroundColor = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            }
            if (!changed) return;
            _appliedState = null;
            UpdateUIElement();
        }

        internal OcrSceneBlock SourceBlock => _captureGeometry is { } p
            ? new(Text, p.X, p.Y, p.Width, p.Height)
            : new(Text, X * _renderScaleX, Y * _renderScaleY, Width * _renderScaleX, Height * _renderScaleY);
        internal bool IntersectsCaptureChange(FrameChangeMap changes)
        {
            var p = SourceBlock;
            return changes.Intersects(p.X, p.Y, p.Width, p.Height) ||
                (_eraserPixelBounds is Rect patch && changes.Intersects(patch.X, patch.Y, patch.Width, patch.Height));
        }

        private OverlayPixelGeometry? _captureGeometry;
        private double _renderScaleX = 1, _renderScaleY = 1;
        private Rect? _eraserPixelBounds;
        internal double SourceLineHeight { get; private set; }
        // Render hosts/tests may supply immutable options without initializing application settings.
        internal OverlayTextOptions? OptionsOverride { get; init; }
        private bool _allowFontShrink;
        private OverlayTextOptions Options => OptionsOverride ?? new(
            ConfigManager.Instance.IsAutoSizeTextBlocksEnabled(), ConfigManager.Instance.IsAutoSetOverlayBackground(),
            ConfigManager.Instance.IsLanguageFontOverrideEnabled() ? ConfigManager.Instance.GetLanguageFontFamily() : DefaultFontFamily,
            ConfigManager.Instance.GetLanguageFontSizeMin(), ConfigManager.Instance.GetLanguageFontSizeMax());

        internal void BindCaptureGeometry(OverlayPixelGeometry pixels, double scaleX, double scaleY)
        {
            _captureGeometry = pixels;
            ApplyRenderDpi(scaleX, scaleY, force: true);
        }

        internal void ApplyRenderDpi(double scaleX, double scaleY, bool force = false)
        {
            if (_captureGeometry is not { } pixels) return;
            scaleX = OverlayPixelGeometry.ValidScale(scaleX);
            scaleY = OverlayPixelGeometry.ValidScale(scaleY);
            if (!force && scaleX == _renderScaleX && scaleY == _renderScaleY) return;
            _renderScaleX = scaleX; _renderScaleY = scaleY;
            var logical = pixels.ToDips(scaleX, scaleY);
            X = logical.X; Y = logical.Y; Width = logical.Width; Height = logical.Height;
            SourceLineHeight = logical.LineHeight;
            if (EraserElement != null && _eraserPixelBounds is Rect patch)
            {
                EraserElement.Width = patch.Width / scaleX;
                EraserElement.Height = patch.Height / scaleY;
                _eraserOffset = new Vector(patch.X / scaleX - X, patch.Y / scaleY - Y);
                PositionEraser();
            }
            if (TextBlock != null) UpdateUIElement();
        }

        // Feather width (DIPs) of the background edge when overlay colors are sampled automatically
        private const double SoftEdgeRadius = 6;
        // Glow around translated text drawn directly on the erased background
        private const double TextHaloRadius = 6;
        private System.Windows.Shapes.Rectangle? _softBackground;

        // Fallback chain used when no language font override is set
        private const string DefaultFontFamily = "Arial, Noto Sans SC, Noto Sans JP, Noto Sans, Noto Sans KR, Noto Sans Devanagari, MS Gothic, Malgun Gothic, Yu Gothic, Microsoft YaHei, Arial Unicode MS";

        // Audio player for click sound
        private static SoundPlayer? _soundPlayer;

        // Constructor with default parameters
        public TextObject(
            string text = "",
            double x = 0,
            double y = 0,
            double width = 0,
            double height = 0,
            SolidColorBrush? textColor = null,
            SolidColorBrush? backgroundColor = null,
            double captureX = 0,
            double captureY = 0)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            TextColor = textColor ?? new SolidColorBrush(ConfigManager.Instance.GetOverlayTextColor());
            BackgroundColor = backgroundColor ?? new SolidColorBrush(ConfigManager.Instance.GetOverlayBackgroundColor()); // Half-transparent black
            CaptureX = captureX;
            CaptureY = captureY;

            // Initialize sound player if not already initialized
            _soundPlayer ??= new SoundPlayer();

            // Create the UI element that will be added to the overlay
            //UIElement = CreateUIElement();
        }

        // Create a UI element with the current properties
        // Public so it can be used by MonitorWindow
        public UIElement CreateUIElement(bool useRelativePosition = true)
        {
            _appliedState = null;
            _fitVersion = -1;
            // Create a border for the background
            Border = new Border()
            {
                Background = BackgroundColor,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand  // Change cursor to hand to indicate clickable
            };

            // Position based on useRelativePosition parameter
            if (useRelativePosition)
            {
                // Use Margin for positioning in the parent container
                Border.Margin = new Thickness(X, Y, 0, 0);
            }
            TextBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(TextTranslated) ? Text : TextTranslated,
                Foreground = TextColor,
                FlowDirection = FlowDirection,
                FontWeight = FontWeights.Normal,
                FontSize = 24,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                FontStretch = FontStretches.Normal,
                FontFamily = new FontFamily(Options.FontFamily),
                Margin = new Thickness(0),
                TextTrimming = TextTrimming.None
            };

            ResetLayout();
            ApplyBoxConstraints();

            // Background layer under the text, so its edges can be feathered without blurring the text
            _softBackground = new System.Windows.Shapes.Rectangle
            {
                // Extend past the box by the blur radius so the box itself stays fully covered
                Margin = new Thickness(-SoftEdgeRadius),
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = SoftEdgeRadius },
                IsHitTestVisible = false
            };
            var layers = new Grid();
            layers.Children.Add(_softBackground);
            layers.Children.Add(TextBlock);
            Border.Child = layers;
            ApplyBackground();

            // Add click event handler
            // Border.MouseLeftButtonDown += Border_MouseLeftButtonDown;

            // Add context menu for right-click options
            // Border.ContextMenu = CreateContextMenu();

            // Fit before attaching the visual; Loaded must not resize it a frame later.
            AdjustFontSize(Border, TextBlock);

            return Border;
        }

        // Update the UI element with current properties
        internal void ApplySavedResultAppearance(bool preserveSampledColors)
        {
            if (!preserveSampledColors)
            {
                TextColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayTextColor());
                BackgroundColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayBackgroundColor());
            }
            if (!ConfigManager.Instance.IsAutoSetOverlayBackground()) EraserElement = null;
            UpdateUIElement();
        }

        public void UpdateUIElement()
        {
            // Check if we need to run on the UI thread
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateUIElement());
                return;
            }

            var options = Options;
            string displayText = string.IsNullOrWhiteSpace(TextTranslated) ? Text : TextTranslated;
            var state = new RenderState(displayText, FlowDirection, TextColor.Color, BackgroundColor.Color,
                X, Y, Width, Height, SourceLineHeight, _renderScaleX, _renderScaleY, options, EraserElement);
            if (TextBlock != null && _appliedState == state) return;
            if (TextBlock == null) UIElement = CreateUIElement(useRelativePosition: false);
            TextBlock.Text = displayText;
            TextBlock.FlowDirection = FlowDirection;
            TextBlock.Foreground = TextColor;
            ApplyBackground();
            Border.Margin = new Thickness(0);
            Canvas.SetLeft(Border, X); Canvas.SetTop(Border, Y);
            ResetLayout();
            ApplyBoxConstraints();
            if (TextBlock.FontFamily.Source != options.FontFamily) TextBlock.FontFamily = new FontFamily(options.FontFamily);
            AdjustFontSize(Border, TextBlock);
            _appliedState = state;
            _contentVersion++;
        }

        // Original background with the source text removed, placed on the overlay under all translations
        public System.Windows.Controls.Image? EraserElement { get; private set; }
        private Vector _eraserOffset;

        public void SetErasedBackground(ImageSource patch, Rect bounds)
        {
            EraserElement = new System.Windows.Controls.Image
            {
                Source = patch,
                Width = bounds.Width,
                Height = bounds.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            _eraserPixelBounds = new Rect(bounds.X * _renderScaleX, bounds.Y * _renderScaleY,
                bounds.Width * _renderScaleX, bounds.Height * _renderScaleY);
            // Relative to the text position, so it follows when text objects are shifted
            _eraserOffset = new Vector(bounds.X - X, bounds.Y - Y);
        }

        // The eraser stays over the original text even when the translation box is moved by the layout pass
        public void PositionEraser()
        {
            if (EraserElement == null)
                return;
            Canvas.SetLeft(EraserElement, X + _eraserOffset.X);
            Canvas.SetTop(EraserElement, Y + _eraserOffset.Y);
        }

        // With the original text erased, the translation needs no box: only a halo in the background
        // color keeps it legible over busy art. Otherwise sampled colors get soft edges, and a
        // user-chosen color keeps a crisp box.
        private void ApplyBackground()
        {
            if (_gameFontSize.HasValue)
            {
                Border.Background = BackgroundColor;
                if (_softBackground != null) _softBackground.Visibility = Visibility.Collapsed;
                TextBlock.Effect = null;
                return;
            }
            bool erased = EraserElement != null;
            bool soft = !erased && _softBackground != null && Options.AutoBackground;
            Border.Background = erased || soft ? null : BackgroundColor;
            if (_softBackground != null)
            {
                _softBackground.Fill = BackgroundColor;
                _softBackground.Visibility = soft ? Visibility.Visible : Visibility.Collapsed;
            }

            if (erased)
            {
                _halo ??= new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = TextHaloRadius, Opacity = 1 };
                _halo.Color = BackgroundColor.Color;
                TextBlock.Effect = _halo;
            }
            else TextBlock.Effect = null;
        }

        private void ResetLayout()
        {
            LayoutWidth = Width;
            MinSourceSizeRatio = DefaultMinSourceSizeRatio;
            _allowFontShrink = false;
        }

        private void ApplyBoxConstraints()
        {
            if (Width > 0)
            {
                // Cover at least the original text; wrap at the (possibly widened) layout width
                Border.MinWidth = Width;
                Border.MaxWidth = LayoutWidth + 20;
                TextBlock.MaxWidth = LayoutWidth;
            }

            if (Height > 0)
            {
                // Set MinHeight to ensure background covers original area
                Border.MinHeight = Height;
                // Allow Border to grow taller if text wraps to multiple lines
                Border.MaxHeight = double.PositiveInfinity;
            }
        }

        // Re-fit the text with a wider wrap width and/or a lower minimum font size (used by the overlay layout pass)
        public void Relayout(double layoutWidth, double minSourceSizeRatio, bool allowShrink = false)
        {
            if (TextBlock == null) return;
            _fitVersion = -1;
            LayoutWidth = Math.Max(Width, layoutWidth);
            MinSourceSizeRatio = minSourceSizeRatio;
            _allowFontShrink = allowShrink;
            ApplyBoxConstraints();
            AdjustFontSize(Border, TextBlock);
        }

        // Static cache for font size calculations
        private readonly record struct FontLayoutKey(string Text, string Family, FontWeight Weight, System.Windows.FontStyle Style,
            FontStretch Stretch, System.Windows.FlowDirection Direction, string Language, string Culture, double Dpi, double RenderScale,
            double Width, double Height, double Preferred, double Minimum, bool Shrink);
        private static readonly Dictionary<FontLayoutKey, (double Size, bool Fits)> _fontSizeCache = new();
        public static void ClearCache()
        {
            _fontSizeCache.Clear();
        }

        // Line spacing as a multiple of font size; WPF's default (~1.3x) wastes space in small boxes
        private const double LineHeightRatio = OverlayFontPolicy.LineHeightRatio;
        // The translation may shrink to this fraction of the estimated source font size, no smaller
        public const double DefaultMinSourceSizeRatio = 0.6;

        private static void ApplyFontSize(TextBlock textBlock, double fontSize)
        {
            textBlock.FontSize = fontSize;
            textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            textBlock.LineHeight = fontSize * LineHeightRatio;
            textBlock.LayoutTransform = Transform.Identity;
        }

        // Whether the text, wrapped at the box width, fits inside the box at this font size
        private bool FitsAt(TextBlock textBlock, double fontSize)
        {
            ApplyFontSize(textBlock, fontSize);
            textBlock.Measure(new Size(LayoutWidth, double.PositiveInfinity));
            return textBlock.DesiredSize.Height <= LayoutHeight && textBlock.DesiredSize.Width <= LayoutWidth;
        }

        // Size the text to fill the original text box, staying close to the game's own font size
        private void AdjustFontSize(Border border, TextBlock textBlock)
        {
            try
            {
                // Check if auto sizing is enabled
                if (!Options.AutoSize && !_gameFontSize.HasValue)
                {
                    // Fixed font size; still report whether it fits so the layout pass can widen the box
                    TextFitsBox = Width <= 0 || Height <= 0 || FitsAt(textBlock, 24);
                    ApplyFontSize(textBlock, 24);
                    return;
                }

                // Exit early if dimensions aren't set or text is empty
                if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    TextFitsBox = true;
                    ApplyFontSize(textBlock, 24);
                    return;
                }

                var options = Options;
                var (configMin, configMax) = OverlayFontPolicy.Bounds(options.Minimum, options.Maximum);
                double lineHeight = SourceLineHeight > 0 ? SourceLineHeight :
                    OverlayFontPolicy.SourceLineHeight(Width, Height, Text);
                double preferred = _gameFontSize is double gameSize ? Math.Clamp(gameSize, configMin, configMax)
                    : OverlayFontPolicy.PreferredSize(lineHeight, configMin, configMax);
                double minimum = Math.Min(preferred, Math.Max(configMin, preferred * (_gameFontSize.HasValue ? 0.8 : MinSourceSizeRatio)));
                var cacheKey = new FontLayoutKey(textBlock.Text, textBlock.FontFamily.Source, textBlock.FontWeight,
                    textBlock.FontStyle, textBlock.FontStretch, textBlock.FlowDirection, textBlock.Language.IetfLanguageTag,
                    System.Globalization.CultureInfo.CurrentCulture.Name, VisualTreeHelper.GetDpi(textBlock).PixelsPerDip,
                    _renderScaleY, LayoutWidth, LayoutHeight, preferred, minimum, _allowFontShrink);

                // Check cache
                if (_fontSizeCache.TryGetValue(cacheKey, out var cached))
                {
                    TextFitsBox = cached.Fits;
                    ApplyFontSize(textBlock, cached.Size);
                    return;
                }

                // Keep a stable source-derived size while trying space/wrapping first. Shrink
                // only when the overlay placement pass explicitly runs out of vertical room.
                double finalSize = OverlayFontPolicy.ChooseSize(preferred, minimum, _allowFontShrink,
                    size => FitsAt(textBlock, size));
                TextFitsBox = FitsAt(textBlock, finalSize);
                textBlock.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyFontSize(textBlock, finalSize);

                // Cache result
                if (_fontSizeCache.Count > 100) _fontSizeCache.Clear();
                _fontSizeCache[cacheKey] = (finalSize, TextFitsBox);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustFontSize: {ex.Message}");
                TextFitsBox = false;
                ApplyFontSize(textBlock, 24);
            }
        }

        // Handle click event
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                // Copy text to clipboard
                Clipboard.SetText(Text);

                // Play sound
                PlayClickSound();

                // Animate the border to provide visual feedback
                AnimateBorderOnClick(border);

                e.Handled = true;
            }
        }

        // Play a sound when clicked
        private void PlayClickSound()
        {
            // Set the sound file path
            string soundFile = "audio\\clipboard.wav";

            // Check if file exists
            if (System.IO.File.Exists(soundFile))
            {
                _soundPlayer!.SoundLocation = soundFile;
                _soundPlayer.Play();
            }
        }

        // Store original background color for each border to ensure we restore properly
        private static readonly ConditionalWeakTable<Border, SolidColorBrush> _originalBackgrounds = 
            new ConditionalWeakTable<Border, SolidColorBrush>();

        // Create the context menu with Copy Source, Copy Translated, Speak Source, and Learn Source options
        private ContextMenu CreateContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();

            // Copy Source menu item
            MenuItem copySourceMenuItem = new MenuItem();
            UiText.Bind(copySourceMenuItem, MenuItem.HeaderProperty, "Menu_CopySource");
            copySourceMenuItem.Click += CopySourceMenuItem_Click;
            contextMenu.Items.Add(copySourceMenuItem);

            // Copy Translated menu item
            MenuItem copyTranslatedMenuItem = new MenuItem();
            UiText.Bind(copyTranslatedMenuItem, MenuItem.HeaderProperty, "Menu_CopyTranslated");
            copyTranslatedMenuItem.Click += CopyTranslatedMenuItem_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);

            // Add a separator
            contextMenu.Items.Add(new Separator());

            // Learn Source menu item
            MenuItem learnSourceMenuItem = new MenuItem();
            UiText.Bind(learnSourceMenuItem, MenuItem.HeaderProperty, "Menu_LearnSource");
            learnSourceMenuItem.Click += LearnSourceMenuItem_Click;
            contextMenu.Items.Add(learnSourceMenuItem);

            // Update menu item states when context menu is opened
            contextMenu.Opened += (s, e) => {
                copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(this.TextTranslated);
            };

            return contextMenu;
        }

        // Click handler for Copy Source menu item
        private void CopySourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(this.Text);
            PlayClickSound();
        }

        // Click handler for Copy Translated menu item
        private void CopyTranslatedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this.TextTranslated))
            {
                Clipboard.SetText(this.TextTranslated);
                PlayClickSound();
            }
        }

        // Click handler for Learn Source menu item
        private void LearnSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(this.Text))
                {
                    // Construct the ChatGPT URL with the selected text and instructions
                    string chatGptPrompt = $"Create a lesson to help me learn about this text and its translation: {this.Text}";
                    string encodedPrompt = System.Web.HttpUtility.UrlEncode(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";

                    // Open in default browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });

                    Console.WriteLine($"Opening ChatGPT with text: {this.Text.Substring(0, Math.Min(50, this.Text.Length))}...");
                }
                else
                {
                    Console.WriteLine("No text available for Learn function");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Learn function: {ex.Message}");
            }
        }

        // Click handler for Speak Source menu item

        
        // Animate the border when clicked
        private void AnimateBorderOnClick(Border border)
        {
            try
            {
                // Get or store the original background color
                if (!_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush))
                {
                    // Only store the original background on first click
                    originalBrush = border.Background.Clone() as SolidColorBrush;
                    if (originalBrush != null)
                    {
                        // Freeze it to ensure it doesn't change
                        originalBrush.Freeze();
                        _originalBackgrounds.Add(border, originalBrush);
                    }
                }

                // Get the color to return to (use stored value, not current which might be mid-animation)
                Color targetColor = originalBrush?.Color ?? ((SolidColorBrush)border.Background).Color;

                // Create a new transform for this animation
                var scaleTransform = new ScaleTransform(1.0, 1.0);
                border.RenderTransform = scaleTransform;
                border.RenderTransformOrigin = new Point(0.5, 0.5);

                // Create background color animation with proper completion action
                ColorAnimation colorAnimation = new ColorAnimation()
                {
                    From = Colors.Yellow,
                    To = targetColor,
                    Duration = TimeSpan.FromSeconds(0.3),
                    // Important: Ensure animation completes and doesn't get stuck
                    FillBehavior = FillBehavior.Stop
                };

                // Create scale animation
                DoubleAnimation scaleAnimation = new DoubleAnimation()
                {
                    From = 1.1,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.2),
                    FillBehavior = FillBehavior.Stop
                };

                // Create a new brush for the animation
                SolidColorBrush animationBrush = new SolidColorBrush(Colors.Yellow);

                // When the color animation completes, reset to original background
                colorAnimation.Completed += (s, e) => 
                {
                    border.Background = originalBrush?.Clone() ?? new SolidColorBrush(targetColor);
                };

                // Start animations
                border.Background = animationBrush;
                animationBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Animation error: {ex.Message}");
                // In case of error, try to restore the original background
                if (_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush) && originalBrush != null)
                {
                    border.Background = originalBrush.Clone();
                }
            }
        }
    }
}