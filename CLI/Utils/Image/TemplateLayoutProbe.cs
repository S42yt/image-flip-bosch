using image_flip_bosch.CLI.Config;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;
using ISImage = SixLabors.ImageSharp.Image;

namespace image_flip_bosch.CLI.Utils.Image
{
  public sealed record TemplateBox(int X, int Y, int Width, int Height);

  public static class TemplateLayoutProbe
  {
    private const string FillText = "XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX";
    private const int ChangeThreshold = 60;
    private const int ColorThreshold = 110;
    private const int Padding = 6;

    private static readonly (string Hex, Rgba32 Rgb)[] ProbeColors =
    [
      ("#FF0000", new Rgba32(255, 0, 0)),
      ("#00FF00", new Rgba32(0, 255, 0)),
      ("#0000FF", new Rgba32(0, 0, 255)),
      ("#FFFF00", new Rgba32(255, 255, 0)),
      ("#FF00FF", new Rgba32(255, 0, 255)),
      ("#00FFFF", new Rgba32(0, 255, 255)),
      ("#FF8000", new Rgba32(255, 128, 0)),
      ("#8000FF", new Rgba32(128, 0, 255)),
      ("#00FF80", new Rgba32(0, 255, 128)),
      ("#FF0080", new Rgba32(255, 0, 128)),
      ("#80FF00", new Rgba32(128, 255, 0)),
      ("#0080FF", new Rgba32(0, 128, 255)),
    ];

    private static readonly Lock Gate = new();
    private static readonly string StorePath = ConfigPaths.File("layouts.json");
    private static Dictionary<string, TemplateBox?[]>? _store;

    public static TemplateBox?[]? Cached(string templateId)
    {
      lock (Gate)
      {
        Load();
        return _store!.TryGetValue(templateId, out TemplateBox?[]? boxes) ? boxes : null;
      }
    }

    public static async Task<TemplateBox?[]?> GetAsync(ImgflipSession session, ImageCache cache, Meme meme, CancellationToken ct = default)
    {
      if (Cached(meme.Id) is { } cached) return cached;
      if (!session.IsAuthenticated) return null;

      int count = Math.Clamp(meme.BoxCount, 1, ProbeColors.Length);
      MemeCreationBox[] boxes = Enumerable.Range(0, count)
        .Select(i => new MemeCreationBox { Text = FillText, Color = ProbeColors[i].Hex, OutlineColor = ProbeColors[i].Hex })
        .ToArray();

      string url = await session.CaptionImage(meme.Id, string.Empty, string.Empty, null, null, boxes);
      string resultPath = await cache.GetAsync(url, ct);
      string templatePath = await cache.GetAsync(meme.Url, ct);

      TemplateBox?[] layout = Measure(templatePath, resultPath, count, meme.Width, meme.Height);
      lock (Gate)
      {
        Load();
        _store![meme.Id] = layout;
        Save();
      }
      return layout;
    }

    private static TemplateBox?[] Measure(string templatePath, string resultPath, int count, int templateW, int templateH)
    {
      using Image<Rgba32> template = ISImage.Load<Rgba32>(templatePath);
      using Image<Rgba32> result = ISImage.Load<Rgba32>(resultPath);

      int w = Math.Min(template.Width, result.Width);
      int h = Math.Min(template.Height, result.Height);
      double scaleX = (double)template.Width / Math.Max(1, result.Width);
      double scaleY = (double)template.Height / Math.Max(1, result.Height);

      int[][] rows = new int[count][];
      int[][] cols = new int[count][];
      for (int i = 0; i < count; i++)
      {
        rows[i] = new int[h];
        cols[i] = new int[w];
      }

      for (int y = 0; y < h; y++)
      {
        int ty = Math.Min(template.Height - 1, (int)(y * scaleY));
        for (int x = 0; x < w; x++)
        {
          int tx = Math.Min(template.Width - 1, (int)(x * scaleX));
          Rgba32 r = result[x, y];
          Rgba32 t = template[tx, ty];
          if (Distance(r, t) < ChangeThreshold) continue;

          int best = -1;
          int bestDist = ColorThreshold;
          for (int i = 0; i < count; i++)
          {
            int d = Distance(r, ProbeColors[i].Rgb);
            if (d < bestDist) { bestDist = d; best = i; }
          }
          if (best < 0) continue;
          rows[best][y]++;
          cols[best][x]++;
        }
      }

      double toTemplateX = (double)templateW / Math.Max(1, result.Width);
      double toTemplateY = (double)templateH / Math.Max(1, result.Height);
      TemplateBox?[] layout = new TemplateBox?[count];
      for (int i = 0; i < count; i++)
      {
        (int y0, int y1) = DenseRange(rows[i]);
        (int x0, int x1) = DenseRange(cols[i]);
        if (y0 < 0 || x0 < 0) continue;

        int bx0 = Math.Max(0, (int)((x0 - Padding) * toTemplateX));
        int by0 = Math.Max(0, (int)((y0 - Padding) * toTemplateY));
        int bx1 = Math.Min(templateW, (int)((x1 + Padding) * toTemplateX));
        int by1 = Math.Min(templateH, (int)((y1 + Padding) * toTemplateY));
        if (bx1 - bx0 < 10 || by1 - by0 < 10) continue;
        layout[i] = new TemplateBox(bx0, by0, bx1 - bx0, by1 - by0);
      }
      return layout;
    }

    private static (int From, int To) DenseRange(int[] counts)
    {
      int peak = counts.Max();
      if (peak < 8) return (-1, -1);
      int threshold = Math.Max(3, peak / 12);
      int from = Array.FindIndex(counts, c => c >= threshold);
      int to = Array.FindLastIndex(counts, c => c >= threshold);
      return (from, to);
    }

    private static int Distance(Rgba32 a, Rgba32 b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static void Load()
    {
      if (_store is not null) return;
      try
      {
        _store = File.Exists(StorePath)
          ? JsonSerializer.Deserialize<Dictionary<string, TemplateBox?[]>>(File.ReadAllText(StorePath)) ?? []
          : [];
      }
      catch (Exception)
      {
        _store = [];
      }
    }

    private static void Save()
    {
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_store));
      }
      catch (IOException)
      {
      }
    }
  }
}
