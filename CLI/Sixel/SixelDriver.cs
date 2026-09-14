using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using System.Buffers;
using System.Text;
using Size = SharpConsoleUI.Helpers.Size;

namespace image_flip_bosch.CLI.Sixel
{

  public sealed class SixelDriver : IConsoleDriver
  {
    private sealed class Entry
    {
      public required SixelImageControl Control;
      public bool Changed;
      public SixelFrame? LastFrame;
      public int LastX = -1;
      public int LastY = -1;
      public bool[]? DirtyStripes;
    }

    private readonly Lock _writeLock = new();
    private readonly AutoResetEvent _writeSignal = new(false);
    private sealed record Job(byte[] Buffer, int Length, Entry Entry, bool[]? Mask);

    private Job? _mailbox;
    private long _emittedFrames;
    private long _emittedBytes;
    private long _statsTicks = DateTime.UtcNow.Ticks;
    private double _fps;
    private double _mbps;

    public (double Fps, double MBps) Stats
    {
      get
      {
        lock (_lock) return (_fps, _mbps);
      }
    }

    private readonly IConsoleDriver _inner;
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly Lock _lock = new();
    private int _cursorX;
    private int _cursorY;
    private long _lastEmitTicks;
    private bool _throttleScheduled;
    private bool _forceAllSnapshot;
    private static readonly TimeSpan MinEmitInterval = TimeSpan.FromMilliseconds(8);
    private readonly Stream _stdout = Console.OpenStandardOutput();
    private readonly bool _disabled = Environment.GetEnvironmentVariable("IFB_NO_SIXEL") is not null;
    private int[] _map = Array.Empty<int>();
    private int _w;
    private int _h;
    private bool _forceAll;

    public SixelCapabilities Capabilities { get; }

    public SixelDriver(IConsoleDriver inner, SixelCapabilities? capabilities = null)
    {
      _inner = inner;
      Capabilities = capabilities ?? SixelCapabilities.Default;
      new Thread(WriterLoop) { IsBackground = true, Name = "sixel-writer" }.Start();

      _inner.KeyPressed += (_, e) => KeyPressed?.Invoke(this, e);
      _inner.Paste += (_, e) => Paste?.Invoke(this, e);
      _inner.MouseEvent += (_, flags, point) => MouseEvent?.Invoke(this, flags, point);
      _inner.ScreenResized += (_, size) =>
      {
        lock (_lock) { EnsureMap(size.Width, size.Height, reset: true); _forceAll = true; }
        ScreenResized?.Invoke(this, size);
      };
    }

    private void EnsureMapFromScreen()
    {
      if (_w > 0 && _h > 0) return;
      Size size = _inner.ScreenSize;
      EnsureMap(size.Width, size.Height, reset: false);
    }

    internal void Register(SixelImageControl control)
    {
      lock (_lock) _entries[control.SentinelId] = new Entry { Control = control, Changed = true };
    }

    internal void Unregister(SixelImageControl control)
    {
      lock (_lock) _entries.Remove(control.SentinelId);
    }

    internal void RequestEmit(SixelImageControl control)
    {
      lock (_lock)
      {
        if (_entries.TryGetValue(control.SentinelId, out Entry? e)) e.Changed = true;
      }
    }

    public event EventHandler<ConsoleKeyInfo>? KeyPressed;
    public event EventHandler<string>? Paste;
    public event IConsoleDriver.MouseEventHandler? MouseEvent;
    public event EventHandler<Size>? ScreenResized;

    public Size ScreenSize => _inner.ScreenSize;
    public bool SupportsBlockingLoop => _inner.SupportsBlockingLoop;

    public void Clear()
    {
      lock (_lock) { Array.Clear(_map); _forceAll = true; }
      _inner.Clear();
    }

    public void InvalidateFrontBuffer()
    {
      lock (_lock) _forceAll = true;
      _inner.InvalidateFrontBuffer();
    }

    public void Flush()
    {
      lock (_writeLock) _inner.Flush();
      EmitPending();
    }

