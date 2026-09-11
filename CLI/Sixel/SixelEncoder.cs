using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using System;
using System.Text;

namespace image_flip_bosch.CLI.Sixel
{

  public sealed record SixelFrame(string Data, int Cols, int Rows, int PixelWidth, int PixelHeight);

  public static class SixelEncoder
  {
    public static SixelFrame RenderToCells(
      byte[] imageData,
      int cols,
      int rows,
      int cellWidth,
      int cellHeight,
      Rgba32 background,
      int maxColors = 256,
      bool dither = true)
    {
      int canvasW = Math.Max(1, cols * cellWidth);
      int canvasH = Math.Max(1, rows * cellHeight);

      using Image<Rgba32> source = Image.Load<Rgba32>(imageData);

      double scale = Math.Min(1.0, Math.Min((double)canvasW / source.Width, (double)canvasH / source.Height));
      int targetW = Math.Max(1, (int)Math.Round(source.Width * scale));
      int targetH = Math.Max(1, (int)Math.Round(source.Height * scale));

      if (targetW != source.Width || targetH != source.Height)
        source.Mutate(x => x.Resize(targetW, targetH));

      using Image<Rgba32> canvas = new(canvasW, canvasH, background);
      int offX = (canvasW - targetW) / 2;
      int offY = (canvasH - targetH) / 2;
      canvas.Mutate(x => x.DrawImage(source, new Point(offX, offY), 1f));

      string data = Encode(canvas, maxColors, dither, opaque: true);
      return new SixelFrame(data, cols, rows, canvasW, canvasH);
    }

    public static string Encode(Image<Rgba32> image, int maxColors = 256, bool dither = true, bool opaque = false)
    {
      QuantizerOptions options = new()
      {
        MaxColors = Math.Clamp(maxColors, 2, 256),
        Dither = dither ? KnownDitherings.Bayer4x4 : null,
      };

      using IQuantizer<Rgba32> quantizer = new WuQuantizer(options).CreatePixelSpecificQuantizer<Rgba32>(image.Configuration);
      using IndexedImageFrame<Rgba32> indexed = quantizer.BuildPaletteAndQuantizeFrame(image.Frames.RootFrame, image.Bounds);

      int w = indexed.Width;
      int h = indexed.Height;
      ReadOnlySpan<Rgba32> palette = indexed.Palette.Span;

      StringBuilder sb = new(w * h / 4 + 1024);
      sb.Append(opaque ? "\x1bP0;0;0q" : "\x1bP0;1;0q");
      sb.Append("\"1;1;").Append(w).Append(';').Append(h);

      for (int i = 0; i < palette.Length; i++)
      {
        Rgba32 c = palette[i];
        sb.Append('#').Append(i).Append(";2;")
          .Append(c.R * 100 / 255).Append(';')
          .Append(c.G * 100 / 255).Append(';')
          .Append(c.B * 100 / 255);
      }

      int colors = palette.Length;
      byte[] bits = new byte[colors * w];
      bool[] used = new bool[colors];
      byte[] alpha = new byte[w];

      for (int band = 0; band < h; band += 6)
      {
        int bandRows = Math.Min(6, h - band);
        Array.Clear(used);

        for (int r = 0; r < bandRows; r++)
        {
          ReadOnlySpan<byte> row = indexed.DangerousGetRowSpan(band + r);
          byte bit = (byte)(1 << r);
          for (int x = 0; x < w; x++)
          {
            int idx = row[x];
            bits[idx * w + x] |= bit;
            used[idx] = true;
          }
        }

        bool first = true;
        for (int c = 0; c < colors; c++)
        {
          if (!used[c]) continue;

          int offset = c * w;
          int last = w - 1;
          while (last >= 0 && bits[offset + last] == 0) last--;
          if (last < 0) { Array.Clear(bits, offset, w); continue; }

          if (!first) sb.Append('$');
          first = false;
          sb.Append('#').Append(c);

          int runChar = bits[offset];
          int runLen = 1;
          for (int x = 1; x <= last + 1; x++)
          {
            int b = x <= last ? bits[offset + x] : -1;
            if (b == runChar) { runLen++; continue; }
            AppendRun(sb, runChar, runLen);
            runChar = b;
            runLen = 1;
          }

          Array.Clear(bits, offset, w);
        }

        sb.Append('-');
      }

      sb.Append("\x1b\\");
      return sb.ToString();
    }

    private static void AppendRun(StringBuilder sb, int bitsValue, int count)
    {
      char ch = (char)(63 + bitsValue);
      if (count > 3) sb.Append('!').Append(count).Append(ch);
      else sb.Append(ch, count);
    }
  }
}
