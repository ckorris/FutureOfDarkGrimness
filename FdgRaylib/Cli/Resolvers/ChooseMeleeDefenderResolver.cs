using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class ChooseMeleeDefenderResolver : IStageResolver<ChooseMeleeDefenderRequest, DataBinding<UnitData>>
{
    private readonly SelectionResolver<UnitData> _inner = new();

    public Task<DataBinding<UnitData>> Resolve(ChooseMeleeDefenderRequest request) => _inner.Resolve(request);
}
