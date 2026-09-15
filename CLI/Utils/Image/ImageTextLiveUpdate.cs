using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ISImage = SixLabors.ImageSharp.Image;

namespace image_flip_bosch.CLI.Utils.Image
{
  public sealed record MemeTextArea(string Text, int X, int Y, int Width, int Height);

  public sealed record MemeBoxOverlay(int X, int Y, int Width, int Height, string ColorHex, bool Active);

  public static class ImageTextLiveUpdate
  {
    private const int MaxSide = 900;
    private const float MinFontSize = 8f;

    private const float LineFillRatio = 0.6f;

    private const float StrokeRatio = 0.05f;

    private static readonly string[] BundledFontExtensions = [".ttf", ".otf"];

    private static readonly (string Name, FontStyle Style)[] FontCandidates =
    [
      ("Anton", FontStyle.Regular),
      ("Arial Black", FontStyle.Regular),
      ("DejaVu Sans", FontStyle.Bold),
      ("Liberation Sans", FontStyle.Bold),
      ("Arial", FontStyle.Bold),
    ];

    private static readonly Lock Gate = new();
    private static string? _basePath;
    private static Image<Rgba32>? _base;
    private static (FontFamily Family, FontStyle Style)? _face;
    private static bool _faceResolved;

    public static byte[] Compose(
      string templatePath,
      int templateWidth,
      int templateHeight,
      IReadOnlyList<MemeTextArea> texts,
      IReadOnlyList<MemeBoxOverlay> boxes,
      int? maxFontSize = null)
    {
      using Image<Rgba32> img = BaseImage(templatePath);

      double sx = (double)img.Width / Math.Max(1, templateWidth);
      double sy = (double)img.Height / Math.Max(1, templateHeight);

      foreach (MemeBoxOverlay box in boxes)
      {
        Rgba32 color = HexToRgba(box.ColorHex);
        int x0 = (int)(box.X * sx), y0 = (int)(box.Y * sy);
        int x1 = (int)((box.X + box.Width) * sx) - 1, y1 = (int)((box.Y + box.Height) * sy) - 1;
        int thickness = box.Active ? 4 : 2;

        FillRect(img, x0, y0, x1, y1, new Rgba32(color.R, color.G, color.B, box.Active ? (byte)70 : (byte)40));
        for (int t = 0; t < thickness; t++)
          Outline(img, x0 + t, y0 + t, x1 - t, y1 - t, color);
      }

      foreach (MemeTextArea area in texts)
        DrawCaption(img, area, sx, sy, maxFontSize);

      using MemoryStream ms = new();
      img.SaveAsPng(ms);
      return ms.ToArray();
    }

    public static void Release()
    {
      lock (Gate)
      {
        _base?.Dispose();
        _base = null;
        _basePath = null;
      }
    }

    private static Image<Rgba32> BaseImage(string path)
    {
      lock (Gate)
      {
        if (_base is null || _basePath != path)
        {
          _base?.Dispose();
          _base = null;
          _basePath = null;

          using Image<Rgba32> loaded = ISImage.Load<Rgba32>(path);
          Image<Rgba32> frame = loaded.Frames.Count > 1 ? loaded.Frames.CloneFrame(0) : loaded.Clone();

          if (frame.Width > MaxSide || frame.Height > MaxSide)
          {
            double s = Math.Min((double)MaxSide / frame.Width, (double)MaxSide / frame.Height);
            frame.Mutate(x => x.Resize(Math.Max(1, (int)(frame.Width * s)), Math.Max(1, (int)(frame.Height * s))));
          }

          _base = frame;
          _basePath = path;
        }

        return _base.Clone();
      }
    }

    private static void DrawCaption(Image<Rgba32> img, MemeTextArea area, double sx, double sy, int? maxFontSize)
    {
      string text = area.Text.Trim();
      if (text.Length == 0) return;
      if (Face() is not { } face) return;

      float x = (float)(area.X * sx);
      float y = (float)(area.Y * sy);
      float w = Math.Max(1f, (float)(area.Width * sx));
      float h = Math.Max(1f, (float)(area.Height * sy));
      PointF center = new(x + w / 2f, y + h / 2f);


      float limit = h * LineFillRatio;
      if (maxFontSize is { } cap) limit = Math.Min(limit, (float)(cap * sy));

      float size = Math.Max(MinFontSize, limit);
      RichTextOptions options = Options(face, size, w, center);


      for (int pass = 0; pass < 6; pass++)
      {
        FontRectangle measured = TextMeasurer.MeasureSize(text, options);
        if (measured.Height <= h && measured.Width <= w) break;

        float factor = Math.Min(h / Math.Max(1f, measured.Height), w / Math.Max(1f, measured.Width));
        float next = Math.Max(MinFontSize, size * factor * 0.96f);
        if (next >= size) break;

        size = next;
        options = Options(face, size, w, center);
      }

      float stroke = Math.Max(1f, size * StrokeRatio);
      img.Mutate(ctx => ctx
        .DrawText(options, text, Brushes.Solid(Color.Black), Pens.Solid(Color.Black, stroke))
        .DrawText(options, text, Brushes.Solid(Color.White), Pens.Solid(Color.White, 1f)));
    }

