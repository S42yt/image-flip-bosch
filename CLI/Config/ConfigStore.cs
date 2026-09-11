using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace image_flip_bosch.CLI.Config
{

  public static class ConfigPaths
  {
    private const string AppName = "image_flip_bosch";

    private static string AppDataDirectory =>
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        AppName);

    public static string File(string name) => Path.Combine(AppDataDirectory, name);
  }

  public sealed class ConfigStore<T>(string? path = null)
    where T : class, new()
  {
    private readonly JsonSerializerOptions _options = new()
    {
      WriteIndented = true,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    };

    private readonly Lock _lock = new();
    private T? _cached;

    private string Path { get; } = path ?? ConfigPaths.File(typeof(T).Name.Replace("Config", string.Empty).ToLowerInvariant() + ".json");

    public bool Exists => File.Exists(Path);

    public event EventHandler<T>? Saved;

    public T Load(bool reload = false)
    {
      lock (_lock)
      {
        if (_cached is not null && !reload) return _cached;

        if (!File.Exists(Path))
        {
          _cached = new T();
          return _cached;
        }

        try
        {
          string json = File.ReadAllText(Path, Encoding.UTF8);
          _cached = JsonSerializer.Deserialize<T>(json, _options) ?? new T();
        }
        catch (JsonException)
        {
          BackupCorrupt();
          _cached = new T();
        }

        return _cached;
      }
    }

    private void Save(T config)
    {
      lock (_lock)
      {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string json = JsonSerializer.Serialize(config, _options);
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        File.Move(tmp, Path, overwrite: true);
        _cached = config;
      }
      Saved?.Invoke(this, config);
    }

    public T Update(Action<T> mutate)
    {
      T config = Load();
      mutate(config);
      Save(config);
      return config;
    }

    public T Reset()
    {
      T fresh = new();
      Save(fresh);
      return fresh;
    }

    public bool Delete()
    {
      lock (_lock)
      {
        _cached = null;
        if (!File.Exists(Path)) return false;
        File.Delete(Path);
        return true;
      }
    }

    private void BackupCorrupt()
    {
      try
      {
        File.Move(Path, Path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
      }
      catch (IOException) { }
    }
  }
}
