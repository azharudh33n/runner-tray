namespace RunnerTray;

/// <summary>
/// When to ask GitHub again. Fast while runs are active, slower when idle, much slower when nothing
/// changed for a long time, and never faster than GitHub allows.
/// </summary>
public static class PollPolicy
{
    public static readonly TimeSpan Active = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <param name="snapshot">Result of the last refresh.</param>
    /// <param name="idleSeconds">Interval from the settings while nothing runs.</param>
    /// <param name="sinceLastChange">Time since the runs last changed.</param>
    /// <param name="failuresInRow">Refreshes that failed in a row (offline, server errors).</param>
    /// <param name="serverMinimum">GitHub's x-poll-interval, if any.</param>
    /// <param name="now">Current time.</param>
    public static TimeSpan Next(
        Snapshot snapshot, int idleSeconds, TimeSpan sinceLastChange, int failuresInRow, TimeSpan? serverMinimum, DateTimeOffset now)
    {
        TimeSpan next;
        if (snapshot.Failure == FailureKind.RateLimited && snapshot.RetryAt is { } retry)
        {
            next = retry - now;
        }
        else if (snapshot.Failure == FailureKind.SignInRequired)
        {
            next = Quiet; // nothing to do until the user signs in; sign-in triggers a refresh
        }
        else if (snapshot.Failure is FailureKind.Offline or FailureKind.ServerError or FailureKind.Unexpected)
        {
            // 15 s, 30 s, 60 s, ... up to 5 minutes.
            next = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 15 * Math.Pow(2, Math.Max(0, failuresInRow - 1))));
        }
        else if (RunLogic.AnyActive(snapshot.Runs))
        {
            next = Active;
        }
        else if (sinceLastChange > QuietAfter)
        {
            next = Quiet;
        }
        else
        {
            next = TimeSpan.FromSeconds(Math.Clamp(idleSeconds, 30, 900));
        }

        if (serverMinimum is { } min && next < min)
        {
            next = min;
        }

        return next < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : next;
    }
}
