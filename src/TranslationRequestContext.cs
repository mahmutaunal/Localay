using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay;

// Existing providers retain their public interface. Cancellation flows through retries
// and fallback providers without mutable cancellation state on their shared HttpClients.
internal static class TranslationRequestContext
{
    private sealed class RequestState(CancellationToken token)
    {
        internal CancellationToken Token = token;
        internal TranslationFailure? Failure;
    }
    private static readonly AsyncLocal<RequestState?> Current = new();
    internal static CancellationToken Cancellation => Current.Value?.Token ?? default;
    internal static bool IsManaged => Current.Value != null;
    internal static void ReportFailure(TranslationFailure failure)
    {
        if (Current.Value is { } state) Interlocked.Exchange(ref state.Failure, failure);
    }

    internal static void ReportException(Exception exception)
    {
        if (Current.Value is { } state && !state.Token.IsCancellationRequested && state.Failure == null)
            ReportFailure(TranslationFailure.FromException(exception));
    }

    internal static async Task<string?> RunAsync(ITranslationService service, string json, string prompt,
        CancellationToken cancellation, Action<TranslationFailure>? onFailure = null, bool awaitProviderCompletion = false)
    {
        cancellation.ThrowIfCancellationRequested();
        var previous = Current.Value;
        var state = new RequestState(cancellation);
        Current.Value = state;
        try
        {
            var pending = InvokeMeasuredAsync(service, json, prompt);
            string? response = awaitProviderCompletion ? await pending : await pending.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (state.Failure != null || string.IsNullOrWhiteSpace(response))
                onFailure?.Invoke(state.Failure ?? new(TranslationFailureKind.InvalidResponse));
            return response;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            onFailure?.Invoke(state.Failure ?? TranslationFailure.FromException(ex));
            return null;
        }
        finally { Current.Value = previous; }
    }

    private static async Task<string?> InvokeMeasuredAsync(ITranslationService service, string json, string prompt)
    {
        // The provider may ignore cancellation. Measure its actual lifetime, not only how
        // long the foreground caller waited for it through WaitAsync.
        using var timing = PipelineDiagnostics.Measure(PipelineWork.Provider);
        return await service.TranslateAsync(json, prompt).ConfigureAwait(false);
    }
}
