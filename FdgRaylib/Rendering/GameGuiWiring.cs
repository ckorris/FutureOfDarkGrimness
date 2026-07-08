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
    // Orange / Purple as the two default team colours (was Blue / Red). Purple isn't a Raylib built-in,
    // so it's spelled out; Green/Yellow round out the palette for 3-4 player games.
    public static readonly Color TeamPurple = new(150, 70, 200, 255);
    public static readonly Color[] PlayerPalette =
        { Color.Orange, TeamPurple, Color.Green, Color.Yellow };

    public delegate void GameLaunchedHandler(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        GameLog? log, GuiResolverOverlay overlay, GuiOutstandingTaskDisplay taskDisplay,
        PresentationPlayer presentationPlayer, Func<string?>? saveGameToJson, GuiPlayerMessageUI playerMessageUI);

    /// <summary>
    /// Builds and assigns the GUI interfaces on <paramref name="game"/> and hands the assembled
    /// pieces to <paramref name="onLaunched"/> (normally <c>RaylibRenderer.TransitionToGame</c>).
    /// </summary>
    public static void Launch(IFDGGame game, IReadOnlyList<(PlayerID ID, string Name)> players,
        Func<string?>? saveGameToJson, GameLaunchedHandler? onLaunched)
    {
        // Player -> palette colour, by both PlayerID (table models) and display name (chat sender lines).
        var colors = new Dictionary<PlayerID, Color>();
        var nameColors = new Dictionary<string, TextColor>();
        for (int i = 0; i < players.Count; i++)
        {
            Color c = PlayerPalette[i % PlayerPalette.Length];
            colors[players[i].ID] = c;
            nameColors[players[i].Name] = new TextColor(c.R, c.G, c.B, 255);
        }

        var log   = new GameLog();
        var logUI = new GuiLogMessageUI(log);
        var (resolvers, overlay) = ResolverRegistryFactory.BuildGui(game.TableState);

        var taskDisplay = new GuiOutstandingTaskDisplay();
        var presentationPlayer = new PresentationPlayer();
        var playerMessageUI = new GuiPlayerMessageUI(
            name => nameColors.TryGetValue(name, out var tc) ? tc : new TextColor(150, 220, 255, 255));
        game.AssignInterfaces(logUI, playerMessageUI, resolvers,
            presentationSink: presentationPlayer,
            outstandingTaskDisplay: taskDisplay);

        onLaunched?.Invoke(game.TableState, pid => colors.GetValueOrDefault(pid, Color.White), log,
            overlay, taskDisplay, presentationPlayer, saveGameToJson, playerMessageUI);
    }
}
