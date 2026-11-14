using Xunit;

namespace RunnerTray.Tests;

public class PollPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static RunInfo R(RunState s) => new(1, "r", "w", "main", "push", "a", s, Now, Now, "u");

    private static Snapshot S(FailureKind failure = FailureKind.None, DateTimeOffset? retry = null, params RunInfo[] runs) =>
        new("o", false, runs, Array.Empty<RunnerInfo>(), null, Now) { Failure = failure, RetryAt = retry };

    [Fact]
    public void Active_runs_poll_fast() =>
        Assert.Equal(PollPolicy.Active, PollPolicy.Next(S(runs: R(RunState.Running)), 60, TimeSpan.Zero, 0, null, Now));

    [Fact]
    public void Idle_uses_the_setting() =>
        Assert.Equal(TimeSpan.FromSeconds(90), PollPolicy.Next(S(runs: R(RunState.Success)), 90, TimeSpan.FromMinutes(1), 0, null, Now));

    [Fact]
    public void Long_quiet_slows_down() =>
        Assert.Equal(PollPolicy.Quiet, PollPolicy.Next(S(runs: R(RunState.Success)), 60, TimeSpan.FromHours(1), 0, null, Now));

    [Fact]
    public void Rate_limit_waits_until_retry_time() =>
        Assert.Equal(TimeSpan.FromMinutes(7), PollPolicy.Next(S(FailureKind.RateLimited, Now.AddMinutes(7)), 60, TimeSpan.Zero, 1, null, Now));

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(10, 300)]
    public void Offline_backs_off(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PollPolicy.Next(S(FailureKind.Offline), 60, TimeSpan.Zero, failures, null, Now));

    [Fact]
    public void Server_minimum_is_respected() =>
        Assert.Equal(TimeSpan.FromSeconds(120), PollPolicy.Next(S(runs: R(RunState.Running)), 60, TimeSpan.Zero, 0, TimeSpan.FromSeconds(120), Now));
}

public class RunLogicTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static RunInfo R(long id, string wf, RunState s, int minutes) => new(id, "repo", wf, "main", "push", "a", s, T.AddMinutes(minutes), T, "u");

    [Fact]
    public void A_later_success_clears_an_older_failure()
    {
        var runs = new[] { R(1, "CI", RunState.Failure, 0), R(2, "CI", RunState.Success, 5) };
        Assert.Empty(RunLogic.Failing(runs));
    }

    [Fact]
    public void Latest_failure_counts_and_running_is_ignored()
    {
        var runs = new[] { R(1, "CI", RunState.Success, 0), R(2, "CI", RunState.Failure, 5), R(3, "CI", RunState.Running, 9), R(4, "Lint", RunState.Success, 1) };
        Assert.Equal(2, Assert.Single(RunLogic.Failing(runs)).Id);
        Assert.True(RunLogic.AnyActive(runs));
    }
}

public class LocalDockerTests
{
    [Fact]
    public void Running_job_is_read_from_the_log()
    {
        var job = LocalDockerReader.ParseRunningJob("√ Connected to GitHub\n2026-11-13 16:05:31Z: Listening for Jobs\n2026-11-13 16:06:02Z: Running job: Build and test\r\n");

        Assert.True(job.HasValue);
        var (name, started) = job.GetValueOrDefault();
        Assert.Equal("Build and test", name);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 16, 6, 2, TimeSpan.Zero), started);
    }

    [Fact]
    public void Completed_job_means_idle() =>
        Assert.Null(LocalDockerReader.ParseRunningJob("2026-11-13 16:06:02Z: Running job: Build\n2026-11-13 16:09:00Z: Job Build completed with result: Succeeded\n"));

    [Fact]
    public void Repository_folder_skips_runner_folders() =>
        Assert.Equal("web-app", LocalDockerReader.ParseRepoFolder("_actions\n_temp\nweb-app\n"));

    [Fact]
    public async Task Docker_missing_returns_nothing_and_waits_before_trying_again()
    {
        var calls = 0;
        var now = DateTimeOffset.UtcNow;
        var reader = new LocalDockerReader((args, t, ct) => { calls++; return Task.FromResult((-1, "")); }, () => now);
        var config = new AppConfig { DockerJobLabel = "ci" };

        Assert.Empty(await reader.GetJobsAsync(config, default));
        Assert.Empty(await reader.GetJobsAsync(config, default));
        Assert.False(reader.Available);
        Assert.Equal(1, calls);

        now = now.AddSeconds(31);
        await reader.GetJobsAsync(config, default);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Reads_busy_and_idle_containers()
    {
        var reader = new LocalDockerReader((args, t, ct) => Task.FromResult(args[0] switch
        {
            "ps" => (0, "b\na\n"),
            "logs" when args[3] == "a" => (0, "2026-11-13 16:06:02Z: Running job: Test\n"),
            "logs" => (0, "Listening for Jobs\n"),
            "exec" => (0, "_work\nshop\n"),
            _ => (1, ""),
        }));

        var jobs = await reader.GetJobsAsync(new AppConfig { DockerJobLabel = "ci" }, default);

        Assert.Equal(new[] { "a", "b" }, jobs.Select(j => j.Container));
        Assert.Equal("shop", jobs[0].Repo);
        Assert.True(jobs[0].Busy);
        Assert.False(jobs[1].Busy);
    }

    [Fact]
    public async Task No_label_means_no_docker_calls()
    {
        var calls = 0;
        var reader = new LocalDockerReader((a, t, c) => { calls++; return Task.FromResult((0, "")); });

        Assert.Empty(await reader.GetJobsAsync(new AppConfig(), default));
        Assert.Equal(0, calls);
    }
}

