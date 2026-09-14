using image_flip_bosch.CLI.Sixel.Core;

namespace image_flip_bosch.CLI.Sixel.Video
{
  public sealed class SixelVideoEncoder
  {
    private const double PaletteErrorLimit = 9.0;
    private const int PaletteMinAgeSeconds = 2;
    private const int PaletteMaxAgeSeconds = 15;
    private const int MinStripeHeight = 96;
    private const double FullFrameRatio = 0.6;

    private readonly int _width;
    private readonly int _height;
    private readonly int _cols;
    private readonly int _rows;
    private readonly int _cellHeight;
    private readonly int _stripeRows;
    private readonly int _maxColors;
    private PaletteLut? _lut;
    private int _paletteAge;
    private byte[]? _prevIndices;
    private SixelStripe[]? _prevStripes;

    public SixelVideoEncoder(int cols, int rows, int cellWidth, int cellHeight, int maxColors)
    {
      _cols = cols;
      _rows = rows;
      _width = cols * cellWidth;
      _height = rows * cellHeight;
      _cellHeight = cellHeight;
      _maxColors = maxColors;
      int k = 1;
      while (k * cellHeight % 6 != 0 || k * cellHeight < MinStripeHeight) k++;
      _stripeRows = Math.Min(k, Math.Max(1, rows));
    }

    public int StripeCount => (_rows + _stripeRows - 1) / _stripeRows;

    public SixelFrame Encode(byte[] rgba, int fps)
    {
      int perSecond = Math.Max(1, fps);
      bool newPalette = false;
      if (_lut is null)
      {
        newPalette = true;
      }
      else
      {
        _paletteAge++;
        bool checkNow = _paletteAge % perSecond == 0;
        bool oldEnough = _paletteAge >= PaletteMinAgeSeconds * perSecond;
        bool tooOld = _paletteAge >= PaletteMaxAgeSeconds * perSecond;
        if (tooOld || (checkNow && oldEnough && _lut.MeanError(rgba, 16) > PaletteErrorLimit)) newPalette = true;
      }
      if (newPalette)
      {
        _lut = SixelEncoder.BuildPaletteRgba(rgba, _width, _height, _maxColors);
        _paletteAge = 0;
      }

      byte[] indices = new byte[_width * _height];
      _lut!.Quantize(rgba, indices);

      int stripes = StripeCount;
      bool[] changed = new bool[stripes];
      int changedCount = 0;
      for (int s = 0; s < stripes; s++)
      {
        (int yFrom, int yTo) = Range(s);
        changed[s] = newPalette || _prevIndices is null
          || !indices.AsSpan(yFrom * _width, (yTo - yFrom) * _width).SequenceEqual(_prevIndices.AsSpan(yFrom * _width, (yTo - yFrom) * _width));
        if (changed[s]) changedCount++;
      }

      _prevIndices = indices;
      PaletteLut lut = _lut;

      if (changedCount >= stripes * FullFrameRatio)
      {
        _prevStripes = null;
        byte[] data = SixelEncoder.Encode(indices, _width, 0, _height, lut.Palette, onlyUsedColors: true);
        return new SixelFrame(data, _cols, _rows, _width, _height);
      }

      SixelStripe[] result = new SixelStripe[stripes];
      SixelStripe[]? prev = _prevStripes;
      Parallel.For(0, stripes, s =>
      {
        if (!changed[s] && prev is not null)
        {
          result[s] = prev[s] with { Changed = false };
          return;
        }
        (int yFrom, int yTo) = Range(s);
        result[s] = new SixelStripe(s * _stripeRows, SixelEncoder.Encode(indices, _width, yFrom, yTo, lut.Palette, onlyUsedColors: true), changed[s]);
      });

      _prevStripes = result;
      return new SixelFrame([], _cols, _rows, _width, _height, result);
    }

    private (int From, int To) Range(int stripe)
    {
      int from = stripe * _stripeRows * _cellHeight;
      return (from, Math.Min(_height, from + _stripeRows * _cellHeight));
    }
  }
}
