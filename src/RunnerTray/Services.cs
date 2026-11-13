namespace RunnerTray;

/// <summary>Creates and owns the GitHub services: tokens, API client, data source, sign-in.</summary>
internal sealed class Services : IDisposable
{
    private Services(AppConfig config)
    {
        Flow = new DeviceFlowClient(GitHubAppInfo.ClientId);
        SignedIn = new SignedInTokenSource(new CredentialManagerStore(), Flow.RefreshAsync);
        Tokens = new TokenChain(SignedIn, new EnvironmentTokenSource(), new GitHubCliTokenSource());
        Api = new ApiClient(Tokens, new ResponseCache(ResponseCache.DefaultDir));
        Docker = new LocalDockerReader();
        Source = new GitHubSource(config, Api, docker: Docker);
    }

    public DeviceFlowClient Flow { get; }

    public SignedInTokenSource SignedIn { get; }

    public TokenChain Tokens { get; }

    public ApiClient Api { get; }

    public LocalDockerReader Docker { get; }

    public GitHubSource Source { get; }

    public static Services Create(AppConfig config) => new(config);

    public void Dispose()
    {
        Api.Dispose();
        Flow.Dispose();
        SignedIn.Dispose();
    }
}
