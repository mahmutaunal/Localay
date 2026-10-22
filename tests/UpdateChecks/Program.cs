using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlpWareStudio.Localay;

int checks = 0;
byte[] payload = Encoding.UTF8.GetBytes("MZ-localay-fake-installer-never-executed");
string digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
JsonObject Metadata(string tag = "v1.2.0") => new()
{
    ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false, ["body"] = "Release notes",
    ["assets"] = new JsonArray(new JsonObject
    {
        ["name"] = "Localay-1.2.0-Setup-x64.exe", ["state"] = "uploaded", ["size"] = payload.Length,
        ["digest"] = "sha256:" + digest,
        ["browser_download_url"] = ReleaseUpdateClient.DownloadPrefix + tag + "/Localay-1.2.0-Setup-x64.exe"
    })
};
ReleaseUpdate? Parse(JsonObject data, string installed = "1.0.0.0") =>
    ReleaseUpdateClient.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()), Version.Parse(installed));
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    checks++;
    Console.WriteLine("PASS: " + description);
}
void Reject(JsonObject data, string description)
{
    try { Parse(data); } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
    { Check(true, description); return; }
    throw new Exception("FAILED: accepted " + description);
}
JsonObject Asset(JsonObject data) => data["assets"]![0]!.AsObject();
var release = Parse(Metadata())!;
Check(release.Version == new Version(1, 2, 0) && release.Sha256 == digest, "stable release and GitHub SHA-256 parsed");
Check(Parse(Metadata(), "1.2.0.0") == null, "three-part remote and four-part installed versions compare equally");
Check(Parse(Metadata(), "2.0.0") == null, "older releases never downgrade");
Check(Parse(Metadata("1.2.0")) != null, "optional v prefix accepted");
Check(Parse(Metadata(), "1.10.0") == null, "numeric comparison handles multi-digit version components");
foreach (string flag in new[] { "draft", "prerelease" })
{
    var data = Metadata(); data[flag] = true;
    Check(Parse(data) == null, flag + " ignored");
}
foreach (string tag in new[] { "v1.2.0-beta", "v1.2", "v1.2.0.1", "v01.2.0", "v1.2.0/evil", "v999999999999.2.0" }) Reject(Metadata(tag), "invalid version " + tag);
foreach (string bad in new[] { "", "sha256:1234", "md5:" + digest })
{
    var data = Metadata(); Asset(data)["digest"] = bad; Reject(data, "invalid digest " + bad);
}
{
    var data = Metadata(); Asset(data).Remove("digest"); Reject(data, "missing digest blocks installation");
    data = Metadata(); Asset(data)["name"] = "Localay-1.2.0-Setup-arm64.exe"; Reject(data, "wrong architecture");
    data = Metadata(); data["assets"] = new JsonArray(); Reject(data, "missing installer");
    data = Metadata(); data["assets"]!.AsArray().Add(Asset(data).DeepClone()); Reject(data, "ambiguous installer");
}
foreach (long size in new[] { 0L, -1L, ReleaseUpdateClient.MaximumPackageSize + 1 })
{
    var data = Metadata(); Asset(data)["size"] = size; Reject(data, "invalid package size " + size);
}
foreach (string url in new[] { "http://github.com/file.exe", "https://example.com/file.exe", "https://github.com/other/repo/releases/download/v1.2.0/Localay-1.2.0-Setup-x64.exe" })
{
    var data = Metadata(); Asset(data)["browser_download_url"] = url; Reject(data, "unexpected installer URL");
}
foreach (string host in new[] { "https://example.com/x", "http://github.com/x", "https://github.com.evil.test/x", "https://user@github.com/x", "https://github.com:444/x" })
    Check(!ReleaseUpdateClient.IsDownloadHost(new Uri(host)), "redirect rejected: " + host);
Check(ReleaseUpdateClient.IsDownloadHost(new Uri("https://release-assets.githubusercontent.com/file")), "GitHub asset CDN accepted");

using (var http = new HttpClient(new Handler((request, _) =>
{
    Check(request.Headers.UserAgent.Count > 0 && request.Headers.Authorization == null, "API sets User-Agent without a token");
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}))) Check(await new ReleaseUpdateClient(http).CheckAsync(new Version(1, 0, 0), default) == null, "first release not yet published is harmless");
using (var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
{
    try { await new ReleaseUpdateClient(http).CheckAsync(new Version(1, 0, 0), default); throw new Exception("Expected rate-limit failure"); }
    catch (HttpRequestException) { Check(true, "rate-limited API fails without treating it as latest version"); }
}

string folder = Path.Combine(Path.GetTempPath(), "LocalayUpdateChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    int redirects = 0;
    using (var http = new HttpClient(new Handler((_, _) =>
    {
        if (redirects++ == 0)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/test");
            return Task.FromResult(response);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    })))
    {
        var progress = new InlineProgress();
        string file = await new ReleaseUpdateClient(http).DownloadAsync(release, Path.Combine(folder, "valid"), progress, default);
        Check(File.ReadAllBytes(file).SequenceEqual(payload) && !File.Exists(file + ".part") && progress.Last == 100,
            "redirected complete hash-verified download promotes .part and reports progress");
    }
    foreach (var bad in new[] { payload[..^1], Encoding.UTF8.GetBytes(new string('x', payload.Length)), payload.Concat(new byte[] { 1 }).ToArray() })
    {
        string attempt = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bad) })));
        try { await new ReleaseUpdateClient(http).DownloadAsync(release, attempt, null, default); throw new Exception("Expected bad download rejection"); }
        catch (InvalidDataException) { Check(!Directory.EnumerateFiles(attempt).Any(), "truncated, modified or oversized download leaves no executable or partial file"); }
    }
    using (var http = new HttpClient(new Handler((_, _) =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri("https://example.com/malware.exe"); return Task.FromResult(response);
    })))
    {
        try { await new ReleaseUpdateClient(http).DownloadAsync(release, Path.Combine(folder, "redirect"), null, default); throw new Exception("Expected redirect rejection"); }
        catch (InvalidDataException) { Check(true, "external redirect blocked before requesting its URL"); }
    }
    using (var cancelled = new CancellationTokenSource())
    using (var http = new HttpClient(new Handler(async (_, token) =>
    {
        cancelled.Cancel(); await Task.Delay(1, token); return new HttpResponseMessage(HttpStatusCode.OK);
    })))
    {
        string attempt = Path.Combine(folder, "cancelled");
        try { await new ReleaseUpdateClient(http).DownloadAsync(release, attempt, null, cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(!Directory.EnumerateFiles(attempt).Any(), "cancelled download cannot launch or leave partial files"); }
    }
    Check(UpdateInstaller.RunHelper([]) == null && UpdateInstaller.RunHelper(["--install-update-after"]) == 1,
        "normal startup is unaffected and malformed helper arguments are refused");
    Check(!UpdateInstaller.IsPackagePath(Path.Combine(folder, "Localay-1.2.0-Setup-x64.exe")), "helper rejects packages outside its dedicated directory");
}
finally { Directory.Delete(folder, true); }
Console.WriteLine($"{checks} update checks passed. No network request, Windows installer or application build was performed.");

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
sealed class InlineProgress : IProgress<int>
{
    internal int Last;
    public void Report(int value) => Last = value;
}

namespace AlpWareStudio.Localay
{
    internal static class App { internal static void WriteCrashLog(string context, Exception exception) => throw new Exception("Helper execution is not permitted in portable checks."); }
}
