namespace AlpWareStudio.Localay;

// Record observer failures without writing the user's application log.
internal static class App
{
    internal static readonly List<Exception> Failures = [];
    internal static void WriteCrashLog(string context, Exception exception) => Failures.Add(exception);
}
