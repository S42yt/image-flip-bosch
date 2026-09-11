using System.Text.Json.Serialization;

namespace image_flip_bosch.ImgFlip
{
  public class MemeCreationBox
  {
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("x")]
    public int? X { get; set; }

    [JsonPropertyName("y")]
    public int? Y { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("outline_color")]
    public string? OutlineColor { get; set; }
  }
}
