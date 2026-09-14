using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;


namespace image_flip_bosch.CLI.Utils.Image
{
  public sealed class ImageCache : IAsyncDisposable
  {
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _sweeper;

    private TimeSpan TimeToLive { get; }
    private TimeSpan SweepInterval { get; }

    public ImageCache(
      string? cacheDirectory = null,
      TimeSpan? timeToLive = null,
      TimeSpan? sweepInterval = null,
      HttpClient? httpClient = null)
    {
      _root = cacheDirectory
        ?? Path.Combine(Path.GetTempPath(), "image_flip_bosch", "cache");
      Directory.CreateDirectory(_root);

      TimeToLive = timeToLive ?? TimeSpan.FromHours(24);
      SweepInterval = sweepInterval ?? TimeSpan.FromMinutes(15);

      _ownsHttp = httpClient is null;
      _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

      Logger.Info($"ImageCache using {_root} (TTL {TimeToLive}).");
      _sweeper = Task.Run(() => SweepLoopAsync(_cts.Token));
    }
    public async Task<string> GetAsync(string url, CancellationToken ct = default)
    {
      string key = KeyFor(url);
      SemaphoreSlim gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

      await gate.WaitAsync(ct);
      try
      {
        string? existing = FindFile(key);
        if (existing is not null && !IsExpired(existing))
        {
          Logger.Debug($"Cache hit: {url}");
          return existing;
        }

        if (existing is not null)
          TryDelete(existing);

        return await DownloadAsync(url, key, ct);
      }
      finally
      {
        gate.Release();
      }
    }
    //public bool Contains(string url) => TryGetPath(url) is not null;

    public string? TryGetPath(string url)
    {
      string? path = FindFile(KeyFor(url));
      return path is not null && !IsExpired(path) ? path : null;
    }
    
    /*public bool Remove(string url)
    {
      string? path = FindFile(KeyFor(url));
      if (path is null) return false;

      bool deleted = TryDelete(path);
      if (deleted) Logger.Info($"Removed from cache: {url}");
      return deleted;
    }
    */

    private void Sweep()
    {
      int removed = Directory.EnumerateFiles(_root).Count(file => IsExpired(file) && TryDelete(file));

      if (removed > 0)
        Logger.Info($"Cache sweep removed {removed} expired file(s).");
    }
    public void Clear()
    {
      foreach (string file in Directory.EnumerateFiles(_root))
        TryDelete(file);
      Logger.Info("Cache cleared.");
    }

    private async Task<string> DownloadAsync(string url, string key, CancellationToken ct)
    {
      Logger.Info($"Downloading {url}");

      using HttpResponseMessage response =
        await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
      response.EnsureSuccessStatusCode();

      string ext = ExtensionFor(url, response.Content.Headers.ContentType?.MediaType);
      string finalPath = Path.Combine(_root, key + ext);
      string tempPath = finalPath + ".part";

      try
      {
        await using (FileStream fs = File.Create(tempPath))
        await using (Stream body = await response.Content.ReadAsStreamAsync(ct))
        {
          await body.CopyToAsync(fs, ct);
        }

        File.Move(tempPath, finalPath, overwrite: true);
        File.SetLastWriteTimeUtc(finalPath, DateTime.UtcNow);
        return finalPath;
      }
      catch (Exception ex)
      {
        TryDelete(tempPath);
        Logger.Error($"Download failed: {url}", ex);
        throw;
      }
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
      using PeriodicTimer timer = new(SweepInterval);
      try
      {
        Sweep();
        while (await timer.WaitForNextTickAsync(ct))
          Sweep();
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Logger.Error("Cache sweeper crashed.", ex);
      }
    }

    private bool IsExpired(string path)
    {
      if (path.EndsWith(".part", StringComparison.Ordinal)) return true;
      DateTime written = File.GetLastWriteTimeUtc(path);
      return DateTime.UtcNow - written > TimeToLive;
    }

    private string? FindFile(string key)
    {
      foreach (string file in Directory.EnumerateFiles(_root, key + ".*"))
      {
        if (!file.EndsWith(".part", StringComparison.Ordinal))
          return file;
      }
      return null;
    }

    private static string KeyFor(string url)
    {
      byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim()));
      return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ExtensionFor(string url, string? mediaType)
    {
      string fromUrl = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
      if (fromUrl is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp")
        return fromUrl;

      return mediaType switch
      {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".img",
      };
    }

    private static bool TryDelete(string path)
    {
      try
      {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
      }
      catch (IOException ex)
      {
        Logger.Warn($"Could not delete {path}: {ex.Message}");
        return false;
      }
    }

    public async ValueTask DisposeAsync()
    {
      await _cts.CancelAsync();
      try { await _sweeper; }
      catch
      { /*ignored*/ }
      _cts.Dispose();
      if (_ownsHttp) _http.Dispose();
      foreach (SemaphoreSlim s in _locks.Values) s.Dispose();
    }
  }
}
