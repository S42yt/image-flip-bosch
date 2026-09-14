using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace image_flip_bosch.CLI.Sixel
{

  public sealed record SixelStripe(int Row, byte[] Data, bool Changed);

  public sealed record SixelFrame(byte[] Data, int Cols, int Rows, int PixelWidth, int PixelHeight, SixelStripe[]? Stripes = null);

  public sealed record SixelAnimationFrame(SixelFrame Frame, int DelayMs);

  public sealed class PaletteLut
  {
    private readonly Rgba32[] _palette;
    private readonly short[] _lut = new short[1 << 18];

    public PaletteLut(ReadOnlySpan<Rgba32> palette)
    {
      _palette = palette.ToArray();
      Array.Fill(_lut, (short)-1);
    }

    public ReadOnlySpan<Rgba32> Palette => _palette;

    public void Quantize(ReadOnlySpan<byte> rgba, Span<byte> indices)
    {
      for (int i = 0, p = 0; i < indices.Length; i++, p += 4)
      {
        int key = ((rgba[p] >> 2) << 12) | ((rgba[p + 1] >> 2) << 6) | (rgba[p + 2] >> 2);
        short idx = _lut[key];
        if (idx < 0)
        {
          idx = Nearest(rgba[p], rgba[p + 1], rgba[p + 2]);
          _lut[key] = idx;
        }
        indices[i] = (byte)idx;
      }
    }

    public double MeanError(ReadOnlySpan<byte> rgba, int stride)
    {
      long sum = 0;
      int n = 0;
      for (int p = 0; p + 3 < rgba.Length; p += 4 * stride)
      {
        int key = ((rgba[p] >> 2) << 12) | ((rgba[p + 1] >> 2) << 6) | (rgba[p + 2] >> 2);
        short idx = _lut[key];
        if (idx < 0)
        {
          idx = Nearest(rgba[p], rgba[p + 1], rgba[p + 2]);
          _lut[key] = idx;
        }
        Rgba32 c = _palette[idx];
        sum += Math.Abs(c.R - rgba[p]) + Math.Abs(c.G - rgba[p + 1]) + Math.Abs(c.B - rgba[p + 2]);
        n++;
      }
      return n == 0 ? 0 : sum / (3.0 * n);
    }

    private short Nearest(int r, int g, int b)
    {
      int best = 0;
      int bestDist = int.MaxValue;
      for (int i = 0; i < _palette.Length; i++)
      {
        Rgba32 c = _palette[i];
        int dr = c.R - r, dg = c.G - g, db = c.B - b;
        int d = dr * dr * 2 + dg * dg * 4 + db * db * 3;
        if (d < bestDist)
        {
          bestDist = d;
          best = i;
        }
      }
      return (short)best;
    }
  }

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
      bool dither = false,
      bool firstFrameOnly = false)
    {
      int canvasW = Math.Max(1, cols * cellWidth);
      int canvasH = Math.Max(1, rows * cellHeight);

      DecoderOptions options = new() { MaxFrames = firstFrameOnly ? 1 : uint.MaxValue };
      using var source = Image.Load<Rgba32>(options, imageData);

      (int targetW, int targetH, int offX, int offY) = Fit(source.Width, source.Height, canvasW, canvasH);
      if (targetW != source.Width || targetH != source.Height)
        source.Mutate(x => x.Resize(targetW, targetH));

      using Image<Rgba32> canvas = new(canvasW, canvasH, background);
      Blit(source.Frames.RootFrame, canvas, offX, offY, background);

      using IQuantizer<Rgba32> quantizer = new WuQuantizer(new QuantizerOptions
      {
        MaxColors = Math.Clamp(maxColors, 2, 256),
        Dither = dither ? KnownDitherings.Bayer4x4 : null,
      }).CreatePixelSpecificQuantizer<Rgba32>(canvas.Configuration);
      using IndexedImageFrame<Rgba32> indexed = quantizer.BuildPaletteAndQuantizeFrame(canvas.Frames.RootFrame, canvas.Bounds);

      byte[] indices = new byte[canvasW * canvasH];
      for (int y = 0; y < canvasH; y++)
        indexed.DangerousGetRowSpan(y).CopyTo(indices.AsSpan(y * canvasW, canvasW));

      return new SixelFrame(Encode(indices, canvasW, canvasH, indexed.Palette.Span), cols, rows, canvasW, canvasH);
    }

    public static bool IsAnimatedGif(byte[] data)
    {
      if (data.Length < 6 || data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F') return false;
      try
      {
        return Image.Identify(data).FrameMetadataCollection.Count > 1;
      }
      catch
      {
        return false;
      }
    }

    public static List<SixelAnimationFrame> RenderGifToCells(
      byte[] gifData,
      int cols,
      int rows,
      int cellWidth,
      int cellHeight,
      Rgba32 background,
      int maxColors = 256,
      int maxFrames = 200,
      Action<int, int>? progress = null,
      CancellationToken ct = default)
    {
      int canvasW = Math.Max(1, cols * cellWidth);
      int canvasH = Math.Max(1, rows * cellHeight);

      using var gif = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = (uint)Math.Max(1, maxFrames) }, gifData);
      int frameCount = gif.Frames.Count;

      (int targetW, int targetH, int offX, int offY) = Fit(gif.Width, gif.Height, canvasW, canvasH);
      if (targetW != gif.Width || targetH != gif.Height)
        gif.Mutate(x => x.Resize(targetW, targetH));

      PaletteLut lut = BuildGifPalette(gif, background, maxColors);
      int[] delays = new int[frameCount];
      for (int i = 0; i < frameCount; i++)
      {
        int delay = gif.Frames[i].Metadata.GetGifMetadata().FrameDelay * 10;
        delays[i] = delay < 20 ? 100 : delay;
      }

      var result = new SixelAnimationFrame[frameCount];
      int done = 0;
      ParallelOptions po = new() { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
      Parallel.For(0, frameCount, po, i =>
      {
        using Image<Rgba32> canvas = new(canvasW, canvasH, background);
        Blit(gif.Frames[i], canvas, offX, offY, background);

        byte[] rgba = new byte[canvasW * canvasH * 4];
        canvas.CopyPixelDataTo(rgba);
        byte[] indices = new byte[canvasW * canvasH];
        lut.Quantize(rgba, indices);

        byte[] data = Encode(indices, canvasW, canvasH, lut.Palette);
        result[i] = new SixelAnimationFrame(new SixelFrame(data, cols, rows, canvasW, canvasH), delays[i]);

        int n = Interlocked.Increment(ref done);
        if (n % 8 == 0 || n == frameCount) progress?.Invoke(n, frameCount);
      });

      return [.. result];
    }

    public static PaletteLut BuildPaletteRgba(ReadOnlySpan<byte> rgba, int width, int height, int maxColors)
    {
      int sw = Math.Max(1, width / 2);
      int sh = Math.Max(1, height / 2);
      byte[] sample = new byte[sw * sh * 4];
      for (int y = 0; y < sh; y++)
      {
        int srcRow = (y * 2) * width * 4;
        int dstRow = y * sw * 4;
        for (int x = 0; x < sw; x++)
          rgba.Slice(srcRow + x * 8, 4).CopyTo(sample.AsSpan(dstRow + x * 4, 4));
      }

      using var image = Image.LoadPixelData<Rgba32>(sample, sw, sh);
      using IQuantizer<Rgba32> wu = new WuQuantizer(new QuantizerOptions { MaxColors = Math.Clamp(maxColors, 2, 256), Dither = null })
        .CreatePixelSpecificQuantizer<Rgba32>(image.Configuration);
      using IndexedImageFrame<Rgba32> indexed = wu.BuildPaletteAndQuantizeFrame(image.Frames.RootFrame, image.Bounds);
      return new PaletteLut(indexed.Palette.Span);
    }

    public static SixelFrame EncodeRgba(ReadOnlySpan<byte> rgba, int width, int height, int cols, int rows, PaletteLut lut)
    {
      byte[] indices = new byte[width * height];
      lut.Quantize(rgba, indices);
      return new SixelFrame(Encode(indices, width, height, lut.Palette), cols, rows, width, height);
    }

    private static (int W, int H, int OffX, int OffY) Fit(int srcW, int srcH, int canvasW, int canvasH)
    {
      double scale = Math.Min(1.0, Math.Min((double)canvasW / srcW, (double)canvasH / srcH));
      int w = Math.Max(1, (int)Math.Round(srcW * scale));
      int h = Math.Max(1, (int)Math.Round(srcH * scale));
      return (w, h, (canvasW - w) / 2, (canvasH - h) / 2);
    }

    private static void Blit(ImageFrame<Rgba32> frame, Image<Rgba32> canvas, int offX, int offY, Rgba32 background)
    {
      int w = Math.Min(frame.Width, canvas.Width - offX);
      int h = Math.Min(frame.Height, canvas.Height - offY);
      for (int y = 0; y < h; y++)
      {
        Span<Rgba32> src = frame.DangerousGetPixelRowMemory(y).Span;
        Span<Rgba32> dst = canvas.DangerousGetPixelRowMemory(y + offY).Span.Slice(offX, w);
        for (int x = 0; x < w; x++)
        {
          Rgba32 p = src[x];
          switch (p.A)
          {
            case 255:
              dst[x] = p;
              continue;
            case 0:
              dst[x] = background;
              continue;
            default:
            {
              int a = p.A;
              dst[x] = new Rgba32(
                (byte)((p.R * a + background.R * (255 - a)) / 255),
                (byte)((p.G * a + background.G * (255 - a)) / 255),
                (byte)((p.B * a + background.B * (255 - a)) / 255),
                255);
              break;
            }
          }
        }
      }
    }

    private static PaletteLut BuildGifPalette(Image<Rgba32> gif, Rgba32 background, int maxColors)
    {
      int n = gif.Frames.Count;
      int[] picks = n <= 4 ? Enumerable.Range(0, n).ToArray() : [0, n / 3, 2 * n / 3, n - 1];

      using Image<Rgba32> sheet = new(gif.Width * picks.Length, gif.Height, background);
      for (int i = 0; i < picks.Length; i++)
        Blit(gif.Frames[picks[i]], sheet, i * gif.Width, 0, background);

      using IQuantizer<Rgba32> wu = new WuQuantizer(new QuantizerOptions { MaxColors = Math.Clamp(maxColors, 2, 256), Dither = null })
        .CreatePixelSpecificQuantizer<Rgba32>(sheet.Configuration);
      using IndexedImageFrame<Rgba32> indexed = wu.BuildPaletteAndQuantizeFrame(sheet.Frames.RootFrame, sheet.Bounds);
      return new PaletteLut(indexed.Palette.Span);
    }

    private static byte[] Encode(byte[] indices, int w, int h, ReadOnlySpan<Rgba32> palette) =>
      Encode(indices, w, 0, h, palette, onlyUsedColors: false);

    internal static byte[] Encode(byte[] indices, int w, int yFrom, int yTo, ReadOnlySpan<Rgba32> palette, bool onlyUsedColors)
    {
      int colors = palette.Length;
      int h = yTo - yFrom;
      ByteBuffer o = new(w * h / 3 + 4096);
      o.Ascii("\eP0;0;0q\"1;1;");
      o.Int(w).Byte((byte)';').Int(h);

      bool[]? define = null;
      if (onlyUsedColors)
      {
        define = new bool[colors];
        for (int p = yFrom * w; p < yTo * w; p++) define[indices[p]] = true;
      }
      for (int i = 0; i < colors; i++)
      {
        if (define is not null && !define[i]) continue;
        Rgba32 c = palette[i];
        o.Byte((byte)'#').Int(i).Ascii(";2;").Int(c.R * 100 / 255).Byte((byte)';').Int(c.G * 100 / 255).Byte((byte)';').Int(c.B * 100 / 255);
      }

      int groups = Math.Clamp(h / 120, 1, Environment.ProcessorCount);
      if (groups == 1)
      {
        EncodeBands(indices, w, yFrom, yTo, colors, o);
      }
      else
      {
        int bandsTotal = (h + 5) / 6;
        int bandsPerGroup = (bandsTotal + groups - 1) / groups;
        ByteBuffer[] parts = new ByteBuffer[groups];
        Parallel.For(0, groups, g =>
        {
          int from = yFrom + g * bandsPerGroup * 6;
          int to = Math.Min(yTo, from + bandsPerGroup * 6);
          ByteBuffer part = new(Math.Max(1024, w * (to - from) / 3));
          if (from < to) EncodeBands(indices, w, from, to, colors, part);
          parts[g] = part;
        });
        foreach (ByteBuffer part in parts) o.Bytes(part.AsSpan());
      }

      o.Ascii("\e\\");
      return o.ToArray();
    }

    private static void EncodeBands(byte[] indices, int w, int yFrom, int yTo, int colors, ByteBuffer o)
    {
      int cap = 6 * w;
      int[] lastX = new int[colors];
      int[] lastPos = new int[colors];
      int[] count = new int[colors];
      int[] start = new int[colors + 1];
      int[] eColor = new int[cap];
      int[] eX = new int[cap];
      byte[] eMask = new byte[cap];
      int[] sX = new int[cap];
      byte[] sMask = new byte[cap];

      for (int band = yFrom; band < yTo; band += 6)
      {
        int bandRows = Math.Min(6, yTo - band);
        Array.Fill(lastX, -1);
        Array.Clear(count);
        int n = 0;

        for (int x = 0; x < w; x++)
        {
          for (int r = 0; r < bandRows; r++)
          {
            int idx = indices[(band + r) * w + x];
            byte bit = (byte)(1 << r);
            if (lastX[idx] == x)
            {
              eMask[lastPos[idx]] |= bit;
              continue;
            }
            eColor[n] = idx;
            eX[n] = x;
            eMask[n] = bit;
            lastX[idx] = x;
            lastPos[idx] = n;
            count[idx]++;
            n++;
          }
        }

        int acc = 0;
        for (int c = 0; c < colors; c++)
        {
          start[c] = acc;
          acc += count[c];
        }
        start[colors] = acc;

        for (int e = 0; e < n; e++)
        {
          int c = eColor[e];
          int pos = start[c]++;
          sX[pos] = eX[e];
          sMask[pos] = eMask[e];
        }

        bool firstColor = true;
        for (int c = 0; c < colors; c++)
        {
          int cnt = count[c];
          if (cnt == 0) continue;
          int from = start[c] - cnt;

          if (!firstColor) o.Byte((byte)'$');
          firstColor = false;
          o.Byte((byte)'#').Int(c);

          int prev = -1;
          int runMask = -1;
          int runLen = 0;
          for (int pos = from; pos < from + cnt; pos++)
          {
            int x = sX[pos];
            int gap = x - prev - 1;
            byte m = sMask[pos];
            if (gap > 0)
            {
              if (runLen > 0) o.Run(runMask, runLen);
              o.Run(0, gap);
              runMask = m;
              runLen = 1;
            }
            else if (m == runMask)
            {
              runLen++;
            }
            else
            {
              if (runLen > 0) o.Run(runMask, runLen);
              runMask = m;
              runLen = 1;
            }
            prev = x;
          }
          if (runLen > 0) o.Run(runMask, runLen);
        }

        o.Byte((byte)'-');
      }
    }

    private sealed class ByteBuffer(int capacity)
    {
      private byte[] _buf = new byte[capacity];
      private int _len;

      public ByteBuffer Byte(byte b)
      {
        if (_len == _buf.Length) Grow(1);
        _buf[_len++] = b;
        return this;
      }

      public ByteBuffer Ascii(string s)
      {
        if (_len + s.Length > _buf.Length) Grow(s.Length);
        for (int i = 0; i < s.Length; i++) _buf[_len++] = (byte)s[i];
        return this;
      }

      public ByteBuffer Int(int v)
      {
        if (v >= 10) Int(v / 10);
        return Byte((byte)('0' + v % 10));
      }

      public ByteBuffer Run(int bits, int count)
      {
        byte ch = (byte)(63 + bits);
        if (count > 3) return Byte((byte)'!').Int(count).Byte(ch);
        if (_len + count > _buf.Length) Grow(count);
        for (int i = 0; i < count; i++) _buf[_len++] = ch;
        return this;
      }

      public ByteBuffer Bytes(ReadOnlySpan<byte> data)
      {
        if (_len + data.Length > _buf.Length) Grow(data.Length);
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
        return this;
      }

      public ReadOnlySpan<byte> AsSpan() => _buf.AsSpan(0, _len);

      public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();

      private void Grow(int extra) => Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + extra));
    }
  }
}
