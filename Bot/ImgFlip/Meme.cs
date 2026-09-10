using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace image_flip_bosch.Bot.ImgFlip
{
  public class Meme
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 500;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 500;

    [JsonPropertyName("box_count")]
    public int box_counnt { get; set; } = 2;

    [JsonPropertyName("captions")]
    public int Captions { get; set; } = 0;
  }
}
