using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlpWareStudio.Localay;

internal sealed record ReleaseUpdate(Version Version, string Tag, string Notes, string FileName,
    Uri DownloadUrl, long Size, string Sha256);

// Public GitHub data only. No token, signing service, shell script or workflow changes required.
internal sealed class ReleaseUpdateClient(HttpClient http)
{
    internal const string ApiUrl = "https://api.github.com/repos/mahmutaunal/Localay/releases/latest";
    internal const string DownloadPrefix = "https://github.com/mahmutaunal/Localay/releases/download/";
    internal const long MaximumPackageSize = 1024L * 1024 * 1024;

    internal static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = Request(new Uri(ApiUrl));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // No published release yet.
        response.EnsureSuccessStatusCode();
        byte[] json = await ReadBoundedAsync(response.Content, 1024 * 1024, timeout.Token).ConfigureAwait(false);
        return Parse(json, current);
    }

    internal static ReleaseUpdate? Parse(byte[] json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant) ||
            !Version.TryParse(tag.TrimStart('v'), out var version)) throw new InvalidDataException("Unsupported release version.");
        var installed = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        if (version <= installed) return null;
        string name = $"Localay-{version.ToString(3)}-Setup-x64.exe";
        var matches = root.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Expected one Windows x64 installer.");
        var asset = matches[0];
        long size = asset.GetProperty("size").GetInt64();
        string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (size <= 0 || size > MaximumPackageSize || !Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Installer size or GitHub digest is missing or invalid.");
        string expected = DownloadPrefix + tag + "/" + name;
        if (asset.GetProperty("browser_download_url").GetString() != expected ||
            asset.GetProperty("state").GetString() != "uploaded") throw new InvalidDataException("Unexpected installer destination.");
        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new(version, tag, notes.Length > 16000 ? notes[..16000] : notes, name,
            new Uri(expected), size, digest[7..].ToLowerInvariant());
    }

    internal async Task<string> DownloadAsync(ReleaseUpdate release, string directory,
        IProgress<int>? progress, CancellationToken cancellation)
    {
        // Each attempt owns a new directory. Partial files never become launchable installers.
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, release.FileName + ".part");
        string completed = Path.Combine(directory, release.FileName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            using var response = await OpenDownloadAsync(release.DownloadUrl, timeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("Installer length differs from release metadata.");
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int previousProgress = -1;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    received += count;
                    if (received > release.Size) throw new InvalidDataException("Installer exceeds expected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    int percent = (int)(received * 100 / release.Size);
                    if (percent != previousProgress) { progress?.Report(percent); previousProgress = percent; }
                }
                if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer integrity verification failed.");
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(partial, completed); // Only a complete, verified payload receives the .exe name.
            return completed;
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri url, CancellationToken cancellation)
    {
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            if (!IsDownloadHost(url)) throw new InvalidDataException("Unapproved download redirect.");
            using var request = Request(url);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) throw new InvalidDataException("Missing redirect destination.");
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Dispose();
                throw new HttpRequestException("Installer download failed.");
            }
            return response;
        }
        throw new InvalidDataException("Too many download redirects.");
    }

    internal static bool IsDownloadHost(Uri url) => url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort &&
        string.IsNullOrEmpty(url.UserInfo) && url.Host is "github.com" or "release-assets.githubusercontent.com" or
            "objects.githubusercontent.com" or "github-releases.githubusercontent.com";

    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Localay-Updater/1.0");
        return request;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellation)
    {
        await using var input = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Release metadata is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
