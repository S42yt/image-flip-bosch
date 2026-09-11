using System.Net.Http.Json;
using System.Text.Json;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Enum;
using image_flip_bosch.ImgFlip.Requests;

namespace image_flip_bosch.ImgFlip
{
  internal class ImgFlipApi : IImgFlipApi
  {
    readonly HttpClient httpClient = new()
    {
      BaseAddress = new Uri("https://api.imgflip.com/"),
      Timeout = TimeSpan.FromSeconds(15),
    };

    static string Flag(bool value) => value ? "1" : "0";

    static string TypeValue(EMemeTyp type) => type switch
    {
      EMemeTyp.Gif => "gif",
      EMemeTyp.ImageAndGif => "image,gif",
      _ => "image",
    };

    static string ModelValue(EAiModel model) => model == EAiModel.Classic ? "classic" : "openai";

    static List<KeyValuePair<string, string>> Auth(string username, string password)
    {
      var parameters = new List<KeyValuePair<string, string>>();
      if (!string.IsNullOrEmpty(username)) parameters.Add(new("username", username));
      if (!string.IsNullOrEmpty(password)) parameters.Add(new("password", password));
      return parameters;
    }

    static void AddCaptionBoxes(
      List<KeyValuePair<string, string>> parameters,
      MemeCreationBox[]? boxes = null)
    {
      if (boxes is MemeCreationBox[] boxesArr)
      {
        foreach ((int idx, MemeCreationBox? box) in boxesArr.Index())
        {
          parameters.Add(new($"boxes[{idx}][text]", box.Text));
          if (box.X is int x) parameters.Add(new($"boxes[{idx}][x]", x.ToString()));
          if (box.Y is int y) parameters.Add(new($"boxes[{idx}][y]", y.ToString()));
          if (box.Width is int w) parameters.Add(new($"boxes[{idx}][width]", w.ToString()));
          if (box.Height is int h) parameters.Add(new($"boxes[{idx}][height]", h.ToString()));
          if (box.Color is string color) parameters.Add(new($"boxes[{idx}][color]", color));
          if (box.OutlineColor is string outline) parameters.Add(new($"boxes[{idx}][outline_color]", outline));
        }
      }
    }

    static ResponseImgFlipData Unwrap(ResponseImgFlip? res)
    {
      if (res is null)
        throw new JsonException("Failed to deserialize Imgflip response");
      if (!res.Success)
        throw new ImgFlipException(res.ErrorMessage ?? "Imgflip returned success=false without a message");
      return res.ResponseImgFlipData ??
        throw new ImgFlipException("Imgflip returned success=true without data");
    }

    async Task<ResponseImgFlipData> FetchData(
      string uri,
      List<KeyValuePair<string, string>> parameters)
    {
      using var content = new FormUrlEncodedContent(parameters);

      HttpResponseMessage response = await httpClient.PostAsync(uri, content);

      response.EnsureSuccessStatusCode();

      return Unwrap(await response.Content.ReadFromJsonAsync<ResponseImgFlip>());
    }

    public async Task<Meme[]> GetMemes(EMemeTyp? type1)
    {
      EMemeTyp? type = null;
      string uri = type is { } typeVal ? $"get_memes?type={Uri.EscapeDataString(TypeValue(typeVal))}" : "get_memes";
      ResponseImgFlipData data = Unwrap(await httpClient.GetFromJsonAsync<ResponseImgFlip>(uri));
      return data.Memes ?? throw new ImgFlipException("get_memes returned no memes");
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
      var parameters = Auth(username, password);
      parameters.Add(new("template_id", templateId));

      if (boxes is { Length: > 0 })
      {
        AddCaptionBoxes(parameters, boxes);
      }
      else
      {
        parameters.Add(new("text0", text0));
        parameters.Add(new("text1", text1));
      }

      if (maxFontSize is int maxFontSizeVal)
        parameters.Add(new("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", Flag(noWatermarkVal)));

      ResponseImgFlipData data = await FetchData("caption_image", parameters);
      return data.Url ?? throw new ImgFlipException("caption_image returned no url");
    }

    public async Task<string> CaptionGif(
      string templateId,
      string username,
      string password,
      int? maxFontSize = null,
      bool? noWatermark = null,
      MemeCreationBox[]? boxes = null)
    {
      var parameters = Auth(username, password);
      parameters.Add(new("template_id", templateId));

      if (maxFontSize is int maxFontSizeVal)
        parameters.Add(new("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", Flag(noWatermarkVal)));

      AddCaptionBoxes(parameters, boxes);

      ResponseImgFlipData data = await FetchData("caption_gif", parameters);
      return data.Url ?? throw new ImgFlipException("caption_gif returned no url");
    }

    public async Task<Meme[]> SearchMemes(
      string username,
      string password,
      string query,
      EMemeTyp? type = null,
      bool? includeNsfw = null)
    {
      var parameters = Auth(username, password);
      parameters.Add(new("query", query));

      if (type is EMemeTyp typeVal)
        parameters.Add(new("type", TypeValue(typeVal)));
      if (includeNsfw is bool includeNsfwVal)
        parameters.Add(new("include_nsfw", Flag(includeNsfwVal)));

      ResponseImgFlipData data = await FetchData("search_memes", parameters);
      return data.Memes ?? throw new ImgFlipException("search_memes returned no memes");
    }

    public async Task<Meme> GetMeme(
      string username,
      string password,
      string templateId)
    {
      var parameters = Auth(username, password);
      parameters.Add(new("template_id", templateId));

      ResponseImgFlipData data = await FetchData("get_meme", parameters);
      return data.Meme ?? throw new ImgFlipException("get_meme returned no meme");
    }

    public async Task<string> AutoMeme(
      string username,
      string password,
      string text,
      bool? noWatermark = null)
    {
      var parameters = Auth(username, password);
      parameters.Add(new("text", text));

      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", Flag(noWatermarkVal)));

      ResponseImgFlipData data = await FetchData("automeme", parameters);
      return data.Url ?? throw new ImgFlipException("automeme returned no url");
    }

    public async Task<string> AiMeme(
      string username,
      string password,
      EAiModel? model = null,
      int? templateId = null,
      string? prefixText = null,
      bool? noWatermark = null)
    {
      var parameters = Auth(username, password);

      if (model is EAiModel modelVal)
        parameters.Add(new("model", ModelValue(modelVal)));
      if (templateId is int templateIdVal)
        parameters.Add(new("template_id", templateIdVal.ToString()));
      if (prefixText is string prefixTextVal)
        parameters.Add(new("prefix_text", prefixTextVal));
      if (noWatermark is bool noWatermarkVal)
        parameters.Add(new("no_watermark", Flag(noWatermarkVal)));

      ResponseImgFlipData data = await FetchData("ai_meme", parameters);
      return data.Url ?? throw new ImgFlipException("ai_meme returned no url");
    }
  }
}
