using System.Text.Json;

namespace RunnerTray;

/// <summary>
/// Reads workflow runs and self-hosted runners from GitHub through <see cref="ApiClient"/>.
/// Never throws: on any failure it returns the last good data, marked stale, with the reason.
/// </summary>
public sealed class GitHubSource : IDataSource
{
    /// <summary>Most repositories checked per refresh (the ones with the newest pushes).</summary>
    public const int MaxRepos = 40;

    private static readonly string[] DefaultLabels = { "self-hosted", "linux", "x64", "arm64", "windows", "macos" };
    private static readonly TimeSpan RepoListAge = TimeSpan.FromMinutes(10);

    private readonly ApiClient _api;
    private readonly Func<DateTimeOffset> _now;
    private readonly LocalDockerReader _docker;
    private AppConfig _config;

    private string? _login;
    private string? _owner;
    private bool _ownerIsOrg;
    private TokenKind _identityKind;
    private List<string> _repos = new();
    private DateTimeOffset _reposAt = DateTimeOffset.MinValue;
    private string? _installUrl;
    private Snapshot? _lastGood;

    public GitHubSource(AppConfig config, ApiClient api, Func<DateTimeOffset>? now = null, LocalDockerReader? docker = null)
    {
        _config = config;
        _api = api;
        _now = now ?? (() => DateTimeOffset.Now);
        _docker = docker ?? new LocalDockerReader();
    }

    public ApiClient Api => _api;

    /// <summary>Repositories checked in the last refresh, for diagnostics.</summary>
    public IReadOnlyList<string> WatchedRepos => _repos;

