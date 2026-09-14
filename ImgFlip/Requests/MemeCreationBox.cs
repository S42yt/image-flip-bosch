using System.Text.Json.Serialization;

namespace image_flip_bosch.ImgFlip.Requests
{
  public class MemeCreationBox
  {
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("x")]
    public int? X { get; init; }

    [JsonPropertyName("y")]
    public int? Y { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [JsonPropertyName("outline_color")]
    public string? OutlineColor { get; init; }
  }
}
