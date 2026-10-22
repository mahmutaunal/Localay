using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AlpWareStudio.Localay;

// The existing provider owns batching/IDs. This transport preserves one response slot
// per input string and never switches engines or dispatches speculative requests.
internal sealed class GoogleCloudNmtClient(HttpClient httpClient)
{
    internal async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts,
        string source, string target, string apiKey, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var empty = new string?[texts.Count];
        if (texts.Count == 0) return empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TranslationRequestContext.ReportFailure(new(TranslationFailureKind.Unauthorized));
            return empty;
        }
        if (texts.Count > 128)
        {
            TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidRequest));
            return empty;
        }
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            if (httpClient.Timeout != Timeout.InfiniteTimeSpan) budget.CancelAfter(httpClient.Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://translation.googleapis.com/language/translate/v2");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            string payload = JsonSerializer.Serialize(new
            {
                q = texts, source, target, format = "text", model = GameTranslationProfile.Model
            });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                TranslationRequestContext.ReportFailure(TranslationFailure.FromStatus(response));
                return empty;
            }
            string body = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("translations", out var values) || values.ValueKind != JsonValueKind.Array ||
                values.GetArrayLength() != texts.Count)
            {
                TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                return empty;
            }
            var result = new string?[texts.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var item = values[i];
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("translatedText", out var value) ||
                    value.ValueKind != JsonValueKind.String)
                {
                    TranslationRequestContext.ReportFailure(new(TranslationFailureKind.InvalidResponse));
                    return empty;
                }
                string translated = WebUtility.HtmlDecode(value.GetString()!);
                result[i] = string.IsNullOrWhiteSpace(translated) ? null : translated;
            }
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TranslationRequestContext.ReportException(ex);
            return empty;
        }
    }
}
