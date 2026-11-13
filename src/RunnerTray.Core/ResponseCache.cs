using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunnerTray;

/// <summary>A stored GitHub response, reused when GitHub answers 304 Not Modified.</summary>
public sealed record CachedResponse(string ETag, string Body, string? NextPage);

/// <summary>
/// ETag cache for conditional requests. A conditional request that returns 304 does not count
/// against the GitHub rate limit, so most refreshes are free.
/// Kept in memory and, optionally, on disk so the cache survives restarts.
/// </summary>
public sealed class ResponseCache
{
    private const int MaxEntries = 1000;
    private readonly Dictionary<string, (CachedResponse Value, DateTime Used)> _entries = new();
    private readonly object _gate = new();
    private readonly string? _dir;

    /// <param name="dir">Folder for the disk copy, or null for memory only.</param>
    public ResponseCache(string? dir)
    {
        _dir = dir;
    }

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunnerTray", "cache");

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public bool TryGet(string key, out CachedResponse value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                _entries[key] = (e.Value, DateTime.UtcNow);
                value = e.Value;
                return true;
            }
        }

        var loaded = LoadFromDisk(key);
        if (loaded is not null)
        {
            lock (_gate) _entries[key] = (loaded, DateTime.UtcNow);
            value = loaded;
            return true;
        }

        value = null!;
        return false;
    }

    public void Put(string key, CachedResponse value)
    {
        lock (_gate)
        {
            _entries[key] = (value, DateTime.UtcNow);
            if (_entries.Count > MaxEntries)
            {
                foreach (var old in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries).Select(e => e.Key).ToList())
                {
                    _entries.Remove(old);
                }
            }
        }

        SaveToDisk(key, value);
    }

    /// <summary>Forget everything, for example after the user signs in with another account.</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
        if (_dir is not null && Directory.Exists(_dir))
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException ex)
            {
                Log.Warn($"Could not clear the response cache: {ex.Message}");
            }
        }
    }

    private string? FileFor(string key)
    {
        if (_dir is null) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(_dir, hash + ".json");
    }

    private CachedResponse? LoadFromDisk(string key)
    {
        var file = FileFor(key);
        if (file is null || !File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedResponse>(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged cache file is simply ignored; the next request rewrites it.
            return null;
        }
    }

    private void SaveToDisk(string key, CachedResponse value)
    {
        var file = FileFor(key);
        if (file is null) return;
        try
        {
            Directory.CreateDirectory(_dir!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write the response cache: {ex.Message}");
        }
    }
}
