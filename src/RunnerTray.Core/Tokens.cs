namespace RunnerTray;

public enum TokenKind
{
    None,

    /// <summary>"Sign in with GitHub" (device flow), saved in Windows Credential Manager.</summary>
    SignedIn,

    /// <summary>GITHUB_TOKEN or GH_TOKEN environment variable.</summary>
    Environment,

    /// <summary>GitHub CLI (`gh auth token`).</summary>
    GitHubCli,
}

/// <summary>Where API tokens come from.</summary>
public interface ITokenSource
{
    TokenKind Kind { get; }

    /// <summary>A token that is valid now, or null when this source has none.</summary>
    Task<string?> GetTokenAsync(CancellationToken ct);

    /// <summary>Called after GitHub answered 401. Returns true when a new token may work.</summary>
    Task<bool> TryRefreshAsync(CancellationToken ct);
}

/// <summary>A token from GITHUB_TOKEN or GH_TOKEN.</summary>
public sealed class EnvironmentTokenSource : ITokenSource
{
    private readonly Func<string, string?> _getVariable;

    public EnvironmentTokenSource(Func<string, string?>? getVariable = null)
    {
        _getVariable = getVariable ?? System.Environment.GetEnvironmentVariable;
    }

    public TokenKind Kind => TokenKind.Environment;

    public Task<string?> GetTokenAsync(CancellationToken ct)
    {
        var token = _getVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token)) token = _getVariable("GH_TOKEN");
        return Task.FromResult(string.IsNullOrWhiteSpace(token) ? null : token.Trim());
    }

    public Task<bool> TryRefreshAsync(CancellationToken ct) => Task.FromResult(false);
}

/// <summary>The GitHub CLI token. Read again after a 401, because `gh` may have refreshed it.</summary>
public sealed class GitHubCliTokenSource : ITokenSource
{
    private readonly Func<CancellationToken, Task<string?>> _readToken;
    private string? _token;
    private DateTime _readAt;

    public GitHubCliTokenSource(Func<CancellationToken, Task<string?>>? readToken = null)
    {
        _readToken = readToken ?? ReadFromCliAsync;
    }

    public TokenKind Kind => TokenKind.GitHubCli;

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        // Ask `gh` at most every 10 minutes; it is a separate process.
        if (_token is null || DateTime.UtcNow - _readAt > TimeSpan.FromMinutes(10))
        {
            _token = await _readToken(ct);
            _readAt = DateTime.UtcNow;
        }

        return _token;
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var old = _token;
        _token = await _readToken(ct);
        _readAt = DateTime.UtcNow;
        return _token is not null && _token != old;
    }

    private static async Task<string?> ReadFromCliAsync(CancellationToken ct)
    {
        var (code, output) = await Proc.RunAsync("gh", new[] { "auth", "token" }, 10000, ct, includeStdErr: false);
        var token = output.Trim();
        return code == 0 && token.Length > 0 && !token.Contains(' ', StringComparison.Ordinal) ? token : null;
    }
}

/// <summary>
/// Uses the first source that has a token, in this order: signed in, environment, GitHub CLI.
/// Remembers which source is active for diagnostics.
/// </summary>
public sealed class TokenChain : ITokenSource
{
    private readonly IReadOnlyList<ITokenSource> _sources;
    private ITokenSource? _active;

    public TokenChain(params ITokenSource[] sources)
    {
        _sources = sources;
    }

    public TokenKind Kind => _active?.Kind ?? TokenKind.None;

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        foreach (var s in _sources)
        {
            var token = await s.GetTokenAsync(ct);
            if (token is not null)
            {
                if (!ReferenceEquals(_active, s))
                {
                    Log.Info($"Using GitHub token from: {s.Kind}");
                }

                _active = s;
                return token;
            }
        }

        _active = null;
        return null;
    }

    public Task<bool> TryRefreshAsync(CancellationToken ct) =>
        _active is null ? Task.FromResult(false) : _active.TryRefreshAsync(ct);
}
