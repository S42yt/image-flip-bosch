using image_flip_bosch.ImgFlip.Enum;
using image_flip_bosch.ImgFlip.Requests;

namespace image_flip_bosch.ImgFlip.Auth
{
  public sealed class ImgFlipException : Exception
  {
    public ImgFlipException(string message) : base(message) { }
  }

  internal sealed class ImgflipSession
  {
    private static readonly ImgflipCredentials Anonymous = new(string.Empty, string.Empty);

    private readonly IImgFlipApi _api;
    private readonly Func<ImgflipCredentials?> _credentials;

    public ImgflipSession(IImgFlipApi api, Func<ImgflipCredentials?> credentials)
    {
      _api = api;
      _credentials = () => credentials() ?? Anonymous;
    }

    public ImgflipSession(IImgFlipApi api, ImgflipCredentials? credentials = null) : this(api, () => credentials) { }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_credentials()?.Username);

    public Task<Meme[]> GetMemes() => _api.GetMemes();

    public Task<string> CaptionImage(string templateId, string text0, string text1, int? maxFontSize = null, bool? noWatermark = null, MemeCreationBox[]? boxes = null)
    {
      ImgflipCredentials? c = _credentials();
      return _api.CaptionImage(templateId, c!.Username, c.Password, text0, text1, maxFontSize, noWatermark, boxes);
    }

    public Task<string> CaptionGif(string templateId, MemeCreationBox[] boxes, int? maxFontSize = null, bool? noWatermark = null)
    {
      ImgflipCredentials? c = _credentials();
      return _api.CaptionGif(templateId, c!.Username, c.Password, maxFontSize, noWatermark, boxes);
    }

    public Task<Meme[]> SearchMemes(string query, EMemeTyp? type = null, bool? includeNsfw = null)
    {
      ImgflipCredentials? c = _credentials();
      return _api.SearchMemes(c!.Username, c.Password, query, type, includeNsfw);
    }

    public Task<Meme> GetMeme(string templateId)
    {
      ImgflipCredentials? c = _credentials();
      return _api.GetMeme(c!.Username, c.Password, templateId);
    }

    public Task<string> AutoMeme(string text, bool? noWatermark = null)
    {
      ImgflipCredentials? c = _credentials();
      return _api.AutoMeme(c!.Username, c.Password, text, noWatermark);
    }

    public Task<string> AiMeme(EAiModel? model = null, int? templateId = null, string? prefixText = null, bool? noWatermark = null)
    {
      ImgflipCredentials? c = _credentials();
      return _api.AiMeme(c!.Username, c.Password, model, templateId, prefixText, noWatermark);
    }
  }
}
