using FDG;
using FDG.EngineInterface;
using FDG.Players;
using FdgRaylib.Cli;
using FdgRaylib.Rendering.Presentation;
using FdgRaylib.Rendering.Resolvers;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The GUI-side wiring a local player's game needs at launch: log + chat UIs, the GUI resolver
/// registry + canvas overlay, task display, presentation player, and player palette colors.
/// Extracted from <see cref="LobbyScreen"/>'s launch handler (#167) so the lobby path and the
/// no-lobby <c>--scenario</c> direct launch stay one implementation.
/// </summary>
public static class GameGuiWiring
{
    public delegate void GameLaunchedHandler(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        GameLog? log, GuiResolverOverlay overlay, GuiOutstandingTaskDisplay taskDisplay,
        PresentationPlayer presentationPlayer, Func<string?>? saveGameToJson, GuiPlayerMessageUI playerMessageUI);

    /// <summary>
    /// Builds and assigns the GUI interfaces on <paramref name="game"/> and hands the assembled
    /// pieces to <paramref name="onLaunched"/> (normally <c>RaylibRenderer.TransitionToGame</c>).
    /// </summary>
    /// <param name="colorChoiceForPlayer">A player's lobby colour pick (#221) as an index into
    /// <see cref="PlayerColorOptions.Options"/>, or null for no pick. Null / omitted entirely (the
    /// --scenario direct launch) means every slot takes the palette defaults in order.</param>
    /// <param name="coverProximityExceptions">The launched game's #201 cover setting (lobby toggle,
    /// default on), threaded into the GUI resolvers/overlay so cover previews match the engine.</param>
    /// <param name="tableBackground">The launched game's #265 table surface (lobby setting, synced to
    /// every player and saved with the game), carried on the overlay to the renderer.</param>
    public static void Launch(IFDGGame game, IReadOnlyList<(PlayerID ID, string Name)> players,
        Func<string?>? saveGameToJson, GameLaunchedHandler? onLaunched,
        Func<PlayerID, int?>? colorChoiceForPlayer = null,
        bool coverProximityExceptions = true,
        ETableBackground tableBackground = ETableBackground.Forest)
    {
        // Player -> palette colour (#221: lobby picks win, unpicked slots fill with the free defaults),
        // by both PlayerID (table models) and display name (chat sender lines).
        var chosen = new int?[players.Count];
        for (int i = 0; i < players.Count; i++)
            chosen[i] = colorChoiceForPlayer?.Invoke(players[i].ID);
        int[] colorIdx = PlayerColorOptions.ResolveIndices(chosen);

        var colors = new Dictionary<PlayerID, Color>();
        var nameColors = new Dictionary<string, TextColor>();
        for (int i = 0; i < players.Count; i++)
        {
            Color c = PlayerColorOptions.Options[colorIdx[i]].Color;
            colors[players[i].ID] = c;
            nameColors[players[i].Name] = new TextColor(c.R, c.G, c.B, 255);
        }

        var log   = new GameLog();
        // #168: flush the army-load rule warnings buffered during the host's launch path (they fire
        // before this log exists) and stream any later ones here.
        RuleLoadWarnings.AttachLog(log);
        var logUI = new GuiLogMessageUI(log);
        var (resolvers, overlay) = ResolverRegistryFactory.BuildGui(game.TableState, coverProximityExceptions,
            tableBackground);

        var taskDisplay = new GuiOutstandingTaskDisplay(game.LocalPlayerIDs);
        var presentationPlayer = new PresentationPlayer();
        var playerMessageUI = new GuiPlayerMessageUI(
            name => nameColors.TryGetValue(name, out var tc) ? tc : new TextColor(150, 220, 255, 255));
        game.AssignInterfaces(logUI, playerMessageUI, resolvers,
            presentationSink: presentationPlayer,
            outstandingTaskDisplay: taskDisplay);

        // #280: live decision-preview sharing - publish the local resolver's ghosts/paths while a
        // request is pending, draw every other player's. Both halves ride the overlay to the
        // renderer, like the per-launch settings above.
        overlay.AttachPreviews(game.PreviewChannel, game.PreviewFeed, game.TableState);

        onLaunched?.Invoke(game.TableState, pid => colors.GetValueOrDefault(pid, Color.White), log,
            overlay, taskDisplay, presentationPlayer, saveGameToJson, playerMessageUI);
    }
}
