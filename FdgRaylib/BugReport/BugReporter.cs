using FdgRaylib.ListServer;
using FdgRaylib.Rendering;

namespace FdgRaylib.BugReport;

/// <summary>
/// The escape menu's bug-report backend (#226): builds the bundle, always writes the local copy
/// first (local-first - the report survives being offline), then fire-and-forget uploads to the
/// list-server drop box. One instance per launched game, attached in
/// <c>RaylibRenderer.TransitionToGame</c> alongside the save hook.
///
/// <para>Threading: <see cref="Send"/> and the properties are main-thread (escape-menu Draw);
/// only the background upload task writes <see cref="_uploadState"/>/<see cref="LastUploadError"/>,
/// which the menu polls each frame - hence volatile, no lock needed for a single writer.</para>
/// </summary>
public sealed class BugReporter
{
    public enum EUploadState
    {
        /// <summary>No send yet this view.</summary>
        None,
        /// <summary>No list server configured - the local file is the whole report.</summary>
        NotConfigured,
        InFlight,
        Succeeded,
        Failed,
    }

    private readonly GameLog? _log;
    private readonly GameLog? _chatLog;
    private readonly Func<string?>? _saveGameToJson;
    private readonly Action<string>? _announce;
    private readonly Func<string, DateTime, string?> _writeLocal;

    private volatile EUploadState _uploadState = EUploadState.None;
    private volatile string? _lastUploadError;

    /// <param name="announce">Posts a line to every player in the game (the chat relay). A report
    /// carries data belonging to the OTHER players - their chat, and their army lists inside the
    /// host's save - and they never see the disclosure the reporter agreed to, so they are told a
    /// report went out. Null disables the notice (no chat sink wired).</param>
    /// <param name="writeLocal">Writes the bundle and returns the path, or null on failure.
    /// Defaults to <see cref="BugReportStore.TryWrite"/>; a test seam, like the bundle builder's
    /// crash-log path, so the "nothing was retained" branch can be exercised without arranging
    /// a real filesystem failure.</param>
    public BugReporter(GameLog? log, GameLog? chatLog, Func<string?>? saveGameToJson,
        Action<string>? announce = null, Func<string, DateTime, string?>? writeLocal = null)
    {
        _log = log;
        _chatLog = chatLog;
        _saveGameToJson = saveGameToJson;
        _announce = announce;
        _writeLocal = writeLocal ?? BugReportStore.TryWrite;
    }

    /// <summary>
    /// What the other players are told. Mirrors the disclosure's honesty about the save: only a
    /// host's report carries one (a client has nothing to serialize - #054), so only a host's
    /// notice mentions it.
    /// </summary>
    internal static string AnnouncementText(bool isHost) => isHost
        ? "I sent a bug report to the developer. It includes this game's log, our chat, and a full save of the game."
        : "I sent a bug report to the developer. It includes this game's log and our chat.";

    public EUploadState UploadState => _uploadState;
    public string? LastUploadError => _lastUploadError;

    /// <summary>Path the last report was written to locally, or null if that write failed.</summary>
    public string? LastLocalPath { get; private set; }

    /// <summary>Builds, writes locally, and starts the upload. Ignored while one is in flight.</summary>
    public void Send(string description)
    {
        if (_uploadState == EUploadState.InFlight) return;

        string json = BugReportBundleBuilder.Build(description, _log, _chatLog, _saveGameToJson);
        LastLocalPath = _writeLocal(json, DateTime.UtcNow);

        string? baseUrl = ListServerConfig.BaseUrl;
        if (baseUrl == null)
        {
            _uploadState = EUploadState.NotConfigured;
        }
        else
        {
            _uploadState = EUploadState.InFlight;
            _ = Task.Run(async () =>
            {
                string? error = await BugReportUploader.TryUploadAsync(json, baseUrl).ConfigureAwait(false);
                _lastUploadError = error;
                _uploadState = error == null ? EUploadState.Succeeded : EUploadState.Failed;
            });
        }

        // Tell the table AFTER the snapshot is taken, so this notice isn't itself captured in the
        // chat log the report carries. Skipped when nothing was retained (no local file AND no
        // upload attempted) - announcing a report that does not exist anywhere would be a lie.
        // Announced without waiting for the upload result: a failed upload still leaves the local
        // copy, which is meant to be sent on by hand.
        bool retained = LastLocalPath != null || _uploadState == EUploadState.InFlight;
        if (retained) _announce?.Invoke(AnnouncementText(_saveGameToJson != null));
    }
}
