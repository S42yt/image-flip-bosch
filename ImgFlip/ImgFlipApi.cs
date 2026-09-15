using System.Net.Http.Json;
using System.Text.Json;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Enum;
using image_flip_bosch.ImgFlip.Requests;

namespace image_flip_bosch.ImgFlip
{
  public class ImgFlipApi : IImgFlipApi
  {
    private readonly HttpClient _httpClient;

    public ImgFlipApi(Uri? baseAddress = null)
    {
      _httpClient = new HttpClient
      {
        BaseAddress = baseAddress ?? new Uri("https://api.imgflip.com/"),
        Timeout = TimeSpan.FromSeconds(15),
      };
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static string TypeValue(EMemeTyp type) => type switch
    {
      EMemeTyp.Gif => "gif",
      EMemeTyp.ImageAndGif => "image,gif",
      _ => "image",
    };

    private static string ModelValue(EAiModel model) => model == EAiModel.Classic ? "classic" : "openai";

    private static List<KeyValuePair<string, string>> Auth(string username, string password)
    {
      var parameters = new List<KeyValuePair<string, string>>();
      if (!string.IsNullOrEmpty(username)) parameters.Add(new KeyValuePair<string, string>("username", username));
      if (!string.IsNullOrEmpty(password)) parameters.Add(new KeyValuePair<string, string>("password", password));
      return parameters;
    }

    private static void AddCaptionBoxes(
      List<KeyValuePair<string, string>> parameters,
      MemeCreationBox[]? boxes = null)
    {
      if (boxes is null) return;
      foreach ((int idx, MemeCreationBox? box) in boxes.Index())
      {
        parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][text]", box.Text));
        if (box.X is { } x) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][x]", x.ToString()));
        if (box.Y is { } y) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][y]", y.ToString()));
        if (box.Width is { } w) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][width]", w.ToString()));
        if (box.Height is { } h) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][height]", h.ToString()));
        if (box.Color is { } color) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][color]", color));
        if (box.OutlineColor is { } outline) parameters.Add(new KeyValuePair<string, string>($"boxes[{idx}][outline_color]", outline));
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

    private async Task<ResponseImgFlipData> FetchData(
      string uri,
      List<KeyValuePair<string, string>> parameters)
    {
      using var content = new FormUrlEncodedContent(parameters);

      HttpResponseMessage response = await _httpClient.PostAsync(uri, content);

      response.EnsureSuccessStatusCode();

      return Unwrap(await response.Content.ReadFromJsonAsync<ResponseImgFlip>());
    }

    public async Task<Meme[]> GetMemes(EMemeTyp? type1)
    {
      EMemeTyp? type = null;
      string uri = type is { } typeVal ? $"get_memes?type={Uri.EscapeDataString(TypeValue(typeVal))}" : "get_memes";
      ResponseImgFlipData data = Unwrap(await _httpClient.GetFromJsonAsync<ResponseImgFlip>(uri));
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
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("template_id", templateId));

      if (boxes is { Length: > 0 })
      {
        AddCaptionBoxes(parameters, boxes);
      }
      else
      {
        parameters.Add(new KeyValuePair<string, string>("text0", text0));
        parameters.Add(new KeyValuePair<string, string>("text1", text1));
      }

      if (maxFontSize is { } maxFontSizeVal)
        parameters.Add(new KeyValuePair<string, string>("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is { } noWatermarkVal)
        parameters.Add(new KeyValuePair<string, string>("no_watermark", Flag(noWatermarkVal)));

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
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("template_id", templateId));

      if (maxFontSize is { } maxFontSizeVal)
        parameters.Add(new KeyValuePair<string, string>("max_font_size", maxFontSizeVal.ToString()));
      if (noWatermark is { } noWatermarkVal)
        parameters.Add(new KeyValuePair<string, string>("no_watermark", Flag(noWatermarkVal)));

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
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("query", query));

      if (type is { } typeVal)
        parameters.Add(new KeyValuePair<string, string>("type", TypeValue(typeVal)));
      if (includeNsfw is { } includeNsfwVal)
        parameters.Add(new KeyValuePair<string, string>("include_nsfw", Flag(includeNsfwVal)));

      ResponseImgFlipData data = await FetchData("search_memes", parameters);
      return data.Memes ?? throw new ImgFlipException("search_memes returned no memes");
    }

    public async Task<bool> VerifyCredentials(string username, string password)
    {
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("template_id", "0"));
      parameters.Add(new KeyValuePair<string, string>("text0", "login check"));
      parameters.Add(new KeyValuePair<string, string>("text1", string.Empty));
      try
      {
        await FetchData("caption_image", parameters);
        return true;
      }
      catch (ImgFlipException ex)
      {
        string m = ex.Message;
        bool credentialError = m.Contains("username", StringComparison.OrdinalIgnoreCase)
          || m.Contains("password", StringComparison.OrdinalIgnoreCase)
          || m.Contains("login", StringComparison.OrdinalIgnoreCase);
        return !credentialError;
      }
    }

    public async Task<Meme> GetMeme(
      string username,
      string password,
      string templateId)
    {
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("template_id", templateId));

      ResponseImgFlipData data = await FetchData("get_meme", parameters);
      return data.Meme ?? throw new ImgFlipException("get_meme returned no meme");
    }

    public async Task<string> AutoMeme(
      string username,
      string password,
      string text,
      bool? noWatermark = null)
    {
      List<KeyValuePair<string, string>> parameters = Auth(username, password);
      parameters.Add(new KeyValuePair<string, string>("text", text));

      if (noWatermark is { } noWatermarkVal)
        parameters.Add(new KeyValuePair<string, string>("no_watermark", Flag(noWatermarkVal)));

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
      List<KeyValuePair<string, string>> parameters = Auth(username, password);

      if (model is { } modelVal)
        parameters.Add(new KeyValuePair<string, string>("model", ModelValue(modelVal)));
      if (templateId is { } templateIdVal)
        parameters.Add(new KeyValuePair<string, string>("template_id", templateIdVal.ToString()));
      if (prefixText != null)
        parameters.Add(new KeyValuePair<string, string>("prefix_text", prefixText));
      if (noWatermark is { } noWatermarkVal)
        parameters.Add(new KeyValuePair<string, string>("no_watermark", Flag(noWatermarkVal)));

      ResponseImgFlipData data = await FetchData("ai_meme", parameters);
      return data.Url ?? throw new ImgFlipException("ai_meme returned no url");
    }
  }
}
