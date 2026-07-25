using System.Text.Json;
using FDG;
using FDG.StageResolution.Previews;
using Raylib_cs;

namespace FdgRaylib.Rendering.Previews;

/// <summary>
/// World-space draw context handed to preview presenters each frame: the same table layout the
/// resolver overlays use, plus lookups a presenter needs to turn payload references into pixels.
/// </summary>
public readonly struct PreviewDrawContext
{
    public readonly float Scale;
    public readonly int OriginX;
    public readonly int OriginY;
    public readonly float TableH;
    public readonly ITableState TableState;
    public readonly Func<PlayerID, Color> ColorForPlayer;

    public PreviewDrawContext(float scale, int originX, int originY, float tableH,
        ITableState tableState, Func<PlayerID, Color> colorForPlayer)
    {
        Scale = scale;
        OriginX = originX;
        OriginY = originY;
        TableH = tableH;
        TableState = tableState;
        ColorForPlayer = colorForPlayer;
    }

    public (float px, float py) InchesToPixel(float x, float z)
        => (OriginX + x * Scale, OriginY + (TableH - z) * Scale);
}

/// <summary>
/// Draws one family of remote preview payloads. A presenter receives ALL of a player's decoded
/// slots and picks the ones it understands, so multi-slot families (base + ghost) compose without
/// the overlay knowing their semantics.
/// </summary>
public interface IRemotePreviewPresenter
{
    void Draw(PlayerID sourcePlayer, IReadOnlyDictionary<string, object> slots, in PreviewDrawContext ctx);
}

/// <summary>
/// Receive side of #277, app half: reads the engine <see cref="IPreviewFeed"/> (other players'
/// latest preview state), decodes payloads through a registered-type allowlist, and hands them to
/// presenters each frame. Decoding is cached and gated on the feed's version counter, so a frame
/// with no network traffic costs one int comparison; unknown payload type names (version skew,
/// future payloads) and malformed JSON are skipped silently - previews are cosmetic.
/// Deserialization never reads a type name from the wire into a type lookup beyond this class's
/// own registry, and payload types are plain records - no polymorphic surface (#186 discipline).
/// </summary>
public class RemotePreviewOverlay
{
    private readonly IPreviewFeed _feed;
    private readonly ITableState _tableState;

    private readonly Dictionary<string, Type> _payloadTypes = new();
    private readonly List<IRemotePreviewPresenter> _presenters = new();

    // Decode cache: reused as long as an entry's JSON string is unchanged (the feed may bump its
    // version for one slot while others are untouched).
    private readonly Dictionary<(PlayerID Player, string Slot), (string Json, object Payload)> _decoded = new();
    private readonly List<(PlayerID Player, Dictionary<string, object> Slots)> _current = new();
    private int _lastFeedVersion = -1;

    // Layout - main-thread only, pushed by the renderer each frame like IGuiCanvasOverlay.
    private float _scale = 10f;
    private int _originX, _originY;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

    private Func<PlayerID, Color>? _colorForPlayer;

    public RemotePreviewOverlay(IPreviewFeed feed, ITableState tableState)
    {
        _feed = feed;
        _tableState = tableState;
    }

    /// <summary>Allowlists a payload record type for decoding, keyed by its FullName.</summary>
    public void RegisterPayload<T>() => _payloadTypes[typeof(T).FullName!] = typeof(T);

    public void Register(IRemotePreviewPresenter presenter) => _presenters.Add(presenter);

    /// <summary>Player palette lookup, available at TransitionToGame (not construction time).</summary>
    public void AttachColors(Func<PlayerID, Color> colorForPlayer) => _colorForPlayer = colorForPlayer;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    public void Draw()
    {
        if (_colorForPlayer == null)
        {
            return;
        }

        RefreshDecoded();

        if (_current.Count == 0)
        {
            return;
        }

        var ctx = new PreviewDrawContext(_scale, _originX, _originY, _tableH, _tableState, _colorForPlayer);
        foreach ((PlayerID player, Dictionary<string, object> slots) in _current)
        {
            foreach (IRemotePreviewPresenter presenter in _presenters)
            {
                presenter.Draw(player, slots, in ctx);
            }
        }
    }

    private void RefreshDecoded()
    {
        int feedVersion = _feed.Version;
        if (feedVersion == _lastFeedVersion)
        {
            return;
        }
        _lastFeedVersion = feedVersion;

        IReadOnlyList<PreviewEntry> snapshot = _feed.GetSnapshot();

        var seen = new HashSet<(PlayerID, string)>();
        _current.Clear();
        var byPlayer = new Dictionary<PlayerID, Dictionary<string, object>>();

        foreach (PreviewEntry entry in snapshot)
        {
            if (_payloadTypes.TryGetValue(entry.PreviewTypeName, out Type? payloadType) == false)
            {
                continue; // Unknown payload (skew / not yet registered) - ignore, don't fail.
            }

            (PlayerID, string) key = (entry.SourcePlayerID, entry.Slot);
            seen.Add(key);

            if (_decoded.TryGetValue(key, out (string Json, object Payload) cached) == false
                || cached.Json != entry.PreviewJson)
            {
                object? payload;
                try
                {
                    payload = JsonSerializer.Deserialize(entry.PreviewJson, payloadType);
                }
                catch (JsonException)
                {
                    continue; // Malformed payload - cosmetic channel, skip.
                }
                if (payload == null)
                {
                    continue;
                }
                cached = (entry.PreviewJson, payload);
                _decoded[key] = cached;
            }

            if (byPlayer.TryGetValue(entry.SourcePlayerID, out Dictionary<string, object>? slots) == false)
            {
                slots = new Dictionary<string, object>();
                byPlayer[entry.SourcePlayerID] = slots;
            }
            slots[entry.Slot] = cached.Payload;
        }

        // Drop cache entries whose feed entry disappeared (clear / expiry).
        foreach ((PlayerID, string) key in _decoded.Keys.Where(k => seen.Contains(k) == false).ToList())
        {
            _decoded.Remove(key);
        }

        foreach (KeyValuePair<PlayerID, Dictionary<string, object>> kvp in byPlayer)
        {
            _current.Add((kvp.Key, kvp.Value));
        }
    }
}
