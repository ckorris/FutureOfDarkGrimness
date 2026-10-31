using Mono.Nat;

namespace FdgRaylib.ListServer;

/// <summary>
/// Best-effort UPnP / NAT-PMP port forwarding for a host (#271). On <see cref="Start"/> it discovers
/// the local router and asks it to forward the game's listen port to this machine, so a friend on the
/// internet can reach a host that never manually port-forwarded. Created by
/// <see cref="Rendering.HostModal"/> whenever a host is started; Program.cs disposes it (removing the
/// mapping) when the lobby closes, the game ends, or the app exits.
///
/// This is BEST-EFFORT by nature and failure is normal, not exceptional: many routers ship with UPnP
/// disabled, and carrier-grade NAT / double-NAT defeat it entirely. A failure never affects hosting -
/// it just means the host still needs manual port forwarding or Tailscale. The list server's
/// reachability probe (#271) remains the source of truth for whether the port is actually open from
/// the outside; this only improves the odds. All status text is ASCII (CLAUDE.md).
/// </summary>
public sealed class NatPortMapper : IDisposable
{
    public enum MapState { Idle, Trying, Mapped, Failed }

    // Mono.Nat's discovery is best-effort with no "nothing found" event, so we cap the attempt: if no
    // gateway has accepted a mapping by then, report Failed and stop listening.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly object _lock = new();

    private volatile MapState _state = MapState.Idle;
    private INatDevice? _device;
    private Mapping? _mapping;
    private CancellationTokenSource? _cancel;
    private bool _disposed;

    public NatPortMapper(int port, TimeSpan? timeout = null)
    {
        _port = port;
        _timeout = timeout ?? DefaultTimeout;
    }

    public MapState State => _state;

    /// <summary>Latest human-readable status, for surfacing in the lobby UI. ASCII only.</summary>
    public string Status => _state switch
    {
        MapState.Trying => $"UPnP: asking your router to open port {_port}...",
        MapState.Mapped => $"UPnP: port {_port} opened on your router.",
        MapState.Failed => $"UPnP: could not auto-open port {_port} - your router may not support it. " +
                           "Manual port forwarding or Tailscale may still be needed.",
        _               => "",
    };

    /// <summary>
    /// Begins discovery and mapping in the background. Returns immediately; the outcome is reflected in
    /// <see cref="State"/> / <see cref="Status"/>. Safe to call once; a second call is a no-op.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _state != MapState.Idle) return;
            _state = MapState.Trying;
            _cancel = new CancellationTokenSource();
            NatUtility.DeviceFound += OnDeviceFound;
            try { NatUtility.StartDiscovery(); }
            catch { _state = MapState.Failed; NatUtility.DeviceFound -= OnDeviceFound; return; }
            _ = TimeoutAsync(_cancel.Token);
        }
    }

    private async Task TimeoutAsync(CancellationToken ct)
    {
        try { await Task.Delay(_timeout, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        lock (_lock)
        {
            if (_state == MapState.Trying)
            {
                _state = MapState.Failed;
                StopDiscovery();
            }
        }
    }

    private async void OnDeviceFound(object? sender, DeviceEventArgs e)
    {
        INatDevice device = e.Device;
        // A description helps a curious host spot the entry in their router's port-forward table.
        var mapping = new Mapping(Protocol.Tcp, _port, _port, 0, "Future of Dark Grimness");
        try
        {
            await device.CreatePortMapAsync(mapping).ConfigureAwait(false);
        }
        catch
        {
            // This device refused or isn't a real gateway. Leave discovery running - another device
            // may answer, otherwise the timeout flips us to Failed.
            return;
        }

        lock (_lock)
        {
            // Lost the race to the timeout / a Dispose, or a second device beat us here: undo and bail.
            if (_disposed || _state != MapState.Trying)
            {
                _ = SafeDeleteAsync(device, mapping);
                return;
            }
            _device = device;
            _mapping = mapping;
            _state = MapState.Mapped;
            StopDiscovery();
        }
    }

    // Caller holds _lock (or is Dispose, which owns exclusive teardown).
    private void StopDiscovery()
    {
        NatUtility.DeviceFound -= OnDeviceFound;
        try { NatUtility.StopDiscovery(); } catch { }
        _cancel?.Cancel();
    }

    private static async Task SafeDeleteAsync(INatDevice device, Mapping mapping)
    {
        try { await device.DeletePortMapAsync(mapping).ConfigureAwait(false); } catch { }
    }

    /// <summary>
    /// Releases the mapping. Returns immediately: the router round-trip that removes it runs in the
    /// background, because this is called from the UI thread when a lobby closes and a router taking
    /// its time froze the app for most of a second before the main menu appeared (#310 follow-up).
    /// Use <see cref="DisposeBlocking"/> on the way out of the process instead, where there is no
    /// later moment for a background task to finish in.
    /// </summary>
    public void Dispose() => Teardown(TimeSpan.Zero);

    /// <summary>
    /// Dispose, waiting up to <paramref name="timeout"/> for the router to confirm the removal. For
    /// app shutdown only - the mapping is created with an indefinite lease, so letting the process
    /// exit mid-removal would leave the port open on the router until it is rebooted.
    /// </summary>
    public void DisposeBlocking(TimeSpan? timeout = null) => Teardown(timeout ?? TimeSpan.FromSeconds(2));

    private void Teardown(TimeSpan wait)
    {
        INatDevice? device;
        Mapping? mapping;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopDiscovery();
            device = _device;
            mapping = _mapping;
            _device = null;
            _mapping = null;
        }

        // Best-effort removal so the mapping vanishes now instead of lingering until the router's lease
        // expires. Nothing here is load-bearing: a removal that never lands just leaves a forwarded port.
        if (device == null || mapping == null) return;

        Task removal = SafeDeleteAsync(device, mapping);
        if (wait > TimeSpan.Zero)
        {
            try { removal.Wait(wait); } catch { }
        }
    }
}
