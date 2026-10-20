using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

internal static class CredentialRedactor
{
    private static readonly ConcurrentDictionary<string, byte> Secrets = new(StringComparer.Ordinal);
    internal static void Register(string value)
    {
        foreach (string key in value.Split("+++", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (key.Length >= 6)
            {
                Secrets.TryAdd(key, 0);
                Secrets.TryAdd(Uri.EscapeDataString(key), 0);
            }
    }

    internal static string Redact(string text)
    {
        foreach (string secret in Secrets.Keys.OrderByDescending(s => s.Length))
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        text = Regex.Replace(text, @"(?i)([?&](?:key|api[_-]?key|token|access_token)=)[^\s&""'<>]+", "$1[REDACTED]");
        return Regex.Replace(text, @"(?i)(Bearer\s+)[^\s""'<>]+", "$1[REDACTED]");
    }
}

// Buffer complete lines so fragmented writes cannot leak part of a credential.
internal sealed class RedactingTextWriter(TextWriter inner) : TextWriter
{
    private readonly StringBuilder _pending = new();
    private readonly object _sync = new();
    public override Encoding Encoding => inner.Encoding;
    public override void Write(char value) => Write(value.ToString());
    public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
    public override void Write(string? value)
    {
        if (value == null) return;
        lock (_sync)
        {
            _pending.Append(value);
            string pending = _pending.ToString();
            int end = pending.LastIndexOf('\n');
            if (end < 0) return;
            inner.Write(CredentialRedactor.Redact(pending[..(end + 1)]));
            _pending.Clear().Append(pending[(end + 1)..]);
        }
    }
    public override void WriteLine(string? value) => Write(value + NewLine);
    public override void Flush()
    {
        lock (_sync)
        {
            inner.Write(CredentialRedactor.Redact(_pending.ToString()));
            _pending.Clear();
            inner.Flush();
        }
    }
}
