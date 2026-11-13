using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RunnerTray;

/// <summary>
/// Reads workflow runs and self-hosted runners from the GitHub REST API.
/// Token: GITHUB_TOKEN or GH_TOKEN, otherwise `gh auth token` (GitHub CLI).
/// </summary>
public sealed class GitHubSource : IDataSource
{
    private static readonly string[] DefaultLabels = { "self-hosted", "linux", "x64", "arm64", "windows", "macos" };

    private readonly AppConfig _config;
    private readonly HttpClient _http;
    private string? _owner;
    private bool _ownerIsOrg;
    private bool _ownerIsMe;
    private List<string> _repos = new();
    private DateTimeOffset _reposAt = DateTimeOffset.MinValue;

    public GitHubSource(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RunnerTray/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<Snapshot> FetchAsync(CancellationToken ct)
    {
        try
        {
            await EnsureAuthAsync(ct);
            await EnsureReposAsync(ct);

            var runTasks = _repos.Select(r => GetRunsAsync(r, ct)).ToList();
            var runnersTask = _config.ShowSelfHostedRunners && _ownerIsOrg
                ? GetRunnersAsync(ct)
                : Task.FromResult<IReadOnlyList<RunnerInfo>>(Array.Empty<RunnerInfo>());

            var runs = (await Task.WhenAll(runTasks))
                .SelectMany(x => x)
                .OrderByDescending(r => r.Created)
                .Take(_config.RunCount)
                .ToList();

            return new Snapshot(_owner!, _ownerIsOrg, runs, await runnersTask, null, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return new Snapshot(_owner ?? _config.Owner, _ownerIsOrg, Array.Empty<RunInfo>(), Array.Empty<RunnerInfo>(),
                ex.Message, DateTimeOffset.Now);
        }
    }

    public Task<IReadOnlyList<LocalJob>> FetchLocalAsync(CancellationToken ct) => LocalDocker.GetJobsAsync(_config, ct);

    private async Task EnsureAuthAsync(CancellationToken ct)
    {
        if (_owner is not null)
        {
            return;
        }

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            var (code, output) = await Proc.RunAsync("gh", new[] { "auth", "token" }, 10000, ct);
            token = code == 0 ? output.Trim() : null;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "No GitHub token. Install the GitHub CLI and run `gh auth login`, or set GITHUB_TOKEN.");
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var me = await GetJsonAsync("user", ct);
        var login = me.RootElement.GetProperty("login").GetString()!;
        var owner = string.IsNullOrWhiteSpace(_config.Owner) ? login : _config.Owner.Trim();

        using var account = await GetJsonAsync($"users/{owner}", ct);
        _ownerIsOrg = account.RootElement.GetProperty("type").GetString() == "Organization";
        _ownerIsMe = string.Equals(owner, login, StringComparison.OrdinalIgnoreCase);
        _owner = owner;
    }

    private async Task EnsureReposAsync(CancellationToken ct)
    {
        if (_config.Repos.Count > 0)
        {
            _repos = _config.Repos;
            return;
        }

        if (DateTimeOffset.Now - _reposAt < TimeSpan.FromMinutes(10))
        {
            return;
        }

        var path = _ownerIsOrg ? $"orgs/{_owner}/repos?sort=pushed&per_page=100"
            : _ownerIsMe ? "user/repos?affiliation=owner&sort=pushed&per_page=100"
            : $"users/{_owner}/repos?sort=pushed&per_page=100";

        var since = DateTimeOffset.UtcNow.AddDays(-_config.LookbackDays);
        using var doc = await GetJsonAsync(path, ct);
        _repos = doc.RootElement.EnumerateArray()
            .Where(r => !r.GetProperty("archived").GetBoolean())
            .Where(r => r.TryGetProperty("pushed_at", out var p) && p.ValueKind == JsonValueKind.String
                        && p.GetDateTimeOffset() >= since)
            .Select(r => r.GetProperty("name").GetString()!)
            .ToList();
        _reposAt = DateTimeOffset.Now;
    }

    private async Task<IReadOnlyList<RunInfo>> GetRunsAsync(string repo, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"repos/{_owner}/{repo}/actions/runs?per_page=5", ct);
            return doc.RootElement.GetProperty("workflow_runs").EnumerateArray().Select(r => new RunInfo(
                r.GetProperty("id").GetInt64(),
                repo,
                r.GetProperty("name").GetString() ?? "",
                r.GetProperty("head_branch").GetString() ?? "",
                r.GetProperty("event").GetString() ?? "",
                r.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Object
                    ? a.GetProperty("login").GetString() ?? "" : "",
                ToState(r.GetProperty("status").GetString(), r.GetProperty("conclusion").GetString()),
                r.GetProperty("created_at").GetDateTimeOffset(),
                r.GetProperty("updated_at").GetDateTimeOffset(),
                r.GetProperty("html_url").GetString() ?? "")).ToList();
        }
        catch (HttpRequestException)
        {
            return Array.Empty<RunInfo>(); // Actions disabled or no access for this repository
        }
    }

    private async Task<IReadOnlyList<RunnerInfo>> GetRunnersAsync(CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"orgs/{_owner}/actions/runners?per_page=100", ct);
            return doc.RootElement.GetProperty("runners").EnumerateArray()
                .Select(r => new RunnerInfo(
                    r.GetProperty("name").GetString() ?? "",
                    r.GetProperty("status").GetString() == "online",
                    r.GetProperty("busy").GetBoolean(),
                    r.GetProperty("labels").EnumerateArray()
                        .Select(l => l.GetProperty("name").GetString() ?? "")
                        .Where(l => !DefaultLabels.Contains(l, StringComparer.OrdinalIgnoreCase))
                        .ToList()))
                .Where(r => _config.RunnerNameFilter.Length == 0
                            || r.Name.Contains(_config.RunnerNameFilter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Busy).ThenByDescending(r => r.Online).ThenBy(r => r.Name)
                .ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return Array.Empty<RunnerInfo>(); // token without admin:org read access
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static RunState ToState(string? status, string? conclusion) => status switch
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
