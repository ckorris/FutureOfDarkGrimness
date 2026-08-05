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

    private volatile EUploadState _uploadState = EUploadState.None;
    private volatile string? _lastUploadError;

    public BugReporter(GameLog? log, GameLog? chatLog, Func<string?>? saveGameToJson)
    {
        _log = log;
        _chatLog = chatLog;
        _saveGameToJson = saveGameToJson;
    }

    public EUploadState UploadState => _uploadState;
    public string? LastUploadError => _lastUploadError;

    /// <summary>Path the last report was written to locally, or null if that write failed.</summary>
    public string? LastLocalPath { get; private set; }

    /// <summary>Builds, writes locally, and starts the upload. Ignored while one is in flight.</summary>
    public void Send(string description)
    {
        if (_uploadState == EUploadState.InFlight) return;

        string json = BugReportBundleBuilder.Build(description, _log, _chatLog, _saveGameToJson);
        LastLocalPath = BugReportStore.TryWrite(json, DateTime.UtcNow);

        string? baseUrl = ListServerConfig.BaseUrl;
        if (baseUrl == null)
        {
            _uploadState = EUploadState.NotConfigured;
            return;
        }

        _uploadState = EUploadState.InFlight;
        _ = Task.Run(async () =>
        {
            string? error = await BugReportUploader.TryUploadAsync(json, baseUrl).ConfigureAwait(false);
            _lastUploadError = error;
            _uploadState = error == null ? EUploadState.Succeeded : EUploadState.Failed;
        });
    }
}
