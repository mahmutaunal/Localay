using System;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AlpWareStudio.Localay
{
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }
        private static bool _applied;

        public static void ApplyTheme(bool isDark)
        {
            var app = System.Windows.Application.Current;
            if (app == null || (_applied && IsDark == isDark)) return;
            IsDark = isDark;
            _applied = true;

            foreach (var (name, light, dark) in DesignTokens.Palette)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isDark ? dark : light);
                app.Resources[name + "Color"] = color;
                SetBrush(app, name + "Brush", color);
            }
            app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["SurfaceBrush"];
            app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["Surface2Brush"];
            app.Resources[System.Windows.SystemColors.ControlLightBrushKey] = app.Resources["Surface3Brush"];
            app.Resources[System.Windows.SystemColors.ControlDarkBrushKey] = app.Resources["BorderBrush"];
            app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextBrush"];
            app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextBrush"];
            app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
            app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = app.Resources["AccentTextBrush"];
            foreach (Window window in app.Windows) ApplyWindowCaption(window);
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

        internal static void ApplyWindowCaption(Window window)
        {
            // These documented caption attributes are available starting with Windows 11.
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window.WindowStyle == WindowStyle.None) return;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var app = System.Windows.Application.Current;
            if (app.Resources["Surface2Brush"] is not SolidColorBrush surface || app.Resources["TextBrush"] is not SolidColorBrush text) return;
            uint background = ToColorRef(surface.Color), foreground = ToColorRef(text.Color);
            _ = DwmSetWindowAttribute(hwnd, 35, ref background, sizeof(uint)); // DWMWA_CAPTION_COLOR
            _ = DwmSetWindowAttribute(hwnd, 36, ref foreground, sizeof(uint)); // DWMWA_TEXT_COLOR
        }

        private static uint ToColorRef(System.Windows.Media.Color color) => (uint)(color.R | color.G << 8 | color.B << 16);

        private static void SetBrush(System.Windows.Application app, string key, System.Windows.Media.Color color)
        {
            if (app.Resources[key] is SolidColorBrush existing && !existing.IsFrozen) existing.Color = color;
            else app.Resources[key] = new SolidColorBrush(color);
        }
    }
}
