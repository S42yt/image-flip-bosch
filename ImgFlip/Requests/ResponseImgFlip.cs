using System.Text.Json.Serialization;

namespace image_flip_bosch.ImgFlip.Requests
{
  public class ResponseImgFlip
  {
    [JsonPropertyName("success")]
    public bool Success { get; init; } = false;

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; } = null;

    [JsonPropertyName("data")]
    public ResponseImgFlipData? ResponseImgFlipData { get; init; } = null;
  }
  public class ResponseImgFlipData
  {
    [JsonPropertyName("url")]
    public string? Url { get; set; } = null;

    [JsonPropertyName("page_url")]
    public string? PageUrl { get; set; } = null;

    [JsonPropertyName("template_id")]
    public int TemplateId { get; set; } = 0;

    [JsonPropertyName("texts")]
    public string[] Texts { get; set; } = [];

    [JsonPropertyName("meme")]
    public Meme? Meme { get; set; } = null;

    [JsonPropertyName("memes")]
    public Meme[]? Memes { get; set; } = null;
  }
}
