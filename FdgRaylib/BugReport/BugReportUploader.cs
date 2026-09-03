using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace FdgRaylib.BugReport;

/// <summary>
/// Uploads a bug-report bundle (#226) to the list-server drop box (<c>POST /reports</c> on the
/// #271 Worker). The body is gzip: real saves compress ~17:1, which is what keeps every
/// legitimate bundle inside the server's 1MB cap. Sent as <c>application/octet-stream</c> rather
/// than a <c>Content-Encoding</c> header so no proxy is tempted to transparently decompress it -
/// the Worker gunzips explicitly.
/// </summary>
public static class BugReportUploader
{
    /// <summary>
    /// Gzips and POSTs the bundle. Returns null on success, or a short human-readable reason on
    /// failure - never throws (the escape menu shows the reason next to the local-copy path).
    /// </summary>
    public static async Task<string?> TryUploadAsync(string bundleJson, string baseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] gzipped = Gzip(bundleJson);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new ByteArrayContent(gzipped);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using HttpResponseMessage response = await http.PostAsync(
                $"{baseUrl.TrimEnd('/')}/reports", content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"server answered {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            // Timeouts, DNS, no network: all the same to the player - the upload didn't happen.
            return ex.GetBaseException().Message;
        }
    }

    internal static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
