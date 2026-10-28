using FdgRaylib.ListServer;

namespace FdgRaylib.Analytics;

/// <summary>
/// Where usage events go and whether this run may send them at all (#406).
///
/// <para>URL resolution mirrors <see cref="ListServerConfig"/>: the <c>FDG_ANALYTICS_URL</c> environment
/// variable, then an <c>analytics.url</c> file next to the executable, then the list-server URL (the
/// events endpoint lives on the same Worker). Setting either override to a blank value disables
/// analytics without a rebuild.</para>
///
/// <para><c>FDG_ANALYTICS=0</c> disables sending outright; <c>FDG_ANALYTICS=1</c> forces it on for a run
/// that is otherwise excluded (headless, scenario launches, <c>-dev</c> builds) - for testing against
/// <c>wrangler dev</c> or production. Neither overrides the player's own opt-out or the notice gate.</para>
/// </summary>
public static class AnalyticsConfig
{
    public const string UrlEnvVar = "FDG_ANALYTICS_URL";
    public const string SwitchEnvVar = "FDG_ANALYTICS";
    public const string UrlFileName = "analytics.url";

    /// <summary>The events base URL without a trailing slash, or null when disabled/unconfigured.</summary>
    public static string? ResolveBaseUrl()
    {
        string? url = Environment.GetEnvironmentVariable(UrlEnvVar);
        if (url != null) return Normalize(url); // set, even blank: the override wins

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, UrlFileName);
            if (File.Exists(path)) return Normalize(File.ReadAllText(path));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ListServerConfig.BaseUrl;
    }

    /// <summary><c>FDG_ANALYTICS</c>: true = force on in excluded runs, false = off, null = default.</summary>
    public static bool? ResolveSwitch() =>
        Environment.GetEnvironmentVariable(SwitchEnvVar)?.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };

    /// <summary>Why this build never sends by default, or null. Local and IDE builds report
    /// "&lt;version&gt;-dev" (see <see cref="AppVersion"/>), so a developer's own runs stay out of
    /// the numbers.</summary>
    public static string? BuildExclusion(string appVersion) =>
        appVersion.Contains("-dev", StringComparison.Ordinal) ? "dev build" : null;

    internal static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim().TrimEnd('/');
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }
}
