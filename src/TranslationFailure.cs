using System.Net;
using System.Net.Http;

namespace AlpWareStudio.Localay;

internal enum TranslationFailureKind { Unknown, Network, Timeout, RateLimited, Unauthorized, AccessDenied, InvalidRequest, Server, InvalidResponse }

internal sealed record TranslationFailure(TranslationFailureKind Kind, TimeSpan? RetryAfter = null)
{
    internal bool CanRetry => Kind is not (TranslationFailureKind.Unauthorized or TranslationFailureKind.AccessDenied or TranslationFailureKind.InvalidRequest);
    internal string MessageKey => "TranslationFailure_" + Kind;
    internal static TranslationFailure FromStatus(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        TimeSpan? delay = response.Headers.RetryAfter?.Delta;
        if (delay == null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
            delay = date - DateTimeOffset.UtcNow;
        return new(status switch
        {
            401 => TranslationFailureKind.Unauthorized,
            403 => TranslationFailureKind.AccessDenied,
            429 => TranslationFailureKind.RateLimited,
            408 or 504 => TranslationFailureKind.Timeout,
            >= 500 => TranslationFailureKind.Server,
            >= 400 and < 500 => TranslationFailureKind.InvalidRequest,
            _ => TranslationFailureKind.Unknown
        }, delay > TimeSpan.Zero ? delay : null);
    }
    internal static TranslationFailure FromException(Exception exception) => new(exception switch
    {
        OperationCanceledException => TranslationFailureKind.Timeout,
        HttpRequestException => TranslationFailureKind.Network,
        System.Text.Json.JsonException => TranslationFailureKind.InvalidResponse,
        _ => TranslationFailureKind.Unknown
    });
}

// Shared HttpClients keep their connection pools. Each async request owns its diagnostics.
internal sealed class TranslationHttpHandler : DelegatingHandler
{
    internal TranslationHttpHandler() : base(new HttpClientHandler()) { }
    internal TranslationHttpHandler(HttpMessageHandler inner) : base(inner) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                TranslationRequestContext.ReportFailure(TranslationFailure.FromStatus(response));
            return response;
        }
        catch (Exception ex)
        {
            if (!TranslationRequestContext.Cancellation.IsCancellationRequested)
                TranslationRequestContext.ReportFailure(TranslationFailure.FromException(ex));
            throw;
        }
    }
}
