using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay;

// Optional package, never downloaded or loaded for users of another provider.
internal sealed class MarianTranslationService : ITranslationService
{
    internal const string ServiceName = "Marian (local)";
    private static readonly string PackageDirectory = Path.Combine(AppContext.BaseDirectory, "LocalModels", "opus-en-tr");
    private static readonly Lazy<MarianPackage> Package = new(() => MarianPackage.Read(PackageDirectory));
    private static readonly Lazy<string> ManifestIdentity = new(() => "marian-en-tr-v1:" +
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(PackageDirectory, "profile.json")))));
    private static readonly Lazy<MarianRuntime> Runtime = new(() =>
    {
        if (Package.Value.Identity != ManifestIdentity.Value)
            throw new InvalidDataException("The model package changed. Restart Localay before using it.");
        return new MarianRuntime(Package.Value);
    });
    internal static bool HasPackage => File.Exists(Path.Combine(PackageDirectory, "profile.json"));
    internal static string CacheIdentity
    {
        get
        {
            try { return ManifestIdentity.Value; }
            catch (Exception) { return "marian-en-tr:not-ready"; }
        }
    }

    public async Task<string?> TranslateAsync(string jsonData, string prompt)
    {
        var cancellation = TranslationRequestContext.Cancellation;
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            if (!TranslationBlockProtocol.IsEnabled(doc.RootElement)) throw new ArgumentException("Marian requires the screen block protocol.");
            TranslateGemmaClient.RequireEnglishTurkish(doc.RootElement);
            var blocks = TranslationBlockProtocol.ReadBlocks(doc.RootElement);
            if (blocks.Length is 0 or > 15 || blocks.Any(b => b.Text.Length > 4096))
                throw new ArgumentException("Local translation input exceeds the profile limit.");
            cancellation.ThrowIfCancellationRequested();
            // The native work is not cancellable. Await its real end so the shared provider
            // slot cannot be reused while cancelled native work is still running.
            var values = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                return Runtime.Value.Translate(blocks.Select(b => b.Text).ToArray(), cancellation);
            }).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return TranslationBlockProtocol.DirectResponse(blocks, values);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TranslationRequestContext.ReportFailure(new(ex is InvalidDataException or JsonException
                ? TranslationFailureKind.InvalidResponse : TranslationFailureKind.InvalidRequest));
            // Missing/incompatible assets are a readiness failure, never an online fallback.
            Console.WriteLine($"[Marian] Not ready or inference rejected: {ex.Message}");
            return null;
        }
    }
}

internal sealed record MarianPackage(string Directory, string Identity)
{
    internal static MarianPackage Read(string directory)
    {
        string manifestPath = Path.Combine(directory, "profile.json");
        byte[] manifest = File.ReadAllBytes(manifestPath);
        using var doc = JsonDocument.Parse(manifest);
        var root = doc.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1 ||
            root.GetProperty("model").GetString() != "Helsinki-NLP/opus-mt-tc-big-en-tr" ||
            root.GetProperty("runtime").GetString() != "CTranslate2-4.8.3" ||
            root.GetProperty("tokenizer").GetString() != "SentencePiece-0.2.1" ||
            root.GetProperty("compute").GetString() != "cpu-int8" ||
            root.GetProperty("source").GetString() != "en" || root.GetProperty("target").GetString() != "tr" ||
            !IsHex(root.GetProperty("revision").GetString(), 40))
            throw new InvalidDataException("Unsupported or unpinned Marian profile.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in root.GetProperty("files").EnumerateObject())
        {
            if (file.Name.Contains('\\') || file.Name.Split('/').Any(p => p is "" or "." or "..") ||
                Path.IsPathRooted(file.Name) || !IsHex(file.Value.GetString(), 64) || !hashes.TryAdd(file.Name, file.Value.GetString()!))
                throw new InvalidDataException("Invalid package file identity.");
        }
        foreach (string required in new[] { "localay_marian.dll", "source.spm", "target.spm", "model/model.bin", "model/config.json" })
            if (!hashes.ContainsKey(required)) throw new InvalidDataException("Incomplete local package.");
        foreach (var file in hashes)
        {
            using var stream = File.OpenRead(Path.Combine(directory, file.Key));
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Local package hash mismatch: " + file.Key);
        }
        // Include runtime dependencies and vocabularies, not just model weights, in identity.
        foreach (string path in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative != "profile.json" && !hashes.ContainsKey(relative))
                throw new InvalidDataException("Unpinned local package asset: " + relative);
        }
        return new(directory, "marian-en-tr-v1:" + Convert.ToHexString(SHA256.HashData(manifest)));
    }

    private static bool IsHex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
}

