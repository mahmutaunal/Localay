using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AlpWareStudio.Localay;

/// <summary>Shared formatting/binding for text created outside XAML.</summary>
internal static class UiText
{
    internal static string Get(string key) => LocalizationManager.Instance[key];
    internal static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    internal static string Service(string value) => value == "Custom API" ? Get("Service_CustomApi") : value;

    // A binding keeps already-open menus, titles and status labels in the selected language.
    internal static void Bind(DependencyObject target, DependencyProperty property, string key, params object[] args) =>
        BindingOperations.SetBinding(target, property, new System.Windows.Data.Binding("Strings")
        {
            Source = LocalizationManager.Instance,
            Converter = new TextConverter(key, args)
        });

    private sealed class TextConverter(string key, object[] args) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Only provider-bearing fields translate a provider ID; user text/profile names stay intact.
            if (key is "ApiKeys_ServiceTitle" or "Status_WaitingService" or "Status_WaitingServiceTimer"
                && args.Length > 0 && args[0] is string service)
            {
                object[] displayedArgs = (object[])args.Clone();
                displayedArgs[0] = Service(service);
                return Format(key, displayedArgs);
            }
            return Format(key, args);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
