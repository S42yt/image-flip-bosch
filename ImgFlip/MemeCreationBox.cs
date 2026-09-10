using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace image_flip_bosch.Bot.ImgFlip
{
  public class MemeCreationBox
  {
    [JsonPropertyName("text")]
    public string Text { get; set; } = "Benni ist der Coolste";

    [JsonPropertyName("x")]
    public int X { get; set; } = 10;

    [JsonPropertyName("y")]
    public int Y { get; set; } = 10;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 548;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 100;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#ffffff";

    [JsonPropertyName("outline_color")]
    public string OutlineColor { get; set; } = "#000000";

  }
}