    /// <summary>Use new settings from the next refresh on.</summary>
    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        ResetIdentity();
    }

    /// <summary>Forget who is signed in, for example after sign-in or sign-out.</summary>
    public void ResetIdentity()
    {
        _login = null;
        _owner = null;
        _reposAt = DateTimeOffset.MinValue;
        _lastGood = null;
    }

    /// <summary>Seed with data saved from the previous session, shown until the first refresh finishes.</summary>
    public void Seed(Snapshot snapshot) => _lastGood ??= snapshot;

    public async Task<Snapshot> FetchAsync(CancellationToken ct)
    {
        try
        {
            await EnsureIdentityAsync(ct);
            await EnsureReposAsync(ct);

            var notices = new List<string>();
            var runs = new List<RunInfo>();
            var skipped = 0;
            foreach (var repo in _repos)
            {
                try
                {
                    runs.AddRange(await GetRunsAsync(repo, ct));
                }
                catch (GitHubException ex) when (ex.Kind is FailureKind.NotFound or FailureKind.Forbidden)
                {
                    skipped++; // Actions turned off, or no access to this repository.
                }
            }

            if (skipped > 0)
            {
                notices.Add($"{skipped} repositor{(skipped == 1 ? "y" : "ies")} skipped (Actions off or no access).");
            }

            var runners = await GetRunnersAsync(notices, ct);
            if (_installUrl is not null)
            {
                notices.Add($"RunnerTray is not installed on {_owner}. Install it to see its repositories.");
            }

            var top = runs.OrderByDescending(r => r.Created).Take(_config.RunCount).ToList();
            var snapshot = new Snapshot(_owner!, _ownerIsOrg, top, runners, null, _now())
            {
                DataAt = _now(),
                Notices = notices,
                InstallUrl = _installUrl,
                TokenKind = _api.Tokens.Kind,
            };
            _lastGood = snapshot;
            return snapshot;
        }
        catch (GitHubException ex)
        {
            if (ex.Kind != FailureKind.SignInRequired) Log.Warn($"Refresh failed ({ex.Kind}): {ex.Message}");
            return Stale(ex.Kind, ex.Message, ex.RetryAt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected data from GitHub (changed format) must not crash the app.
            Log.Error("Unexpected error while refreshing", ex);
            return Stale(FailureKind.Unexpected, "Something went wrong while reading GitHub. Details are in the log.", null);
        }
    }

    public Task<IReadOnlyList<LocalJob>> FetchLocalAsync(CancellationToken ct) => _docker.GetJobsAsync(_config, ct);

    private Snapshot Stale(FailureKind kind, string message, DateTimeOffset? retryAt)
    {
        var baseline = _lastGood ?? Snapshot.Empty(_owner ?? _config.Owner);
        return baseline with
        {
            Error = message,
            At = _now(),
            Failure = kind,
            RetryAt = retryAt,
            TokenKind = _api.Tokens.Kind,
        };
    }

    private async Task EnsureIdentityAsync(CancellationToken ct)
    {
        // A different token source can mean a different account: start over.
        await _api.Tokens.GetTokenAsync(ct);
        if (_login is not null && _identityKind == _api.Tokens.Kind)
        {
            return;
        }

        using var me = await _api.GetJsonAsync("user", ct);
        _login = me.RootElement.GetProperty("login").GetString()!;
        var owner = string.IsNullOrWhiteSpace(_config.Owner) ? _login : _config.Owner.Trim();

        if (string.Equals(owner, _login, StringComparison.OrdinalIgnoreCase))
        {
            _ownerIsOrg = false;
        }
        else
        {
            using var account = await _api.GetJsonAsync($"users/{Uri.EscapeDataString(owner)}", ct);
            _ownerIsOrg = account.RootElement.GetProperty("type").GetString() == "Organization";
            owner = account.RootElement.GetProperty("login").GetString() ?? owner;
        }

        _owner = owner;
        _identityKind = _api.Tokens.Kind;
        _reposAt = DateTimeOffset.MinValue;
        Log.Info($"Signed in as {_login}, watching {_owner} ({(_ownerIsOrg ? "organization" : "user")}).");
    }

    private async Task EnsureReposAsync(CancellationToken ct)
    {
        _installUrl = null;
        if (_config.Repos.Count > 0)
        {
            _repos = _config.Repos;
            return;
        }

        if (_now() - _reposAt < RepoListAge)
        {
            return;
        }

        var since = _now().AddDays(-_config.LookbackDays);
        var found = _api.Tokens.Kind == TokenKind.SignedIn
            ? await ReposFromInstallationsAsync(ct)
            : await ReposFromListingAsync(since, ct);

        _repos = found
            .Where(r => !r.Archived && r.PushedAt >= since)
            .OrderByDescending(r => r.PushedAt)
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRepos)
            .ToList();
        _reposAt = _now();
    }

    private sealed record RepoEntry(string Name, DateTimeOffset PushedAt, bool Archived);

    private static RepoEntry ToRepo(JsonElement r) => new(
        r.GetProperty("name").GetString()!,
        r.TryGetProperty("pushed_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.MinValue,
        r.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True);

    /// <summary>Signed in with the GitHub App: repositories come from the app's installations.</summary>
    private async Task<List<RepoEntry>> ReposFromInstallationsAsync(CancellationToken ct)
    {
        var result = new List<RepoEntry>();
        var installs = await _api.GetPagesAsync("user/installations?per_page=100", 5, _ => true, ct);
        var ids = new List<long>();
        foreach (var page in installs)
        {
            using (page)
            {
                foreach (var i in page.RootElement.GetProperty("installations").EnumerateArray())
                {
                    if (string.Equals(i.GetProperty("account").GetProperty("login").GetString(), _owner, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(i.GetProperty("id").GetInt64());
                    }
                }
            }
        }

        if (ids.Count == 0)
        {
            _installUrl = GitHubAppInfo.InstallUrl;
            return result;
        }

        foreach (var id in ids)
        {
            var pages = await _api.GetPagesAsync($"user/installations/{id}/repositories?per_page=100", 10, _ => true, ct);
            foreach (var page in pages)
            {
                using (page)
                {
                    result.AddRange(page.RootElement.GetProperty("repositories").EnumerateArray().Select(ToRepo));
                }
            }
        }

        return result;
    }

    /// <summary>Token from the CLI or environment: list repositories sorted by push date, stop at old ones.</summary>
    private async Task<List<RepoEntry>> ReposFromListingAsync(DateTimeOffset since, CancellationToken ct)
    {
        var path = _ownerIsOrg
            ? $"orgs/{Uri.EscapeDataString(_owner!)}/repos?sort=pushed&direction=desc&per_page=100"
            : string.Equals(_owner, _login, StringComparison.OrdinalIgnoreCase)
                ? "user/repos?affiliation=owner&sort=pushed&direction=desc&per_page=100"
                : $"users/{Uri.EscapeDataString(_owner!)}/repos?sort=pushed&direction=desc&per_page=100";

        var result = new List<RepoEntry>();
        var pages = await _api.GetPagesAsync(path, 10, page =>
        {
            var items = page.RootElement.EnumerateArray().Select(ToRepo).ToList();
            return items.Count > 0 && items[^1].PushedAt >= since;
        }, ct);
        foreach (var page in pages)
        {
            using (page)
            {
                result.AddRange(page.RootElement.EnumerateArray().Select(ToRepo));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<RunInfo>> GetRunsAsync(string repo, CancellationToken ct)
    {
        using var doc = await _api.GetJsonAsync(
            $"repos/{Uri.EscapeDataString(_owner!)}/{Uri.EscapeDataString(repo)}/actions/runs?per_page=5", ct);
        return doc.RootElement.GetProperty("workflow_runs").EnumerateArray().Select(r => ParseRun(repo, r)).ToList();
    }

    internal static RunInfo ParseRun(string repo, JsonElement r) => new(
        r.GetProperty("id").GetInt64(),
        repo,
        Str(r, "name"),
        Str(r, "head_branch"),
        Str(r, "event"),
        r.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Object ? Str(a, "login") : "",
        ToState(Str(r, "status"), Str(r, "conclusion")),
        r.GetProperty("created_at").GetDateTimeOffset(),
        r.GetProperty("updated_at").GetDateTimeOffset(),
        Str(r, "html_url"))
    {
        Message = RunLogic.Headline(RunMessage(r)),
    };

    /// <summary>
    /// display_title is the pull request title for pull request runs and the commit headline for pushes.
    /// Older responses may lack it; then the head commit message is used.
    /// </summary>
    private static string RunMessage(JsonElement r)
    {
        var title = Str(r, "display_title");
        if (title.Length > 0)
        {
            return title;
        }

        return r.TryGetProperty("head_commit", out var commit) && commit.ValueKind == JsonValueKind.Object ? Str(commit, "message") : "";
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private async Task<IReadOnlyList<RunnerInfo>> GetRunnersAsync(List<string> notices, CancellationToken ct)
    {
        if (!_config.ShowSelfHostedRunners || !_ownerIsOrg)
        {
            return Array.Empty<RunnerInfo>();
        }

        try
        {
            var pages = await _api.GetPagesAsync(
                $"orgs/{Uri.EscapeDataString(_owner!)}/actions/runners?per_page=100", 5, _ => true, ct);
            var runners = new List<RunnerInfo>();
            foreach (var page in pages)
            {
                using (page)
                {
                    runners.AddRange(page.RootElement.GetProperty("runners").EnumerateArray().Select(r => new RunnerInfo(
                        Str(r, "name"),
                        Str(r, "status") == "online",
                        r.TryGetProperty("busy", out var b) && b.ValueKind == JsonValueKind.True,
                        r.GetProperty("labels").EnumerateArray()
                            .Select(l => Str(l, "name"))
                            .Where(l => l.Length > 0 && !DefaultLabels.Contains(l, StringComparer.OrdinalIgnoreCase))
                            .ToList())));
                }
            }

            return runners
                .Where(r => _config.RunnerNameFilter.Length == 0
                            || r.Name.Contains(_config.RunnerNameFilter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Busy).ThenByDescending(r => r.Online).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (GitHubException ex) when (ex.Kind is FailureKind.Forbidden or FailureKind.NotFound)
        {
            notices.Add("The runner list needs organization admin access.");
            return Array.Empty<RunnerInfo>();
        }
    }

    internal static RunState ToState(string? status, string? conclusion) => status switch
    {
        "completed" => conclusion switch
        {
            "success" => RunState.Success,
            "failure" or "timed_out" or "startup_failure" => RunState.Failure,
            "cancelled" => RunState.Cancelled,
            _ => RunState.Other,
        },
        "in_progress" => RunState.Running,
        _ => RunState.Queued,
    };
}
