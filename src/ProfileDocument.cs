using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

internal static class ProfileDocument
{
    internal static bool ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() ||
            name.EndsWith('.') || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) return false;
        string stem = name.Split('.')[0];
        return !Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase) && name is not ("." or "..");
    }
    internal static IReadOnlyDictionary<string, string> Read(string content, ISet<string> allowed)
    {
        var values = new Dictionary<string, string>();
        string singles = Regex.Replace(content, @"<(\w+)_start>\r?\n?(.*?)\r?\n?<\1_end>", m =>
        {
            if (allowed.Contains(m.Groups[1].Value)) values[m.Groups[1].Value] = m.Groups[2].Value;
            return "";
        }, RegexOptions.Singleline);
        foreach (string line in singles.Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd('\r'), @"^([^|\r\n]+)\|(.*)\|$");
            if (match.Success && allowed.Contains(match.Groups[1].Value) && !values.ContainsKey(match.Groups[1].Value))
                values[match.Groups[1].Value] = match.Groups[2].Value;
        }
        return values;
    }
}
