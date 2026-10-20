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

            if (isDark)
            {
                app.Resources["BgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E");
                app.Resources["SurfaceColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252526");
                app.Resources["Surface2Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D30");
                app.Resources["Surface3Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E3E42");
                app.Resources["AccentColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC");
                app.Resources["BorderColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3F3F46");
                app.Resources["NeutralBgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333337");
                app.Resources["SuccessColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4ADE80");
                app.Resources["WarningColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FBBF24");
                app.Resources["DangerColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171");
                app.Resources["LinkColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#60A5FA");
                
                SetBrush(app, "BgBrush", (System.Windows.Media.Color)app.Resources["BgColor"]);
                SetBrush(app, "SurfaceBrush", (System.Windows.Media.Color)app.Resources["SurfaceColor"]);
                SetBrush(app, "Surface2Brush", (System.Windows.Media.Color)app.Resources["Surface2Color"]);
                SetBrush(app, "Surface3Brush", (System.Windows.Media.Color)app.Resources["Surface3Color"]);
                SetBrush(app, "NeutralBgBrush", (System.Windows.Media.Color)app.Resources["NeutralBgColor"]);
                SetBrush(app, "AccentBrush", (System.Windows.Media.Color)app.Resources["AccentColor"]);
                SetBrush(app, "BorderBrush", (System.Windows.Media.Color)app.Resources["BorderColor"]);
                SetBrush(app, "TextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F1F1F1"));
                SetBrush(app, "MutedBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A0A0A0"));
                SetBrush(app, "SuccessBrush", (System.Windows.Media.Color)app.Resources["SuccessColor"]);
                SetBrush(app, "WarningBrush", (System.Windows.Media.Color)app.Resources["WarningColor"]);
                SetBrush(app, "DangerBrush", (System.Windows.Media.Color)app.Resources["DangerColor"]);
                SetBrush(app, "LinkBrush", (System.Windows.Media.Color)app.Resources["LinkColor"]);

                app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["SurfaceBrush"];
                app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["Surface2Brush"];
                app.Resources[System.Windows.SystemColors.ControlLightBrushKey] = app.Resources["Surface3Brush"];
                app.Resources[System.Windows.SystemColors.ControlDarkBrushKey] = app.Resources["BorderBrush"];
                app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
                app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
            else
            {
                app.Resources["BgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F7FAFC");
                app.Resources["SurfaceColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF");
                app.Resources["Surface2Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F8FD");
                app.Resources["Surface3Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EBF4FB");
                app.Resources["AccentColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0066CC");
                app.Resources["BorderColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0ECF8");
                app.Resources["NeutralBgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E6EEF3");
                app.Resources["SuccessColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15803D");
                app.Resources["WarningColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B45309");
                app.Resources["DangerColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC2626");
                app.Resources["LinkColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0066CC");
                
                SetBrush(app, "BgBrush", (System.Windows.Media.Color)app.Resources["BgColor"]);
                SetBrush(app, "SurfaceBrush", (System.Windows.Media.Color)app.Resources["SurfaceColor"]);
                SetBrush(app, "Surface2Brush", (System.Windows.Media.Color)app.Resources["Surface2Color"]);
                SetBrush(app, "Surface3Brush", (System.Windows.Media.Color)app.Resources["Surface3Color"]);
                SetBrush(app, "NeutralBgBrush", (System.Windows.Media.Color)app.Resources["NeutralBgColor"]);
                SetBrush(app, "AccentBrush", (System.Windows.Media.Color)app.Resources["AccentColor"]);
                SetBrush(app, "BorderBrush", (System.Windows.Media.Color)app.Resources["BorderColor"]);
                SetBrush(app, "TextBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0B2545"));
                SetBrush(app, "MutedBrush", (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5B6B7A"));
                SetBrush(app, "SuccessBrush", (System.Windows.Media.Color)app.Resources["SuccessColor"]);
                SetBrush(app, "WarningBrush", (System.Windows.Media.Color)app.Resources["WarningColor"]);
                SetBrush(app, "DangerBrush", (System.Windows.Media.Color)app.Resources["DangerColor"]);
                SetBrush(app, "LinkBrush", (System.Windows.Media.Color)app.Resources["LinkColor"]);

                app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["SurfaceBrush"];
                app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["Surface2Brush"];
                app.Resources[System.Windows.SystemColors.ControlLightBrushKey] = app.Resources["Surface3Brush"];
                app.Resources[System.Windows.SystemColors.ControlDarkBrushKey] = app.Resources["BorderBrush"];
                app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
                app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
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
