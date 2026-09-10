using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace image_flip_bosch.Bot.ImgFlip
{
  internal class ImgFlipApi : IImgFlipApi
  {
    private readonly HttpClient _httpClient;

    public ImgFlipApi(HttpClient httpClient)
    {
      _httpClient = httpClient;
    }

    public async Task<ResponseImgFlip> GetMemes()
    {
      return await _httpClient.GetFromJsonAsync<ResponseImgFlip>("get_memes") ??
        throw new JsonException("Failed to deserialize JSON");
    }

    public async Task<ResponseImgFlip> CaptionImage(string templateId, string username, string password, string text0, string text1, int? maxFontSize = null, bool? noWatermark = null, MemeCreationBox[]? boxes = null)
    {
      var parameters = new List<KeyValuePair<string, string>>
      {
        new("template_id", templateId),
        new("username", username),
        new("password", password),
        new("text0", text0),
        new("text1", text1)
      };

      using var content = new FormUrlEncodedContent(parameters);

      var response = await _httpClient.PostAsync("caption_image", content);

      response.EnsureSuccessStatusCode();

      return await response.Content.ReadFromJsonAsync<ResponseImgFlip>() ??
        throw new JsonException("Failed to deserialize JSON");
    }

    public Task<ResponseImgFlip> CaptionGif(string templateId, string username, string password, int maxFontSize, bool noWatermark, MemeCreationBox[] boxes)
    {
      throw new NotImplementedException("todo");
    }
    public Task<ResponseImgFlip> SearchMemes(string username, string password, string query, EMemeTyp type, bool includeNsfw)
    {
      throw new NotImplementedException("todo");
    }
    public Task<ResponseImgFlip> GetMeme(string username, string password, string templateId)
    {
      throw new NotImplementedException("todo");
    }
    public Task<ResponseImgFlip> AutoMeme(string username, string password, string text, bool noWatermark)
    {
      throw new NotImplementedException("todo");
    }
    public Task<ResponseImgFlip> AiMeme(string username, string password, EAiModel model, int templateId, string prefixText, bool noWatermark)
    {
      throw new NotImplementedException("todo");
    }

    Task<Meme[]> IImgFlipApi.GetMemes()
    {
      throw new NotImplementedException();
    }

    public Task<ResponseImgFlip> CaptionImage(string templateId, string username, string password, string text0, string text1, int maxFontSize, bool noWatermark, MemeCreationBox[] boxes)
    {
      throw new NotImplementedException();
    }

    Task<Meme[]> IImgFlipApi.SearchMemes(string username, string password, string query, EMemeTyp type, bool includeNsfw)
    {
      throw new NotImplementedException();
    }

    Task<Meme> IImgFlipApi.GetMeme(string username, string password, string templateId)
    {
      throw new NotImplementedException();
    }
  }
}
