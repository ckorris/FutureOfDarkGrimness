namespace FdgLab;

/// <summary>
/// Cooperative pause: a bench/soak and a self-play data-gen driver (#191 B+C campaign) must not
/// fight for the same cores. Touch the file to pause every FdgLab process honoring this gate
/// before starting a new game; remove it to resume. Already-running games are never preempted -
/// only new game starts wait.
/// </summary>
public static class PauseGate
{
    public static async Task WaitWhilePausedAsync(string? pauseFilePath, CancellationToken ct = default)
    {
        if (pauseFilePath == null) return;
        while (File.Exists(pauseFilePath))
            await Task.Delay(2000, ct);
    }
}
