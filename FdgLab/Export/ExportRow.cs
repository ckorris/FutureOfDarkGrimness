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

    // v3 (#191 step 11 S1): what the shipping hand evaluator said about this state, for the ACTING
    // side, at this boundary. Not a feature - the offline baseline step 13 must beat before any
    // bench time is spent on a net ("is the net a better predictor of the outcome than the
    // evaluator it would replace, on the same rows").
    public float HandValue;

    public float Result;
    public float ObjDiffNorm;
    public int RoundsPlayed;
}
