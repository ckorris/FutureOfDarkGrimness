using FDG.Network;
using FDG.Network.Connection.Lobby;

namespace FdgRaylib.ListServer;

/// <summary>
/// Owns the host side of the server browser (#264): a background loop that registers this lobby
/// with the list server and heartbeats every 30 seconds until disposed. Created by
/// <see cref="Rendering.HostModal"/> when "List publicly" is ticked; Program.cs disposes it when
/// the lobby closes, the game ends, or the app exits. Every failure mode is tolerated — a dead or
/// unreachable registry never affects hosting itself, and a crashed host's entry simply expires
/// after the registry's 90s TTL.
/// </summary>
public sealed class PublicListingService : IDisposable
{
    // Mirrors CommandProtocol.TEMP_PORT (engine-internal). One hardcoded listen port is a known
    // limitation tracked as #189; the listing API already carries the port so #189 slots in here
    // without a wire change.
    private const int GamePort = 6389;

    // The lobby has no explicit player cap today; 8 is the color-palette ceiling (#221) and serves
    // as the advertised capacity until a real cap exists.
    private const int AdvertisedMaxPlayers = 8;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly ILobbyViewModel _lobby;
    private readonly bool _hasPassword;
    private readonly ListServerClient _client;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _stateLock = new();

    private string? _serverId;
    private string? _token;
    private volatile bool _inGame;
    private bool _disposed;

    /// <summary>Latest human-readable status ("Listed publicly", "Port 6389 unreachable - ...",
    /// "List server unreachable"), for surfacing in the lobby UI. ASCII only.</summary>
    public string Status { get; private set; } = "Registering...";

    public PublicListingService(ILobbyViewModel lobby, string listServerBaseUrl, bool hasPassword)
    {
        _lobby = lobby;
        _hasPassword = hasPassword;
        _client = new ListServerClient(listServerBaseUrl);

        // Flip the advertised state when the game launches; the browse tab greys in-game entries
        // (post-launch joins are already refused host-side, QF6).
        _lobby.OnLaunched += HandleLaunched;

        _ = Task.Run(() => RunAsync(_cancel.Token));
    }

    private void HandleLaunched(FDG.EngineInterface.IFDGGame game) => _inGame = true;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var payload = BuildPayload();
                RegistrationReply? reply = await _client.RegisterOrHeartbeatAsync(payload, cancellationToken)
                    .ConfigureAwait(false);

                if (reply == null)
                {
                    // 404: our entry expired (long pause, registry restart). Drop the identity and
                    // re-register on the next tick.
                    lock (_stateLock) { _serverId = null; _token = null; }
                    Status = "Listing expired - re-registering...";
                }
                else
                {
                    lock (_stateLock) { _serverId = reply.ServerId; _token = reply.Token; }
                    Status = reply.Reachable == false
                        ? $"Listed, but port {GamePort} looks unreachable from the internet - " +
                          "players may not be able to join. See port forwarding help."
                        : "Listed publicly.";
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Registry down/unreachable/misbehaving: hosting continues unaffected.
                Status = "List server unreachable - hosting continues unlisted.";
            }

            try
            {
                await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private RegistrationPayload BuildPayload()
    {
        string? serverId;
        string? token;
        lock (_stateLock)
        {
            serverId = _serverId;
            token = _token;
        }

        return new RegistrationPayload(
            Name: _lobby.ServerName,
            Port: GamePort,
            ProtocolVersion: NetworkProtocol.Version,
            TypeMapHash: NetworkProtocol.LocalTypeMapHash,
            HasPassword: _hasPassword,
            PlayerCount: _lobby.PlayerInfos.Count,
            MaxPlayers: AdvertisedMaxPlayers,
            State: _inGame ? "in-game" : "lobby",
            ServerId: serverId,
            Token: token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lobby.OnLaunched -= HandleLaunched;
        _cancel.Cancel();

        // Best-effort polite removal so the entry vanishes immediately instead of after the TTL.
        string? serverId;
        string? token;
        lock (_stateLock)
        {
            serverId = _serverId;
            token = _token;
        }
        if (serverId != null && token != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var deleteTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _client.DeleteAsync(serverId, token, deleteTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
                finally
                {
                    _client.Dispose();
                    _cancel.Dispose();
                }
            });
        }
        else
        {
            _client.Dispose();
            _cancel.Dispose();
        }
    }
}
