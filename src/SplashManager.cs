using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

// Use explicit namespaces to avoid ambiguity
using WPFPoint = System.Windows.Point;
using WPFColor = System.Windows.Media.Color;
using WPFOrientation = System.Windows.Controls.Orientation;

namespace AlpWareStudio.Localay
{
    public class SplashManager
    {
        private static SplashManager? _instance;
        public static SplashManager Instance => _instance ??= new SplashManager();

        private Window? _splashWindow;
        private TextBlock? _versionTextBlock;
        private TextBlock? _statusTextBlock;
        private Border? _loadingBar;
        private Border? _loadingBarContainer;
        
        // Event to notify when splash screen is closed
        public event EventHandler? SplashClosed;
        
        public static string CurrentVersion => AppRelease.Version;

        public void ShowSplash()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _splashWindow = new Window
                {
                    Title = "Localay",
                    Width = 420,
                    Height = 280,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Brushes.Transparent,
                    AllowsTransparency = true,
                    Topmost = true,
                    Opacity = 0 // Start invisible for fade-in animation
                };

                // Use current theme colors (dark/light) from App resources
                SolidColorBrush surfaceBrush = GetThemeBrush("SurfaceBrush", WPFColor.FromRgb(255, 255, 255));
                SolidColorBrush surface2Brush = GetThemeBrush("Surface2Brush", WPFColor.FromRgb(243, 248, 253));
                SolidColorBrush borderBrush = GetThemeBrush("BorderBrush", WPFColor.FromRgb(224, 236, 248));
                SolidColorBrush textBrush = GetThemeBrush("TextBrush", WPFColor.FromRgb(11, 37, 69));
                SolidColorBrush mutedBrush = GetThemeBrush("MutedBrush", WPFColor.FromRgb(91, 107, 122));
                SolidColorBrush accentBrush = GetThemeBrush("AccentBrush", WPFColor.FromRgb(0, 102, 204));

                // Main container - clean light design matching MainWindow
                Border mainBorder = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    BorderBrush = borderBrush,
                    Background = surfaceBrush,
                    Padding = new Thickness(30),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 0,
                        BlurRadius = 25,
                        Opacity = 0.15
                    }
                };
                
                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Logo + Title
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Loading bar
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Author

                // Top section: Logo + Title (horizontal layout like MainWindow title bar)
                StackPanel headerPanel = new StackPanel
                {
                    Orientation = WPFOrientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                // Logo with gradient background (matching MainWindow title bar style)
                Border logoBorder = new Border
                {
                    Width = 48,
                    Height = 48,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 15, 0),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0),
                        EndPoint = new WPFPoint(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(WPFColor.FromRgb(0, 170, 255), 0),   // #00aaff
                            new GradientStop(WPFColor.FromRgb(0, 204, 136), 1)    // #00cc88
                        }
                    }
                };

                System.Windows.Controls.Image appIcon = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/assets/localay.ico")),
                    Width = 32,
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                logoBorder.Child = appIcon;
                headerPanel.Children.Add(logoBorder);

                // Title stack
                StackPanel titleStack = new StackPanel
                {
                    Orientation = WPFOrientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock appTitle = new TextBlock
                {
                    Text = AppIdentity.Product,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = textBrush
                };
                titleStack.Children.Add(appTitle);

                _versionTextBlock = new TextBlock
                {
                    Text = UiText.Format("Splash_Version", CurrentVersion),
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = mutedBrush,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                titleStack.Children.Add(_versionTextBlock);

                headerPanel.Children.Add(titleStack);
                Grid.SetRow(headerPanel, 0);
                grid.Children.Add(headerPanel);

                // Status Text - clean and simple
                _statusTextBlock = new TextBlock
                {
                    Text = LocalizationManager.Instance.Strings["Splash_Starting"],
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = mutedBrush,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(_statusTextBlock, 2);
                grid.Children.Add(_statusTextBlock);

                // Loading bar - accent color matching MainWindow
                Border loadingBarContainer = new Border
                {
                    Height = 4,
                    Margin = new Thickness(20, 0, 20, 16),
                    CornerRadius = new CornerRadius(2),
                    Background = surface2Brush,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                };

                _loadingBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Width = 0,
                    Background = accentBrush
                };

                _loadingBarContainer = loadingBarContainer;
                loadingBarContainer.Child = _loadingBar;
                Grid.SetRow(loadingBarContainer, 3);
                grid.Children.Add(loadingBarContainer);

                // Author text - subtle at bottom
                TextBlock authorText = new TextBlock
                {
                    Text = UiText.Format("Splash_DevelopedBy", AppIdentity.Publisher),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromArgb(150, mutedBrush.Color.R, mutedBrush.Color.G, mutedBrush.Color.B)),
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Grid.SetRow(authorText, 4);
                grid.Children.Add(authorText);

                mainBorder.Child = grid;
                _splashWindow.Content = mainBorder;
                _splashWindow.Show();

                // === ANIMATIONS ===
                
                // 1. Fade-in animation for window
                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _splashWindow.BeginAnimation(Window.OpacityProperty, fadeIn);

                // 2. Loading bar animation
                DoubleAnimation loadingAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 320, // Window width - padding
                    Duration = TimeSpan.FromMilliseconds(1800),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                _loadingBar.BeginAnimation(Border.WidthProperty, loadingAnimation);

                CloseSplashAfterDelay(1800);
            });
        }

        private static SolidColorBrush GetThemeBrush(string key, WPFColor fallback)
        {
            try
            {
                if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)
                {
                    // Clone by color to avoid sharing mutable/frozen instances.
                    return new SolidColorBrush(brush.Color);
                }
            }
            catch
            {
                // Fallback below
            }

            return new SolidColorBrush(fallback);
        }

        private void CloseSplashAfterDelay(int delay)
        {
            Task.Delay(delay).ContinueWith(_ =>
            {
                CloseSplash();
            });
        }

        private void CloseSplash()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _splashWindow?.Close();
                _splashWindow = null;
                
                // Raise the SplashClosed event
                SplashClosed?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}