using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace image_flip_bosch.Bot.ImgFlip
{
  public class ResponseImgFlip
  {
    [JsonPropertyName("success")]
    public bool Success { get; set; } = false;

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; } = null;

    [JsonPropertyName("data")]
    public ResonseImgFlipData? resonseImgFlipData { get; set; } = null;
    
  }


  public class ResonseImgFlipData
  {
    [JsonPropertyName("url")]
    public string? Url { get; set; } = null;

    [JsonPropertyName("page_url")]
    public string? PageUrl { get; set; } = null;

    [JsonPropertyName("template_id")]
    public int TemplateId { get; set; } = 0;

    [JsonPropertyName("texts")]
    public string[] Texts { get; set; } = Array.Empty<string>();

    [JsonPropertyName("meme")]
    public Meme? Meme { get; set; } = null;

    [JsonPropertyName("memes")]
    public Meme[]? Memes { get; set; } = null;
  }
}
