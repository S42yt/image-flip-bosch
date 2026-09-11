namespace image_flip_bosch.CLI.Config
{
  using System;
  using System.IO;
  using System.Text;
  using System.Text.Json;
  using System.Text.Json.Serialization;

  public static class ConfigPaths
  {
    public const string AppName = "image_flip_bosch";

    public static string AppDataDirectory =>
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        AppName);

    public static string File(string name) => Path.Combine(AppDataDirectory, name);
  }

  public sealed class ConfigStore<T> where T : class, new()
  {
    private static readonly JsonSerializerOptions Options = new()
    {
      WriteIndented = true,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    };

    private readonly object _lock = new();
    private T? _cached;

    public ConfigStore(string? path = null)
    {
      Path = path ?? ConfigPaths.File(typeof(T).Name.Replace("Config", string.Empty).ToLowerInvariant() + ".json");
    }

    public string Path { get; }

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
          _cached = JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
          BackupCorrupt();
          _cached = new T();
        }

        return _cached;
      }
    }

    public void Save(T config)
    {
      lock (_lock)
      {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string json = JsonSerializer.Serialize(config, Options);
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
