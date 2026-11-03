using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdgRaylib.ListServer;

// DTOs for the list-server API (#271). Deserialized with System.Text.Json into plain records on
// purpose: the list server is an internet-facing service whose responses must never drive
// polymorphic ($type-style) deserialization — see #186 for why that class of deserializer is a
// code-execution hazard on untrusted input. Keep Newtonsoft and the store's JsonSettings away
// from this path.

/// <summary>One live server as returned by <c>GET /servers</c>.</summary>
public sealed record ServerListing(
    string ServerId,
    string Name,
    string Host,
    int Port,
    int ProtocolVersion,
    string TypeMapHash,
    bool HasPassword,
    int PlayerCount,
    int MaxPlayers,
    string State,
    bool? Reachable,
    int AgeSeconds);

/// <summary>What the host reports about itself on register/heartbeat (<c>POST /servers</c>).</summary>
public sealed record RegistrationPayload(
    string Name,
    int Port,
    int ProtocolVersion,
    string TypeMapHash,
    bool HasPassword,
    int PlayerCount,
    int MaxPlayers,
    string State,
    string? ServerId = null,
    string? Token = null);

/// <summary>The registry's reply to a register/heartbeat.</summary>
public sealed record RegistrationReply(
    string ServerId,
    string Token,
    string PublicIp,
    bool? Reachable,
    int TtlSeconds);

internal sealed record ServerListResponse(List<ServerListing> Servers);

/// <summary>
/// Thin HTTP client for the list server (#271). All calls are failure-tolerant at the signature
/// level (exceptions bubble; callers decide whether a dead registry matters — for the host
/// heartbeat it never does).
/// </summary>
public sealed class ListServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public ListServerClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Registers (no id/token in the payload) or heartbeats (id + token set). Returns null
    /// on a 404 heartbeat, which means the entry expired and the caller should re-register.</summary>
    public async Task<RegistrationReply?> RegisterOrHeartbeatAsync(RegistrationPayload payload,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/servers", payload, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegistrationReply>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Fetches the live listing. The registry already filters expired entries.</summary>
    public async Task<IReadOnlyList<ServerListing>> GetServersAsync(CancellationToken cancellationToken)
    {
        ServerListResponse? parsed = await _http.GetFromJsonAsync<ServerListResponse>(
            $"{_baseUrl}/servers", JsonOptions, cancellationToken).ConfigureAwait(false);
        return parsed?.Servers ?? (IReadOnlyList<ServerListing>)Array.Empty<ServerListing>();
    }

    /// <summary>Polite removal on clean shutdown; TTL expiry covers every other path.</summary>
    public async Task DeleteAsync(string serverId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/servers/{serverId}");
        request.Headers.Add("X-Token", token);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        // Best-effort: a failed delete just means the entry lingers until its 90s TTL.
    }

    public void Dispose() => _http.Dispose();
}