// One model owner for the process, with a single physical inference slot. Kept resident
// between requests; teardown occurs only after any native call has released the lock.
internal sealed class MarianRuntime : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string model,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string target,
        int threads, out IntPtr context, [Out] byte[] error, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeTranslate(IntPtr context, IntPtr input,
        int count, out IntPtr output, [Out] byte[] error, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(IntPtr output, int count);
    private readonly object _gate = new();
    private IntPtr _library;
    private IntPtr _context;
    private readonly NativeTranslate _translate;
    private readonly Close? _close;
    private readonly Release _release;
    private readonly byte[] _error = new byte[1024];

    internal MarianRuntime(MarianPackage package)
    {
        _library = NativeLibrary.Load(Path.Combine(package.Directory, "localay_marian.dll"), typeof(MarianRuntime).Assembly,
            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
        try
        {
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            var open = Bind<Open>("localay_marian_open_v1");
            _translate = Bind<NativeTranslate>("localay_marian_translate_v1");
            _close = Bind<Close>("localay_marian_close_v1");
            _release = Bind<Release>("localay_marian_release_v1");
            if (open(Path.Combine(package.Directory, "model"), Path.Combine(package.Directory, "source.spm"),
                Path.Combine(package.Directory, "target.spm"), Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
                out _context, _error, _error.Length) != 0 || _context == IntPtr.Zero)
                throw new InvalidDataException(Error());
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        catch
        {
            try { if (_context != IntPtr.Zero) _close?.Invoke(_context); }
            finally { _context = IntPtr.Zero; NativeLibrary.Free(_library); _library = IntPtr.Zero; }
            throw;
        }
    }

    internal string?[] Translate(string[] input, CancellationToken cancellation)
    {
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_context == IntPtr.Zero) throw new ObjectDisposedException(nameof(MarianRuntime));
            if (input.Length is 0 or > 15 || input.Any(text => string.IsNullOrWhiteSpace(text) || text.Contains('\0')))
                throw new ArgumentException("Invalid native translation input.", nameof(input));
            IntPtr output = IntPtr.Zero;
            IntPtr inputArray = IntPtr.Zero;
            var strings = new IntPtr[input.Length];
            try
            {
                inputArray = Marshal.AllocHGlobal(checked(input.Length * IntPtr.Size));
                for (int i = 0; i < input.Length; i++)
                {
                    strings[i] = Marshal.StringToCoTaskMemUTF8(input[i]);
                    Marshal.WriteIntPtr(inputArray, i * IntPtr.Size, strings[i]);
                }
                if (_translate(_context, inputArray, input.Length, out output, _error, _error.Length) != 0 || output == IntPtr.Zero)
                    throw new InvalidDataException(Error());
                cancellation.ThrowIfCancellationRequested();
                var values = new string?[input.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(output, i * IntPtr.Size));
                    if (string.IsNullOrWhiteSpace(values[i])) throw new InvalidDataException("Empty local translation.");
                }
                return values;
            }
            finally
            {
                try { if (output != IntPtr.Zero) _release(output, input.Length); }
                finally
                {
                    foreach (var value in strings) if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value);
                    if (inputArray != IntPtr.Zero) Marshal.FreeHGlobal(inputArray);
                }
            }
        }
    }
    private string Error()
    {
        int end = Array.IndexOf(_error, (byte)0);
        return System.Text.Encoding.UTF8.GetString(_error, 0, end < 0 ? _error.Length : end);
    }

    private void OnProcessExit(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        lock (_gate)
        {
            if (_library == IntPtr.Zero) return;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            try { if (_context != IntPtr.Zero) _close?.Invoke(_context); }
            finally
            {
                _context = IntPtr.Zero;
                var library = _library;
                _library = IntPtr.Zero;
                NativeLibrary.Free(library);
            }
        }
    }
}
