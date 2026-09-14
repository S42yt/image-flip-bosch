namespace image_flip_bosch.CLI.Config
{
  public sealed class AppConfig
  {
    public string? Theme { get; set; } = "Bosch";

    public CacheConfig Cache { get; } = new();

    public ImgFlipConfig ImgFlip { get; } = new();

    public FeedConfig Feed { get; } = new();

    public string? Proxy { get; set; }

    public void ApplyProxy()
    {
      if (string.IsNullOrWhiteSpace(Proxy)) return;
      string url = Proxy.Contains("://") ? Proxy : "http://" + Proxy;
      HttpClient.DefaultProxy = new System.Net.WebProxy(url) { BypassProxyOnLocal = true };
    }
  }

  public sealed class FeedConfig
  {
    public string BaseUrl { get; set; } = "https://bosch.opengl.tech";
  }

  public sealed class CacheConfig
  {
    public string? Directory { get; set; }

    public int TtlHours { get; set; } = 24;

    public int SweepMinutes { get; set; } = 15;
  }

  public sealed class ImgFlipConfig
  {
    public string? Username { get; set; }

    public string? DefaultFont { get; set; }

    public int? MaxFontSize { get; set; }

    public bool NoWatermark { get; set; }

    public bool IncludeNsfw { get; set; }

    public bool CustomBoxPositions { get; set; }

    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
  }
}
