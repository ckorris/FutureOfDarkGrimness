using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using FdgRaylib.Rendering.Resolvers;

namespace FdgRaylib.Cli;

public static class ResolverRegistryFactory
{
    /// <summary>Headless / CLI build — all decisions via stdin.</summary>
    public static IStageResolverRegistry Build(ITableState tableState)
    {
        return new StageResolverRegistry()
            .RegisterResolver(new YesNoResolver())
            .RegisterResolver(new StringSelectionResolver())
            .RegisterResolver(new ChooseDeploymentZoneResolver())
            .RegisterResolver(new ChooseRangedAttackResolver())
            .RegisterResolver(new DefineMovementPathResolver(tableState))
            .RegisterResolver(new AircraftAdvanceResolver())
            .RegisterResolver(new ConsolidationMoveResolver(tableState))
            .RegisterResolver(new AssignWoundsResolver())
            .RegisterResolver(new SelectionResolver<UnitData>())
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>())
            .RegisterResolver(new CancellableSelectionResolver<UnitData>())
            .RegisterResolver(new PlaceObjectsResolver<ModelData>(tableState))
            .RegisterResolver(new PlaceOneTerrainResolver(tableState));
    }

    /// <summary>GUI build — interactive resolvers where implemented, CLI fallback otherwise.</summary>
    public static (IStageResolverRegistry Registry, GuiResolverOverlay Overlay) BuildGui(ITableState tableState)
    {
        var overlay = new GuiResolverOverlay();

        // Shared Group/Single preference: one instance so flipping the mode in deployment carries
        // to movement and vice-versa, remembered for the whole game.
        var formationMode = new FormationModeState();

        var yesNo         = new GuiYesNoResolver();
        var selectUnit    = new GuiSelectionResolver<UnitData>();
        var selectModel   = new GuiSelectionResolver<ModelData>();
        var selectZone    = new GuiSelectionResolver<RectangularZone>();
        var cancelSelectUnit = new GuiCancellableSelectionResolver<UnitData>();
        var strSel        = new GuiStringSelectionResolver();
        var deployZone    = new GuiChooseDeploymentZoneResolver();
        var rangedAttack  = new GuiChooseRangedAttackResolver(tableState);
        var assignWounds  = new GuiAssignWoundsResolver();
        var movement      = new GuiDefineMovementResolver(tableState, formationMode);
        var aircraftMove  = new GuiAircraftAdvanceResolver();
        var consolidate   = new GuiConsolidationMoveResolver(tableState);
        var placeObjects  = new GuiPlaceObjectsResolver<ModelData>(tableState, formationMode);
        var placeObjective = new GuiPlaceObjectiveResolver(tableState);
        var placeTerrain   = new GuiPlaceOneTerrainResolver(tableState);
        overlay.Register(yesNo);
        overlay.Register(selectUnit);
        overlay.Register(selectModel);
        overlay.Register(selectZone);
        overlay.Register(cancelSelectUnit);
        overlay.Register(strSel);
        overlay.Register(deployZone);
        overlay.Register(rangedAttack);
        overlay.Register(assignWounds);
        overlay.Register(movement);
        overlay.Register(aircraftMove);
        overlay.Register(consolidate);
        overlay.Register(placeObjects);
        overlay.Register(placeObjective);
        overlay.Register(placeTerrain);

        var registry = new StageResolverRegistry()
            .RegisterResolver(yesNo)                                         // GUI
            .RegisterResolver(selectUnit)                                    // GUI
            .RegisterResolver(selectModel)                                   // GUI
            .RegisterResolver(selectZone)                                    // GUI
            .RegisterResolver(cancelSelectUnit)                              // GUI
            .RegisterResolver(strSel)                                        // GUI
            .RegisterResolver(deployZone)                                    // GUI
            .RegisterResolver(rangedAttack)                                  // GUI
            .RegisterResolver(assignWounds)                                  // GUI
            .RegisterResolver(movement)                                      // GUI
            .RegisterResolver(aircraftMove)                                  // GUI
            .RegisterResolver(consolidate)                                   // GUI
            .RegisterResolver(placeObjects)                                  // GUI
            .RegisterResolver(placeObjective)                                // GUI
            .RegisterResolver(placeTerrain);                                 // GUI

        return (registry, overlay);
    }
}
