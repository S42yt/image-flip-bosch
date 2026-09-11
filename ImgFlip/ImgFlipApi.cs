using System.Text.Json;
using System.Net.Http.Json;

namespace image_flip_bosch.ImgFlip
{
  internal class ImgFlipApi : IImgFlipApi
  {
    readonly HttpClient httpClient = new()
    {
      BaseAddress = new Uri("https://api.imgflip.com/"),
      Timeout = TimeSpan.FromSeconds(15),
    };

    static void AddCaptionBoxes(
      List<KeyValuePair<string, string>> parameters,
      MemeCreationBox[]? boxes = null)
    {
      if (boxes is MemeCreationBox[] boxesArr)
      {
        foreach ((int idx, MemeCreationBox? box) in boxesArr.Index())
        {
          parameters.Add(new($"boxes[{idx}][text]", box.Text));
          parameters.Add(new($"boxes[{idx}][x]", box.X.ToString()));
          parameters.Add(new($"boxes[{idx}][y]", box.Y.ToString()));
          parameters.Add(new($"boxes[{idx}][width]", box.Width.ToString()));
          parameters.Add(new($"boxes[{idx}][height]", box.Height.ToString()));
          parameters.Add(new($"boxes[{idx}][color]", box.Color));
          parameters.Add(new($"boxes[{idx}][outline_color]", box.OutlineColor));
        }
      }
    }

    async Task<ResponseImgFlipData> FetchData(
      string uri,
      List<KeyValuePair<string, string>> parameters)
    {
      using var content = new FormUrlEncodedContent(parameters);

      HttpResponseMessage response = await httpClient.PostAsync(uri, content);

      response.EnsureSuccessStatusCode();

      ResponseImgFlip res = await response.Content.ReadFromJsonAsync<ResponseImgFlip>() ??
        throw new JsonException("Failed to deserialize JSON");
      return res.ResponseImgFlipData ??
        throw new JsonException("Invalid JSON");
    }

    public async Task<Meme[]> GetMemes()
    {
      ResponseImgFlip res = await httpClient.GetFromJsonAsync<ResponseImgFlip>("get_memes") ??
        throw new JsonException("Failed to deserialize JSON");
      ResponseImgFlipData data = res.ResponseImgFlipData ??
        throw new JsonException("Invalid JSON data");
      return data.Memes ??
        throw new JsonException("Invalid JSON data");
    }

    public async Task<string> CaptionImage(
      string templateId,
      string username,
      string password,
      string text0,
      string text1,
      int? maxFontSize = null,
      bool? noWatermark = null,
      MemeCreationBox[]? boxes = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("template_id", templateId),
        new("username", username),
        new("password", password),
        new("text0", text0),
        new("text1", text1)
      };

      if (maxFontSize is int maxFontSizeVal)
        parameters.Add(new("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", noWatermarkVal.ToString()));

      AddCaptionBoxes(parameters, boxes);

      ResponseImgFlipData data = await FetchData("caption_image", parameters);
      return data.Url ?? throw new JsonException("Invalid JSON");
    }

    public async Task<string> CaptionGif(
      string templateId,
      string username,
      string password,
      int? maxFontSize = null,
      bool? noWatermark = null,
      MemeCreationBox[]? boxes = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("template_id", templateId),
        new("username", username),
        new("password", password),
      };

      if (maxFontSize is int maxFontSizeVal)
        parameters.Add(new("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", noWatermarkVal.ToString()));

      AddCaptionBoxes(parameters, boxes);

      ResponseImgFlipData data = await FetchData("caption_gif", parameters);
      return data.Url ?? throw new JsonException("Invalid JSON");
    }

    public async Task<Meme[]> SearchMemes(
      string username,
      string password,
      string query,
      EMemeTyp? type = null,
      bool? includeNsfw = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("username", username),
        new("password", password),
        new("query", query),
      };

      if (type is EMemeTyp typeVal)
        parameters.Add(new("type", typeVal.ToString()));
      if (includeNsfw is bool includeNsfwVal)
        parameters.Add(new("include_nsfw", includeNsfwVal.ToString()));

      ResponseImgFlipData data = await FetchData("search_memes", parameters);
      return data.Memes ?? throw new JsonException("Invalid JSON");
    }

    public async Task<Meme> GetMeme(
      string username,
      string password,
      string templateId)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("username", username),
        new("password", password),
        new("template_id", templateId),
      };

      ResponseImgFlipData data = await FetchData("get_meme", parameters);
      return data.Meme ?? throw new JsonException("Invalid JSON");
    }

    public async Task<string> AutoMeme(
      string username,
      string password,
      string text,
      bool? noWatermark = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("username", username),
        new("password", password),
        new("text", text),
      };

      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", noWatermarkVal.ToString()));

      ResponseImgFlipData data = await FetchData("automeme", parameters);
      return data.Url ?? throw new JsonException("Invalid JSON");
    }

    public async Task<string> AiMeme(
      string username,
      string password,
      EAiModel? model = null,
      int? templateId = null,
      string? prefixText = null,
      bool? noWatermark = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("username", username),
        new("password", password),
      };

      if (model is EAiModel modelVal)
        parameters.Add(new("model", modelVal.ToString()));
      if (templateId is int templateIdVal)
        parameters.Add(new("template_id", templateIdVal.ToString()));
      if (prefixText is string prefixTextVal)
        parameters.Add(new("prefix_text", prefixTextVal));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", noWatermarkVal.ToString()));

      ResponseImgFlipData data = await FetchData("ai_meme", parameters);
      return data.Url ?? throw new JsonException("Invalid JSON");
    }
  }
}
