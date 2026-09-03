namespace FdgLab.Export;

/// <summary>
/// One C1 row (docs/tactician-c1-schema.md sec 1): the PositionEncoder vector plus the decision
/// taken at that boundary. Labels (Result/ObjDiffNorm/RoundsPlayed) start unset and are filled in
/// by <see cref="SelfPlayDriver"/> once the game's outcome is known (schema: "labels are joined
/// at game end").
/// </summary>
public sealed class ExportRow
{
    public string GameId = "";
    public int Boundary;
    public int Round;
    public int ActingSlot;
    public float[] Features = Array.Empty<float>();
    public int ChosenUnit = -1;
    public string ChosenAction = "";
    public string ChosenMacro = "";

    public float Result;
    public float ObjDiffNorm;
    public int RoundsPlayed;
}
