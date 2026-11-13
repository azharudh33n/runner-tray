namespace RunnerTray;

/// <summary>
/// The public RunnerTray GitHub App used for "Sign in with GitHub".
/// The client ID is public by design: the device flow needs no client secret.
/// Permissions (all read-only): Actions, Metadata, Organization self-hosted runners.
/// </summary>
public static class GitHubAppInfo
{
    public const string ClientId = "Iv23liq1RxU4fPn00maC";

    public const string Slug = "runnertray";

    public static string InstallUrl => $"https://github.com/apps/{Slug}/installations/new";

    public static bool Configured => !ClientId.Contains("PLACEHOLDER", StringComparison.Ordinal);
}
