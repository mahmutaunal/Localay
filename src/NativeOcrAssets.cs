using System.IO;
using System.Security.Cryptography;

namespace AlpWareStudio.Localay;

internal static class NativeOcrAssets
{
    private static readonly Lazy<bool> Verified = new(() =>
    {
        try
        {
            foreach (var (name, expected) in new[]
            {
                ("oneocr.dll", "b94f97ae4f02fbdc01b4fd09054d622e14997c0978c52b747704519b26ea94d2"),
                ("oneocr.onemodel", "f6cef38b839012cd824abf8b854ee9fa4f87d4c5265440661d66b23f4fab5155")
            })
            {
                using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, name));
                if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    });

    internal static bool IsAvailable => Verified.Value;
}
