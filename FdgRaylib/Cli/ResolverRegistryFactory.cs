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
        // #191 A4-1's request split: unit activation is its own request type; it renders through the
        // same stdin selection flow via the adapter.
        var selectUnit = new SelectionResolver<UnitData>();
        var cancelSelectUnitCli = new CancellableSelectionResolver<UnitData>();
        return new StageResolverRegistry()
            .RegisterResolver(new YesNoResolver())
            .RegisterResolver(new StringSelectionResolver())
            .RegisterResolver(new ChooseAbilityEffectResolver())
            .RegisterResolver(new CastAssistResolver())
            .RegisterResolver(new ChooseDeploymentZoneResolver())
            .RegisterResolver(new ChooseRangedAttackResolver())
            .RegisterResolver(new DefineMovementPathResolver(tableState))
            .RegisterResolver(new AircraftAdvanceResolver())
            .RegisterResolver(new ConsolidationMoveResolver(tableState))
            .RegisterResolver(new AssignWoundsResolver())
            .RegisterResolver(selectUnit)
            .RegisterResolver(new DerivedRequestAdapter<ChooseUnitToActivateRequest,
                SelectionRequest<UnitData>, FDG.Data.DataBinding<UnitData>>(selectUnit))
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>())
            .RegisterResolver(cancelSelectUnitCli)
            // #191 A4-3's split: the melee-defender pick renders through the same stdin flow.
            .RegisterResolver(new DerivedRequestAdapter<ChooseMeleeDefenderRequest,
                CancellableSelectionRequest<UnitData>, CancellableResult<FDG.Data.DataBinding<UnitData>>>(
                cancelSelectUnitCli))
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
        var selectUnit    = new GuiUnitSelectionResolver();   // canvas click-to-select (spell targets, melee defender)
        var selectModel   = new GuiModelSelectionResolver();     // canvas click-to-select + stats (Takedown, single-model spells)
        var selectZone    = new GuiSelectionResolver<RectangularZone>();
        var cancelSelectUnit = new GuiCancellableUnitSelectionResolver();   // canvas click-to-select (#100 pre-attack targeting)
        var strSel        = new GuiStringSelectionResolver();
        var abilityEffect = new GuiChooseAbilityEffectResolver();   // #197 P5a "pick one effect" at activation start
        var castAssist    = new GuiCastAssistResolver();
        var deployZone    = new GuiChooseDeploymentZoneResolver();
        var rangedAttack  = new GuiChooseRangedAttackResolver(tableState);
        var assignWounds  = new GuiAssignWoundsResolver();
        var movement      = new GuiDefineMovementResolver(tableState, formationMode);
        var aircraftMove  = new GuiAircraftAdvanceResolver();
        var consolidate   = new GuiConsolidationMoveResolver(tableState, formationMode);
        var placeObjects  = new GuiPlaceObjectsResolver<ModelData>(tableState, formationMode);
        var placeObjective = new GuiPlaceObjectiveResolver(tableState);
        var placeTerrain   = new GuiPlaceOneTerrainResolver(tableState);
        overlay.Register(yesNo);
        overlay.Register(selectUnit);
        overlay.Register(selectModel);
        overlay.Register(selectZone);
        overlay.Register(cancelSelectUnit);
        overlay.Register(strSel);
        overlay.Register(abilityEffect);
        overlay.Register(castAssist);
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
            // #191 A4-1's request split: activation forwards to the SAME canvas click-to-select
            // resolver instance, so the overlay's pending/draw state works unchanged.
            .RegisterResolver(new DerivedRequestAdapter<ChooseUnitToActivateRequest,
                SelectionRequest<UnitData>, FDG.Data.DataBinding<UnitData>>(selectUnit))
            .RegisterResolver(selectModel)                                   // GUI
            .RegisterResolver(selectZone)                                    // GUI
            .RegisterResolver(cancelSelectUnit)                              // GUI
            // #191 A4-3's split: melee-defender forwards to the SAME canvas resolver instance.
            .RegisterResolver(new DerivedRequestAdapter<ChooseMeleeDefenderRequest,
                CancellableSelectionRequest<UnitData>, CancellableResult<FDG.Data.DataBinding<UnitData>>>(
                cancelSelectUnit))
            .RegisterResolver(strSel)                                        // GUI
            .RegisterResolver(abilityEffect)                                 // GUI
            .RegisterResolver(castAssist)                                    // GUI
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
