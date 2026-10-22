namespace AlpWareStudio.Localay;

// An optional startup feature must not prevent the remaining features from loading.
internal static class StartupSequence
{
    internal static int Run(Action<string, Exception> reportFailure, params (string Name, Action Initialize)[] steps)
    {
        int failures = 0;
        foreach (var (name, initialize) in steps)
        {
            try { initialize(); }
            catch (Exception exception)
            {
                failures++;
                reportFailure(name, exception);
            }
        }
        return failures;
    }
}
