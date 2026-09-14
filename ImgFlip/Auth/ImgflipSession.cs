using image_flip_bosch.ImgFlip.Enum;
using image_flip_bosch.ImgFlip.Requests;

namespace image_flip_bosch.ImgFlip.Auth
{
  public sealed class ImgFlipException(string message) : Exception(message);

  public sealed class ImgflipSession(IImgFlipApi api, Func<ImgFlipCredentials?> credentials)
  {
    private static readonly ImgFlipCredentials Anonymous = new(string.Empty, string.Empty);

    private readonly Func<ImgFlipCredentials?> _credentials = () => credentials() ?? Anonymous;

    public ImgflipSession(IImgFlipApi api, ImgFlipCredentials? credentials = null) : this(api, () => credentials) { }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_credentials()?.Username);

    public bool? IsPremium { get; private set; }

    public async Task<bool> CheckPremiumAsync(CancellationToken ct = default)
    {
      if (!IsAuthenticated)
      {
        IsPremium = false;
        return false;
      }

      try
      {
        ImgFlipCredentials c = _credentials()!;
        await api.GetMeme(c.Username, c.Password, "61579").WaitAsync(ct);
        IsPremium = true;
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (ImgFlipException)
      {
        IsPremium = false;
      }
      catch (Exception)
      {
        IsPremium = null;
      }

      return IsPremium == true;
    }

    public void ResetPremium() => IsPremium = null;

    public Task<Meme[]> GetMemes(EMemeTyp? type = null) => api.GetMemes(type);

    public Task<string> CaptionImage(string templateId, string text0, string text1, int? maxFontSize = null, bool? noWatermark = null, MemeCreationBox[]? boxes = null)
    { 
      ImgFlipCredentials c = _credentials()!;
      return api.CaptionImage(templateId, c.Username, c.Password, text0, text1, maxFontSize, noWatermark, boxes);
    }

    public Task<string> CaptionGif(string templateId, MemeCreationBox[] boxes, int? maxFontSize = null, bool? noWatermark = null)
    {
      ImgFlipCredentials c = _credentials()!;
      return api.CaptionGif(templateId, c.Username, c.Password, maxFontSize, noWatermark, boxes);
    }

    public Task<Meme[]> SearchMemes(string query, EMemeTyp? type = null, bool? includeNsfw = null)
    {
      ImgFlipCredentials c = _credentials()!;
      return api.SearchMemes(c.Username, c.Password, query, type, includeNsfw);
    }

    public Task<Meme> GetMeme(string templateId)
    {
      ImgFlipCredentials c = _credentials()!;
      return api.GetMeme(c.Username, c.Password, templateId);
    }

    public Task<string> AutoMeme(string text, bool? noWatermark = null)
    {
      ImgFlipCredentials c = _credentials()!;
      return api.AutoMeme(c.Username, c.Password, text, noWatermark);
    }

    public Task<string> AiMeme(EAiModel? model = null, int? templateId = null, string? prefixText = null, bool? noWatermark = null)
    {
      ImgFlipCredentials c = _credentials()!;
      return api.AiMeme(c.Username, c.Password, model, templateId, prefixText, noWatermark);
    }
  }
}
