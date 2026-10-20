using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AlpWareStudio.Localay
{
    public static class LanguageDisplay
    {
        public static string GetDisplayName(string code) => $"{GetShortName(code)} ({code})";

        public static string GetShortName(string code)
        {
            string key = "Language_" + code.ToLowerInvariant().Replace('-', '_');
            string name = LocalizationManager.Instance[key];
            return name == $"[{key}]" ? code : name;
        }
    }

    // The second binding is the UI language; it refreshes names without changing stored codes.
    public class LanguageCodeToNameConverter : IValueConverter, IMultiValueConverter
    {
        public bool ShortName { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? code = value == DependencyProperty.UnsetValue ? null : value?.ToString();
            return string.IsNullOrEmpty(code) ? "" : ShortName ? LanguageDisplay.GetShortName(code) : LanguageDisplay.GetDisplayName(code);
        }
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            Convert(values[0], targetType, parameter, culture);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
