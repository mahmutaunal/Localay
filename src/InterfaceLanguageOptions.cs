using System.Windows.Controls;
using System.Windows.Data;
using System.IO;

namespace AlpWareStudio.Localay;

internal static class InterfaceLanguageOptions
{
    private static readonly (string Code, string Name)[] Catalog =
    [
        ("en", "English"), ("tr", "Türkçe"), ("vi", "Tiếng Việt"), ("zh_cn", "简体中文"),
        ("ja", "日本語"), ("ko", "한국어"), ("ru", "Русский"), ("pt_br", "Português (Brasil)")
    ];

    internal static IEnumerable<string> AvailableCodes => Catalog
        .Where(item => item.Code == "en" || File.Exists(Path.Combine(AppContext.BaseDirectory, "Languages", item.Code + ".json")))
        .Select(item => item.Code);

    internal static void Populate(System.Windows.Controls.ComboBox combo)
    {
        combo.Items.Clear();
        var system = new ComboBoxItem { Tag = PreferenceRules.System };
        system.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings[Preference_System]") { Source = LocalizationManager.Instance });
        combo.Items.Add(system);
        var available = new HashSet<string>(AvailableCodes);
        foreach (var item in Catalog.Where(item => available.Contains(item.Code)))
            {
            var option = new ComboBoxItem { Tag = item.Code };
            UiText.Bind(option, ContentControl.ContentProperty, "Language_" + item.Code);
            combo.Items.Add(option);
        }
    }

    internal static void SelectPreference(System.Windows.Controls.ComboBox combo, string preference)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == preference)
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == "en");
    }
}
