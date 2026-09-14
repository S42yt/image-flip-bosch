namespace image_flip_bosch.CLI.Config
{
  public sealed class AppConfig
  {
    public string? Theme { get; set; } = "Bosch";

    public CacheConfig Cache { get; } = new();

    public ImgFlipConfig ImgFlip { get; } = new();

    public FeedConfig Feed { get; } = new();

    public string? Proxy { get; set; }

    private static readonly System.Net.IWebProxy SystemProxy = HttpClient.DefaultProxy;

    private static readonly string[] PrivateHosts =
    [
      @"^https?://(localhost|127\.)",
      @"^https?://10\.",
      @"^https?://192\.168\.",
      @"^https?://172\.(1[6-9]|2\d|3[01])\.",
      @"^https?://\[::1\]",
    ];

    public void ApplyProxy()
    {
      if (string.IsNullOrWhiteSpace(Proxy))
      {
        HttpClient.DefaultProxy = SystemProxy;
        return;
      }
      string url = Proxy.Contains("://") ? Proxy : "http://" + Proxy;
      HttpClient.DefaultProxy = new System.Net.WebProxy(url, BypassOnLocal: true, BypassList: PrivateHosts);
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
