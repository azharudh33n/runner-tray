namespace RunnerTray;

/// <summary>Why a GitHub request failed. Each kind has its own message and recovery in the UI.</summary>
public enum FailureKind
{
    None,
    Offline,
    SignInRequired,
    RateLimited,
    NotFound,
    Forbidden,
    ServerError,
    Unexpected,
}

public sealed class GitHubException : Exception
{
    public GitHubException(FailureKind kind, string message, DateTimeOffset? retryAt = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        RetryAt = retryAt;
    }

    public FailureKind Kind { get; }

    /// <summary>When it makes sense to try again (rate limits).</summary>
    public DateTimeOffset? RetryAt { get; }
}