    public static string FontName => Face()?.Family.Name ?? "no font found";

    private static RichTextOptions Options((FontFamily Family, FontStyle Style) face, float size, float wrappingWidth, PointF origin) =>
      new(face.Family.CreateFont(size, face.Style))
      {
        Origin = origin,
        WrappingLength = wrappingWidth,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
      };

    private static (FontFamily Family, FontStyle Style)? Face()
    {
      if (_faceResolved) return _face;

      lock (Gate)
      {
        if (_faceResolved) return _face;
        _faceResolved = true;

        if (SystemFonts.TryGet("Impact", out FontFamily impact))
        {
          _face = (impact, FontStyle.Regular);
          return _face;
        }

        if (TryBundledFont(out FontFamily bundled))
        {
          _face = (bundled, FontStyle.Regular);
          return _face;
        }

        foreach ((string name, FontStyle style) in FontCandidates)
        {
          if (!SystemFonts.TryGet(name, out FontFamily found)) continue;
          _face = (found, style);
          return _face;
        }

        foreach (FontFamily any in SystemFonts.Families)
        {
          _face = (any, FontStyle.Bold);
          return _face;
        }

        return _face;
      }
    }

    private static bool TryBundledFont(out FontFamily family)
    {
      family = default;
      string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
      if (!Directory.Exists(directory)) return false;

      FontCollection collection = new();
      bool found = false;

      foreach (string file in Directory.EnumerateFiles(directory).Order())
      {
        if (!BundledFontExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
        try
        {
          FontFamily added = collection.Add(file);
          if (found) continue;
          family = added;
          found = true;
        }
        catch (Exception)
        {
          // unreadable or unsupported font file, try the next one
        }
      }

      return found;
    }

    private static Rgba32 HexToRgba(string hex)
    {
      hex = hex.TrimStart('#');
      if (hex.Length < 6) return new Rgba32(255, 255, 255, 255);
      byte r = Convert.ToByte(hex[..2], 16);
      byte g = Convert.ToByte(hex.Substring(2, 2), 16);
      byte b = Convert.ToByte(hex.Substring(4, 2), 16);
      return new Rgba32(r, g, b, 255);
    }

    private static void FillRect(Image<Rgba32> img, int x0, int y0, int x1, int y1, Rgba32 tint)
    {
      x0 = Math.Clamp(x0, 0, img.Width - 1); x1 = Math.Clamp(x1, 0, img.Width - 1);
      y0 = Math.Clamp(y0, 0, img.Height - 1); y1 = Math.Clamp(y1, 0, img.Height - 1);
      if (x1 < x0 || y1 < y0) return;

      float a = tint.A / 255f;
      img.ProcessPixelRows(accessor =>
      {
        for (int y = y0; y <= y1; y++)
        {
          Span<Rgba32> row = accessor.GetRowSpan(y);
          for (int x = x0; x <= x1; x++)
          {
            Rgba32 p = row[x];
            row[x] = new Rgba32(
              (byte)(p.R + (tint.R - p.R) * a),
              (byte)(p.G + (tint.G - p.G) * a),
              (byte)(p.B + (tint.B - p.B) * a),
              255);
          }
        }
      });
    }

    private static void Outline(Image<Rgba32> img, int x0, int y0, int x1, int y1, Rgba32 color)
    {
      if (x1 < x0 || y1 < y0) return;
      for (int x = Math.Max(0, x0); x <= Math.Min(img.Width - 1, x1); x++)
      {
        if (y0 >= 0 && y0 < img.Height) img[x, y0] = color;
        if (y1 >= 0 && y1 < img.Height) img[x, y1] = color;
      }
      for (int y = Math.Max(0, y0); y <= Math.Min(img.Height - 1, y1); y++)
      {
        if (x0 >= 0 && x0 < img.Width) img[x0, y] = color;
        if (x1 >= 0 && x1 < img.Width) img[x1, y] = color;
      }
    }
  }
}