public class ConfigAndStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "runnertray-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigAndStorageTests()
    {
        Directory.CreateDirectory(_dir);
        Log.Enabled = false;
    }

    [Fact]
    public void Missing_config_is_created_with_defaults()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = AppConfig.Load(path);

        Assert.True(File.Exists(path));
        Assert.Null(config.LoadError);
        Assert.Equal(60, config.RefreshSeconds);
    }

    [Fact]
    public void Broken_config_gives_defaults_and_names_the_line()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{\n  \"owner\": \"x\",\n  \"runCount\": oops\n}");

        var config = AppConfig.Load(path);

        Assert.Contains("line 3", config.LoadError, StringComparison.Ordinal);
        Assert.Equal("", config.Owner);
        Assert.Contains("oops", File.ReadAllText(path), StringComparison.Ordinal); // file kept for the user to fix
    }

    [Fact]
    public void Values_are_kept_in_safe_ranges()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{"refreshSeconds": 1, "runCount": 9999, "lookbackDays": 0, "repos": [" a ", "A", ""]}""");

        var config = AppConfig.Load(path);

        Assert.Equal(30, config.RefreshSeconds);
        Assert.Equal(50, config.RunCount);
        Assert.Equal(1, config.LookbackDays);
        Assert.Equal(new[] { "a" }, config.Repos);
    }

    [Fact]
    public void Config_round_trips_without_the_load_error()
    {
        var path = Path.Combine(_dir, "config.json");
        new AppConfig { Owner = "acme", NotifyOnSuccess = true }.Save(path);

        var text = File.ReadAllText(path);
        var config = AppConfig.Load(path);

        Assert.Equal("acme", config.Owner);
        Assert.True(config.NotifyOnSuccess);
        Assert.DoesNotContain("loadError", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Response_cache_survives_a_restart_and_ignores_damaged_files()
    {
        var dir = Path.Combine(_dir, "cache");
        new ResponseCache(dir).Put("k", new CachedResponse("\"e\"", "body", null));

        Assert.True(new ResponseCache(dir).TryGet("k", out var hit));
        Assert.Equal("body", hit.Body);

        foreach (var f in Directory.GetFiles(dir)) File.WriteAllText(f, "garbage");
        Assert.False(new ResponseCache(dir).TryGet("k", out _));
    }

    [Fact]
    public void Last_good_snapshot_is_saved_and_loaded_as_old_data()
    {
        var path = Path.Combine(_dir, "last.json");
        var at = DateTimeOffset.Now.AddMinutes(-3);
        var runs = new[] { new RunInfo(1, "r", "w", "b", "push", "a", RunState.Success, at, at, "u") };
        SnapshotStore.Save(new Snapshot("o", true, runs, Array.Empty<RunnerInfo>(), null, at) { DataAt = at }, path);

        var loaded = SnapshotStore.Load(path);

        Assert.NotNull(loaded);
        Assert.Equal("o", loaded.Owner);
        Assert.Single(loaded.Runs);
        Assert.True(loaded.Stale);
        Assert.Equal(at, loaded.DataAt);
    }

    [Fact]
    public void Stale_snapshots_are_not_saved()
    {
        var path = Path.Combine(_dir, "last.json");
        SnapshotStore.Save(Snapshot.Empty("o") with { Failure = FailureKind.Offline, DataAt = DateTimeOffset.Now }, path);

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz123456 here", "token *** here")]
    [InlineData("ghu_ABCDEFGH1234 and ghr_ABCDEFGH5678", "*** and ***")]
    [InlineData("github_pat_11AAAAAAA_bbbbbbbb", "***")]
    [InlineData("nothing secret", "nothing secret")]
    public void Log_masks_tokens(string input, string expected) => Assert.Equal(expected, Log.Mask(input));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