    private void WriterLoop()
    {
      while (true)
      {
        _writeSignal.WaitOne();
        Job? job;
        lock (_lock)
        {
          job = _mailbox;
          _mailbox = null;
        }
        if (job is null) continue;

        try
        {
          lock (_writeLock)
          {
            Console.Out.Flush();
            _stdout.Write(job.Buffer, 0, job.Length);
            _stdout.Flush();
          }
        }
        catch (IOException) { }
        finally
        {
          ArrayPool<byte>.Shared.Return(job.Buffer);
        }

        lock (_lock)
        {
          _emittedFrames++;
          _emittedBytes += job.Length;
          long now = DateTime.UtcNow.Ticks;
          double seconds = (now - _statsTicks) / (double)TimeSpan.TicksPerSecond;
          if (seconds >= 1)
          {
            _fps = _emittedFrames / seconds;
            _mbps = _emittedBytes / seconds / (1024 * 1024);
            _emittedFrames = 0;
            _emittedBytes = 0;
            _statsTicks = now;
          }
        }
      }
    }

    private void Post(byte[] buffer, int length, Entry entry, bool[]? mask)
    {
      Job? dropped;
      lock (_lock)
      {
        dropped = _mailbox;
        _mailbox = new Job(buffer, length, entry, mask);
        if (dropped is not null)
        {
          if (dropped.Mask is null) dropped.Entry.Changed = true;
          else if (dropped.Entry.DirtyStripes is { } d && d.Length == dropped.Mask.Length)
            for (int s = 0; s < d.Length; s++) d[s] |= dropped.Mask[s];
        }
      }
      if (dropped is not null) ArrayPool<byte>.Shared.Return(dropped.Buffer);
      _writeSignal.Set();
    }

    public void Start()
    {
      _inner.Start();
      lock (_lock)
      {
        Size size = _inner.ScreenSize;
        EnsureMap(size.Width, size.Height, reset: true);
      }
    }
    public void Stop() => _inner.Stop();
    public void SetCursorPosition(int x, int y)
    {
      _cursorX = x;
      _cursorY = y;
      _inner.SetCursorPosition(x, y);
    }
    public void SetCursorVisible(bool visible) => _inner.SetCursorVisible(visible);
    public void SetCursorShape(CursorShape shape) => _inner.SetCursorShape(shape);
    public void SetCursorShape(CursorShape shape, CursorBlink blink) => _inner.SetCursorShape(shape, blink);
    public void WriteClipboardOsc52(string sequence) => _inner.WriteClipboardOsc52(sequence);
    public void ResetCursorShape() => _inner.ResetCursorShape();
    public void Initialize(ConsoleWindowSystem windowSystem) => _inner.Initialize(windowSystem);
    public int GetDirtyCharacterCount() => _inner.GetDirtyCharacterCount();

    public void SetNarrowCell(int x, int y, char character, Color fg, Color bg)
    {
      lock (_lock)
      {
        EnsureMapFromScreen();
        Track(x, y, SixelImageControl.SentinelToId(fg, character));
      }
      _inner.SetNarrowCell(x, y, character, fg, bg);
    }

    public void FillCells(int x, int y, int width, char character, Color fg, Color bg)
    {
      int id = SixelImageControl.SentinelToId(fg, character);
      lock (_lock)
      {
        EnsureMapFromScreen();
        for (int i = 0; i < width; i++) Track(x + i, y, id);
      }
      _inner.FillCells(x, y, width, character, fg, bg);
    }

    public void WriteBufferRegion(int destX, int destY, CharacterBuffer source, int srcX, int srcY, int width, Color fallbackBg)
    {
      if (_entries.Count > 0)
      {
        lock (_lock)
        {
          EnsureMapFromScreen();
          if (destY >= 0 && destY < _h)
          {
            for (int i = 0; i < width; i++)
            {
              Cell cell = source.GetCell(srcX + i, srcY);
              int id = cell.Character.Value == ' ' ? SixelImageControl.SentinelToId(cell.Foreground) : 0;
              Track(destX + i, destY, id);
            }
          }
        }
      }
      _inner.WriteBufferRegion(destX, destY, source, srcX, srcY, width, fallbackBg);
    }

    private void Track(int x, int y, int id)
    {
      if (x < 0 || y < 0 || x >= _w || y >= _h) return;

      int idx = y * _w + x;
      int prev = _map[idx];
      if (prev == id) return;

      _map[idx] = id;
      if (prev != 0 && _entries.TryGetValue(prev, out Entry? pe)) pe.Changed = true;
      if (id != 0 && _entries.TryGetValue(id, out Entry? ne)) ne.Changed = true;
    }

    private void EnsureMap(int w, int h, bool reset)
    {
      if (w <= 0 || h <= 0) return;
      if (w == _w && h == _h && !reset) return;
      _map = new int[w * h];
      _w = w;
      _h = h;
    }

