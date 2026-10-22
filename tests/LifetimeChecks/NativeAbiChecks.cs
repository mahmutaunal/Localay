using System.Runtime.InteropServices;
using AlpWareStudio.Localay;

internal static class NativeAbiChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Stat(int key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Mode(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Entered();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Block(int value, IntPtr callback);

    internal static async Task RunIfConfiguredAsync(Action<bool, string> check)
    {
        string? directory = Environment.GetEnvironmentVariable("LOCALAY_MARIAN_ABI_FIXTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        IntPtr probe = NativeLibrary.Load(Path.Combine(directory, "localay_marian.dll"));
        T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(probe, name));
        var stat = Bind<Stat>("fixture_stat");
        var mode = Bind<Mode>("fixture_mode");
        var block = Bind<Block>("fixture_block");
        MarianRuntime? runtime = null;
        try
        {
            var package = new MarianPackage(directory, "ABI fixture only");
            runtime = new MarianRuntime(package);
            check(stat(0) == 1, "native ABI opens one context");
            string[] input = ["Türkçe 😀", "مرحبا", "line\n<text_0>|literal"];
            check(runtime.Translate(input, default).SequenceEqual(input.Select(s => "çeviri: " + s)),
                "native ABI preserves UTF-8, array order and literal separators");
            check(stat(1) == 0, "native success releases output in allocating module");
            for (int value = 1; value <= 3; value++)
            {
                mode(value);
                try { runtime.Translate(["test"], default); throw new Exception("Expected native failure"); }
                catch (InvalidDataException error)
                {
                    check(value != 3 || error.Message == "short", "native errors stop at first NUL without stale error bytes");
                }
                check(stat(1) == 0, "native invalid/failed response releases allocated output");
            }
            mode(0);
            int before = stat(2);
            try { runtime.Translate(["embedded\0NUL"], default); throw new Exception("Expected invalid input"); }
            catch (ArgumentException) { check(stat(2) == before, "embedded NUL rejected before native input truncation"); }

            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Entered callback = () => started.TrySetResult();
            block(1, Marshal.GetFunctionPointerForDelegate(callback));
            var active = runtime;
            Task work = Task.Run(() => active.Translate(["pending"], cancellation.Token));
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                check(!work.IsCompleted && stat(0) == 1, "cancellation retains native context until physical call completes");
            }
            finally { block(0, IntPtr.Zero); }
            try { await work.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(stat(1) == 0, "canceled native call frees returned output"); }
            GC.KeepAlive(callback);
            runtime.Dispose(); runtime.Dispose();
            check(stat(0) == 0, "native disposal closes exactly once");
            try { runtime.Translate(["test"], default); throw new Exception("Expected disposed runtime"); }
            catch (ObjectDisposedException) { check(true, "disposed native runtime rejects inference"); }
            mode(4);
            try { using var failed = new MarianRuntime(package); throw new Exception("Expected open failure"); }
            catch (InvalidDataException) { check(stat(0) == 0, "partially opened native context is closed on failure"); }
        }
        finally
        {
            block(0, IntPtr.Zero);
            runtime?.Dispose();
            mode(0);
            NativeLibrary.Free(probe);
        }
    }
}
