using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
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
    }

    private readonly IConsoleDriver _inner;
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly Stream _stdout = Console.OpenStandardOutput();
    private readonly Lock _lock = new();
    private int[] _map = [];
    private int _w;
    private int _h;
    private bool _forceAll;

    public SixelCapabilities Capabilities { get; }

    public SixelDriver(IConsoleDriver inner, SixelCapabilities? capabilities = null)
    {
      _inner = inner;
      Capabilities = capabilities ?? SixelCapabilities.Default;

      _inner.KeyPressed += (s, e) => KeyPressed?.Invoke(this, e);
      _inner.Paste += (s, e) => Paste?.Invoke(this, e);
      _inner.MouseEvent += (s, flags, point) => MouseEvent?.Invoke(this, flags, point);
      _inner.ScreenResized += (s, size) =>
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
      _inner.Flush();
      EmitPending();
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
    public void SetCursorPosition(int x, int y) => _inner.SetCursorPosition(x, y);
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
      _inner.SetNarrowCell(x, y, character,  fg, bg);
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
      List<(Entry entry, int x, int y, int w, int h)> ready = new();

      lock (_lock)
      {
        foreach (Entry entry in _entries.Values.Where(entry => _forceAll || entry is not { Changed: false, Control.HasPendingFrame: false }))
        {
          entry.Changed = false;

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

      foreach ((Entry entry, int x, int y, int w, int h) in ready)
      {
        SixelFrame? frame = entry.Control.GetFrame(w, h, Capabilities.CellWidth, Capabilities.CellHeight);
        if (frame is null) continue;

        StringBuilder sb = new(frame.Data.Length + 32);
        sb.Append("\x1b7");
        sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
        sb.Append(frame.Data);
        sb.Append("\x1b8");

        byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
        _stdout.Write(bytes, 0, bytes.Length);
        _stdout.Flush();
      }
    }
  }
}
