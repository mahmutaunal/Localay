namespace AlpWareStudio.Localay;

internal static class TranslationParts
{
    internal const string Separator = "##|||##";
    // Failure is represented by an empty slot, preserving positional identity. Source text
    // is never substituted for a missing translation. Equal source/translation IS valid.
    internal static (string Text, bool Success) Reassemble(IReadOnlyList<int> map, IReadOnlyList<string?> translated)
    {
        var parts = new string[map.Count];
        bool any = false;
        for (int i = 0; i < map.Count; i++)
        {
            string? value = map[i] >= 0 && map[i] < translated.Count ? translated[map[i]] : null;
            parts[i] = string.IsNullOrWhiteSpace(value) ? "" : value;
            any |= parts[i].Length > 0;
        }
        return (string.Join(Separator, parts), any);
    }
    internal static string[]? SplitExact(string combined, int count)
    {
        var parts = combined.Split(Separator);
        return parts.Length == count ? parts : null;
    }
}
