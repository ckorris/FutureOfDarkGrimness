namespace FdgRaylib.ListServer;

/// <summary>
/// Resolves the base URL of the master list server (#264). Resolution order:
/// <c>FDG_LIST_SERVER_URL</c> environment variable, then a <c>listserver.url</c> text file next to
/// the executable, then the compiled-in default. All empty means the feature is hidden: no Browse
/// tab, no "List publicly" checkbox — the game degrades to direct-IP connect exactly as before.
/// The file/env indirection exists so shipped builds can be repointed (or the feature killed)
/// without a rebuild.
/// </summary>
public static class ListServerConfig
{
    // The deployed list-server Worker (#264), live 2026-07-23. Overridable at runtime via the
    // FDG_LIST_SERVER_URL env var or a listserver.url file; blank it to disable the feature. Redeploy
    // with `npx wrangler deploy` in tools/list-server after changing the Worker.
    private const string DefaultBaseUrl = "https://fdg-list-server.ckorris.workers.dev";

    private static string? _baseUrl;
    private static bool _resolved;

    /// <summary>The list server base URL without a trailing slash, or null when unconfigured.</summary>
    public static string? BaseUrl
    {
        get
        {
            if (!_resolved)
            {
                _baseUrl = Resolve();
                _resolved = true;
            }
            return _baseUrl;
        }
    }

    public static bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);

    private static string? Resolve()
    {
        string? url = Environment.GetEnvironmentVariable("FDG_LIST_SERVER_URL");

        if (string.IsNullOrWhiteSpace(url))
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "listserver.url");
                if (File.Exists(path))
                    url = File.ReadAllText(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (string.IsNullOrWhiteSpace(url))
            url = DefaultBaseUrl;

        url = url.Trim().TrimEnd('/');

        // Only http(s) URLs make sense; anything else is a config mistake — treat as unconfigured
        // rather than letting a malformed value produce confusing HttpClient errors later.
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return url;
    }
}
