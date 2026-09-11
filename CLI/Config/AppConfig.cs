namespace image_flip_bosch.CLI.Config
{
  public sealed class AppConfig
  {
    public CacheConfig Cache { get; set; } = new();

    public ImgflipConfig Imgflip { get; set; } = new();
  }

  public sealed class CacheConfig
  {
    public string? Directory { get; set; }

    public int TtlHours { get; set; } = 24;

    public int SweepMinutes { get; set; } = 15;
  }

  public sealed class ImgflipConfig
  {
    public string? Username { get; set; }

    public string? DefaultFont { get; set; }

    public int? MaxFontSize { get; set; }

    public bool NoWatermark { get; set; }

    public bool IncludeNsfw { get; set; }

    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
  }
}
