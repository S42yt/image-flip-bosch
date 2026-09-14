using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace image_flip_bosch.CLI.Utils
{
  public abstract record MemeFeedItem(long Id, string User, string ContentType, DateTime CreatedAt, int Size, int Score, int MyVote)
  {
    public int Score { get; set; } = Score;

    public int MyVote { get; set; } = MyVote;
  }

  public sealed class MemeFeedClient(string baseUrl)
  {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false })
    {
      BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
      Timeout = TimeSpan.FromSeconds(30),
    };

    public string BaseUrl => _http.BaseAddress!.ToString().TrimEnd('/');

    public string ImageUrl(long id) => $"{BaseUrl}/memes/{id}";

    public async Task<List<MemeFeedItem>> ListAsync(IEnumerable<long> exclude, string? user, int limit, CancellationToken ct = default)
    {
      string query = $"memes?limit={limit}&exclude={string.Join(',', exclude)}&user={Uri.EscapeDataString(user ?? "")}";
      return await _http.GetFromJsonAsync<List<MemeFeedItem>>(query, Json, ct) ?? [];
    }

    public async Task<(int Score, int MyVote)> VoteAsync(long id, string user, int value, CancellationToken ct = default)
    {
      using HttpResponseMessage response = await _http.PostAsJsonAsync($"memes/{id}/vote", new { user, value }, Json, ct);
      await EnsureOk(response, ct);
      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      return (doc.RootElement.GetProperty("score").GetInt32(), doc.RootElement.GetProperty("myVote").GetInt32());
    }

    public async Task<(long Id, bool Duplicate)> UploadAsync(string user, byte[] data, string contentType, CancellationToken ct = default)
    {
      using MultipartFormDataContent form = new();
      form.Add(new StringContent(user), "user");
      ByteArrayContent file = new(data);
      file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
      form.Add(file, "file", "meme" + ExtensionFor(contentType));

      using HttpResponseMessage response = await _http.PostAsync("memes", form, ct);
      bool duplicate = response.StatusCode == System.Net.HttpStatusCode.Conflict;
      if (!duplicate) await EnsureOk(response, ct);
      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      return (doc.RootElement.GetProperty("id").GetInt64(), duplicate);
    }

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
      ".png" => "image/png",
      ".jpg" or ".jpeg" => "image/jpeg",
      ".gif" => "image/gif",
      ".webp" => "image/webp",
      ".bmp" => "image/bmp",
      _ => "image/png",
    };

    private static async Task EnsureOk(HttpResponseMessage response, CancellationToken ct)
    {
      if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
      "image/jpeg" => ".jpg",
      "image/gif" => ".gif",
      "image/webp" => ".webp",
      "image/bmp" => ".bmp",
      _ => ".png",
    };
  }
}
