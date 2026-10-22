namespace AlpWareStudio.Localay;

// Keep the visible preference unchanged until the replacement file is committed.
internal static class PreferenceCommit
{
    internal static bool TrySet(Dictionary<string, string> values, string key, string value,
        Action<IReadOnlyDictionary<string, string>> persist, out Exception? error, string? obsoleteKey = null)
    {
        return TryUpdate(values, new Dictionary<string, string> { [key] = value }, persist,
            out error, obsoleteKey == null ? [] : [obsoleteKey]);
    }

    // Persist one replacement containing every edited field before publishing any of them.
    internal static bool TryUpdate(Dictionary<string, string> values, IReadOnlyDictionary<string, string> changes,
        Action<IReadOnlyDictionary<string, string>> persist, out Exception? error, params string[] obsoleteKeys)
    {
        var candidate = new Dictionary<string, string>(values);
        foreach (string key in obsoleteKeys) candidate.Remove(key);
        foreach (var entry in changes) candidate[entry.Key] = entry.Value;
        try { persist(candidate); }
        catch (Exception exception)
        {
            error = exception;
            return false;
        }
        foreach (string key in obsoleteKeys) values.Remove(key);
        foreach (var entry in changes) values[entry.Key] = entry.Value;
        error = null;
        return true;
    }
}
