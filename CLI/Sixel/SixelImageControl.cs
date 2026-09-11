namespace image_flip_bosch.CLI.Sixel
{
  using SharpConsoleUI;
  using SharpConsoleUI.Controls;
  using SharpConsoleUI.Layout;
  using System;
  using System.Threading;
  using System.Threading.Tasks;

  public class SixelImageControl : BaseControl
  {
    private static int _nextId;

    private readonly object _lock = new();
    private byte[]? _data;
    private int _version;
    private SixelFrame? _frame;
    private (int cols, int rows, int cw, int ch, int version)? _frameKey;
    private (int cols, int rows, int cw, int ch, int version)? _encodingKey;
    private SixelDriver? _driver;

    public SixelImageControl()
    {
      SentinelId = Interlocked.Increment(ref _nextId);
    }

    public int SentinelId { get; }

    public int MaxColors { get; set; } = 256;

    public bool Dither { get; set; } = true;

    public event EventHandler<string>? EncodeFailed;

    internal bool HasPendingFrame { get; private set; }

    public byte[]? ImageData => _data;

    public override int? ContentWidth => null;

    private Color Sentinel => new Color(3, (byte)(SentinelId >> 8), (byte)(SentinelId & 0xFF));

    public static int SentinelToId(Color fg, char character = ' ')
    {
      if (character != ' ' || fg.R != 3) return 0;
      return (fg.G << 8) | fg.B;
    }

    public void SetImage(byte[]? data)
    {
      lock (_lock)
      {
        _data = data;
        _version++;
        _frame = null;
        _frameKey = null;
        _encodingKey = null;
        HasPendingFrame = false;
      }
      _driver?.RequestEmit(this);
      Invalidate(Invalidation.Repaint);
    }

    public override LayoutSize MeasureDOM(LayoutConstraints constraints)
    {
      int w = constraints.MaxWidth > 10000 ? Math.Max(constraints.MinWidth, 1) : constraints.MaxWidth;
      int h = constraints.MaxHeight > 10000 ? Math.Max(constraints.MinHeight, 1) : constraints.MaxHeight;
      return new LayoutSize(Math.Max(w, constraints.MinWidth), Math.Max(h, constraints.MinHeight));
    }

    public override void PaintDOM(CharacterBuffer buffer, LayoutRect bounds, LayoutRect clipRect, Color defaultForeground, Color defaultBackground)
    {
      EnsureDriver();

      int x0 = bounds.X + Margin.Left;
      int y0 = bounds.Y + Margin.Top;
      int x1 = bounds.X + bounds.Width - Margin.Right;
      int y1 = bounds.Y + bounds.Height - Margin.Bottom;

      Color fg = _data is null ? defaultForeground : Sentinel;

      for (int y = Math.Max(y0, clipRect.Y); y < Math.Min(y1, clipRect.Y + clipRect.Height); y++)
        for (int x = Math.Max(x0, clipRect.X); x < Math.Min(x1, clipRect.X + clipRect.Width); x++)
          buffer.SetNarrowCell(x, y, ' ', fg, defaultBackground);
    }

    internal SixelFrame? GetFrame(int cols, int rows, int cellWidth, int cellHeight)
    {
      byte[]? data;
      (int, int, int, int, int) key;

      lock (_lock)
      {
        data = _data;
        if (data is null) return null;

        key = (cols, rows, cellWidth, cellHeight, _version);
        if (_frame is not null && _frameKey == key)
        {
          HasPendingFrame = false;
          return _frame;
        }
        if (_encodingKey == key) return null;
        _encodingKey = key;
      }

      int maxColors = MaxColors;
      bool dither = Dither;
      ConsoleWindowSystem? ws = Container?.GetConsoleWindowSystem;

      _ = Task.Run(() =>
      {
        SixelFrame? frame = null;
        string? error = null;
        try
        {
          frame = SixelEncoder.RenderToCells(data, cols, rows, cellWidth, cellHeight, maxColors, dither);
        }
        catch (Exception ex)
        {
          error = ex.Message;
        }

        lock (_lock)
        {
          if (_encodingKey != key) return;
          _encodingKey = null;
          if (frame is null) return;
          _frame = frame;
          _frameKey = key;
          HasPendingFrame = true;
        }

        if (error is not null)
        {
          ws?.InvokeAsync(() => EncodeFailed?.Invoke(this, error));
          return;
        }

        _driver?.RequestEmit(this);
        ws?.InvokeAsync(() => Invalidate(Invalidation.Repaint));
      });

      return null;
    }

    private void EnsureDriver()
    {
      if (_driver is not null) return;
      if (Container?.GetConsoleWindowSystem?.ConsoleDriver is SixelDriver driver)
      {
        _driver = driver;
        driver.Register(this);
      }
    }

    protected override void OnDisposing()
    {
      _driver?.Unregister(this);
      _driver = null;
      base.OnDisposing();
    }
  }
}
