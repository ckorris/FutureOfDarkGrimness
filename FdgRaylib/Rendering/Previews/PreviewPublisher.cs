using System.Text.Json;
using FDG;
using FDG.StageResolution.Previews;

namespace FdgRaylib.Rendering.Previews;

/// <summary>
/// Publish side of #280, app half: once per frame the renderer calls <see cref="Tick"/>, which at
/// ~10 Hz asks the active resolver (if it opts into <see cref="IPreviewSource"/>) for its preview,
/// serializes each slot, and sends only the slots whose JSON actually changed - so an idle mouse
/// streams nothing, and the slow-changing "base" slot rides along only on clicks. When the pending
/// request ends (or hands off to a different local player mid-frame, e.g. hotseat), the previous
/// player's previews are cleared. The engine-side feed's resolved-task expiry backstops the clear
/// if this process dies before sending it.
/// </summary>
public class PreviewPublisher
{
    private const double CheckIntervalSeconds = 0.1; // ~10 Hz while state is changing

    private readonly IPreviewChannel _channel;
    private readonly Func<IPreviewSource?> _activeSource;

    private readonly Dictionary<string, string> _lastSentJsonBySlot = new();
    private PlayerID? _publishedPlayer;
    private double _lastCheckTime = double.MinValue;

    /// <param name="activeSource">The resolver currently holding a pending request, when it opts
    /// into previews - normally <c>GuiResolverOverlay.ActivePreviewSource</c>.</param>
    public PreviewPublisher(IPreviewChannel channel, Func<IPreviewSource?> activeSource)
    {
        _channel = channel;
        _activeSource = activeSource;
    }

    /// <summary>Call once per frame from the render loop with a monotonic seconds clock.</summary>
    public void Tick(double nowSeconds)
    {
        if (nowSeconds - _lastCheckTime < CheckIntervalSeconds)
        {
            return;
        }
        _lastCheckTime = nowSeconds;

        PreviewState? state = _activeSource()?.BuildPreviewState();

        if (state == null || state.Slots.Count == 0)
        {
            ClearPublished();
            return;
        }

        if (_publishedPlayer.HasValue && _publishedPlayer.Value != state.SourcePlayerID)
        {
            ClearPublished();
        }

        // Slots send in declaration order, so a base slot always precedes the ghost stream that
        // references it (TCP preserves the order end to end, including through the host relay).
        foreach (PreviewSlotPayload slot in state.Slots)
        {
            string json = JsonSerializer.Serialize(slot.Payload, slot.Payload.GetType());
            if (_lastSentJsonBySlot.TryGetValue(slot.Slot, out string? lastSent) && lastSent == json)
            {
                continue;
            }

            _lastSentJsonBySlot[slot.Slot] = json;
            _channel.PublishUpdate(state.SourcePlayerID, slot.Slot,
                slot.Payload.GetType().FullName!, json);
            _publishedPlayer = state.SourcePlayerID;
        }
    }

    private void ClearPublished()
    {
        if (_publishedPlayer.HasValue == false)
        {
            return;
        }

        _channel.PublishClear(_publishedPlayer.Value);
        _publishedPlayer = null;
        _lastSentJsonBySlot.Clear();
    }
}