    private void EmitPending()
    {
      if (_disabled) return;
      List<(Entry entry, int x, int y, int w, int h)> ready = [];

      lock (_lock)
      {
        _forceAllSnapshot = _forceAll;
        foreach (Entry entry in _entries.Values)
        {
          if (!_forceAll && entry is { Changed: false, Control.HasPendingFrame: false }) continue;

          int id = entry.Control.SentinelId;
          int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
          for (int y = 0; y < _h; y++)
          {
            int rowOff = y * _w;
            for (int x = 0; x < _w; x++)
            {
              if (_map[rowOff + x] != id) continue;
              count++;
              if (x < minX) minX = x;
              if (x > maxX) maxX = x;
              if (y < minY) minY = y;
              if (y > maxY) maxY = y;
            }
          }

          if (count == 0) continue;
          int w = maxX - minX + 1;
          int h = maxY - minY + 1;
          if (count != w * h) continue;

          ready.Add((entry, minX, minY, w, h));
        }
        _forceAll = false;
      }

      if (ready.Count == 0) return;

      var sinceLast = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _lastEmitTicks);
      if (sinceLast < MinEmitInterval)
      {
        lock (_lock) _forceAll = _forceAll || _forceAllSnapshot;
        ScheduleRetry(MinEmitInterval - sinceLast, ready[0].entry.Control);
        return;
      }

      foreach ((Entry entry, int x, int y, int w, int h) in ready)
      {
        SixelFrame? frame = entry.Control.GetFrame(w, h, Capabilities.CellWidth, Capabilities.CellHeight);
        if (frame is null) continue;

        bool regionRewritten = entry.Changed || _forceAllSnapshot;
        if (!regionRewritten && ReferenceEquals(frame, entry.LastFrame) && entry.LastX == x && entry.LastY == y)
          continue;

        bool full = regionRewritten || entry.LastX != x || entry.LastY != y
          || entry.LastFrame is null || entry.LastFrame.Stripes is null || frame.Stripes is null
          || entry.LastFrame.Stripes.Length != frame.Stripes.Length;

        entry.LastFrame = frame;
        entry.LastX = x;
        entry.LastY = y;
        entry.Changed = false;

        byte[] tail = Encoding.ASCII.GetBytes($"\x1b[0m\x1b[{_cursorY + 1};{_cursorX + 1}H");
        if (frame.Stripes is null)
        {
          byte[] head = Encoding.ASCII.GetBytes($"\x1b[0m\x1b[{y + 1};{x + 1}H");
          int total = head.Length + frame.Data.Length + tail.Length;
          byte[] buf = ArrayPool<byte>.Shared.Rent(total);
          head.CopyTo(buf, 0);
          frame.Data.CopyTo(buf, head.Length);
          tail.CopyTo(buf, head.Length + frame.Data.Length);
          entry.DirtyStripes = null;
          Post(buf, total, entry, null);
          continue;
        }

        bool[] dirty = entry.DirtyStripes is { } d && d.Length == frame.Stripes.Length ? d : new bool[frame.Stripes.Length];
        entry.DirtyStripes = dirty;
        int size = tail.Length;
        for (int s = 0; s < frame.Stripes.Length; s++)
        {
          dirty[s] |= full || frame.Stripes[s].Changed;
          if (dirty[s]) size += frame.Stripes[s].Data.Length + 24;
        }

        byte[] sbuf = ArrayPool<byte>.Shared.Rent(size);
        bool[] mask = new bool[frame.Stripes.Length];
        int n = 0;
        for (int s = 0; s < frame.Stripes.Length; s++)
        {
          if (!dirty[s]) continue;
          SixelStripe stripe = frame.Stripes[s];
          n += Encoding.ASCII.GetBytes($"\x1b[0m\x1b[{y + 1 + stripe.Row};{x + 1}H", sbuf.AsSpan(n));
          stripe.Data.CopyTo(sbuf, n);
          n += stripe.Data.Length;
          mask[s] = true;
          dirty[s] = false;
        }
        if (n == 0)
        {
          ArrayPool<byte>.Shared.Return(sbuf);
          continue;
        }
        tail.CopyTo(sbuf, n);
        n += tail.Length;
        Post(sbuf, n, entry, mask);
      }

      _lastEmitTicks = DateTime.UtcNow.Ticks;
    }

    private void ScheduleRetry(TimeSpan delay, SixelImageControl control)
    {
      lock (_lock)
      {
        if (_throttleScheduled) return;
        _throttleScheduled = true;
      }

      _ = Task.Delay(delay).ContinueWith(_ =>
      {
        lock (_lock) _throttleScheduled = false;
        control.RequestRepaint();
      });
    }
  }
}
