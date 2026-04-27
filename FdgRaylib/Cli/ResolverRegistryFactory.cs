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
            .RegisterResolver(new AssignWoundsResolver())
            .RegisterResolver(new SelectionResolver<UnitData>())
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>())
            .RegisterResolver(new PlaceObjectsResolver<ModelData>(tableState));
    }

    /// <summary>GUI build — interactive resolvers where implemented, CLI fallback otherwise.</summary>
    public static (IStageResolverRegistry Registry, GuiResolverOverlay Overlay) BuildGui(ITableState tableState)
    {
        var overlay = new GuiResolverOverlay();

        var yesNo         = new GuiYesNoResolver();
        var selectUnit    = new GuiSelectionResolver<UnitData>();
        var selectModel   = new GuiSelectionResolver<ModelData>();
        var selectZone    = new GuiSelectionResolver<RectangularZone>();
        var strSel        = new GuiStringSelectionResolver();
        var deployZone    = new GuiChooseDeploymentZoneResolver();
        overlay.Register(yesNo);
        overlay.Register(selectUnit);
        overlay.Register(selectModel);
        overlay.Register(selectZone);
        overlay.Register(strSel);
        overlay.Register(deployZone);

        var registry = new StageResolverRegistry()
            .RegisterResolver(yesNo)                                         // GUI
            .RegisterResolver(selectUnit)                                    // GUI
            .RegisterResolver(selectModel)                                   // GUI
            .RegisterResolver(selectZone)                                    // GUI
            .RegisterResolver(strSel)                                        // GUI
            .RegisterResolver(deployZone)                                    // GUI
            .RegisterResolver(new ChooseRangedAttackResolver())              // CLI fallback
            .RegisterResolver(new DefineMovementPathResolver(tableState))    // CLI fallback
            .RegisterResolver(new AssignWoundsResolver())                    // CLI fallback
            .RegisterResolver(new PlaceObjectsResolver<ModelData>(tableState)); // CLI fallback

        return (registry, overlay);
    }
}
