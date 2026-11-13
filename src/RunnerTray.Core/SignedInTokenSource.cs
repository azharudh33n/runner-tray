using System.Text.Json;

namespace RunnerTray;

/// <summary>
/// Tokens from "Sign in with GitHub". Refreshes them before they expire (8 hours) and after a 401.
/// Refresh tokens work only once, so refreshing is one at a time and the new pair is saved first.
/// </summary>
public sealed class SignedInTokenSource : ITokenSource, IDisposable
{
    private const string Key = "github";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(10);

    private readonly ISecretStore _store;
    private readonly Func<string, CancellationToken, Task<TokenSet>> _refresh;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SignedInTokenSource(ISecretStore store, Func<string, CancellationToken, Task<TokenSet>> refresh, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _refresh = refresh;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public TokenKind Kind => TokenKind.SignedIn;

    /// <summary>Raised after sign-in, sign-out, or when the sign-in expired and was removed.</summary>
    public event EventHandler? Changed;

    public bool IsSignedIn => Load() is not null;

    public void Save(TokenSet tokens)
    {
        _store.Write(Key, JsonSerializer.Serialize(tokens));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        _store.Delete(Key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        var set = Load();
        if (set is null)
        {
            return null;
        }

        if (set.AccessExpires is { } expires && expires - _now() < RefreshMargin)
        {
            set = await RefreshAsync(force: false, ct);
        }

        return set?.AccessToken;
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct) => await RefreshAsync(force: true, ct) is not null;

    private async Task<TokenSet?> RefreshAsync(bool force, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            var set = Load();
            if (set is null)
            {
                return null;
            }

            if (!force && set.AccessExpires is { } exp && exp - _now() >= RefreshMargin)
            {
                return set;
            }

            if (set.RefreshToken is null || (set.RefreshExpires is { } rexp && rexp <= _now()))
            {
                Log.Info("Sign-in expired and cannot be refreshed; signing out.");
                SignOut();
                return null;
            }

            try
            {
                var fresh = await _refresh(set.RefreshToken, ct);
                Save(fresh);
                Log.Info("Sign-in refreshed.");
                return fresh;
            }
            catch (DeviceFlowException ex) when (ex.Error == DeviceFlowError.BadRefreshToken)
            {
                Log.Warn("Refresh token rejected; signing out.");
                SignOut();
                return null;
            }
            catch (DeviceFlowException ex) when (ex.Error == DeviceFlowError.Network)
            {
                // Offline: keep the old token; the request will report the network problem.
                return set;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private TokenSet? Load()
    {
        var json = _store.Read(Key);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TokenSet>(json);
        }
        catch (JsonException)
        {
            Log.Warn("Saved sign-in is damaged; removing it.");
            _store.Delete(Key);
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();
}
