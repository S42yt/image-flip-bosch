using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace image_flip_bosch.CLI.Sixel
{

  public sealed record SixelCapabilities(bool Supported, int CellWidth, int CellHeight)
  {
    public static SixelCapabilities Default => new(true, 10, 20);
  }

  public static class SixelTerminal
  {
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static SixelCapabilities Probe(int timeoutMs = 500)
    {
      if (Console.IsInputRedirected || Console.IsOutputRedirected)
        return SixelCapabilities.Default;

      bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
      uint inputMode = 0;
      IntPtr stdin = IntPtr.Zero;

      try
      {
        if (windows)
        {
          stdin = GetStdHandle(StdInputHandle);
          IntPtr stdout = GetStdHandle(StdOutputHandle);
          if (GetConsoleMode(stdout, out uint outMode))
            SetConsoleMode(stdout, outMode | EnableVirtualTerminalProcessing);
          if (GetConsoleMode(stdin, out inputMode))
            SetConsoleMode(stdin, inputMode | EnableVirtualTerminalInput);
        }

        while (Console.KeyAvailable) Console.ReadKey(true);

        Console.Out.Write("\x1b[c\x1b[16t");
        Console.Out.Flush();

        string response = ReadResponse(timeoutMs);

        bool supported = false;
        int cellW = 10;
        int cellH = 20;

        int da = response.IndexOf("\x1b[?", StringComparison.Ordinal);
        if (da >= 0)
        {
          int end = response.IndexOf('c', da);
          if (end > da)
          {
            string[] parts = response.Substring(da + 3, end - da - 3).Split(';');
            foreach (string p in parts)
              if (p == "4") supported = true;
          }
        }

        int cs = response.IndexOf("\x1b[6;", StringComparison.Ordinal);
        if (cs >= 0)
        {
          int end = response.IndexOf('t', cs);
          if (end > cs)
          {
            string[] parts = response.Substring(cs + 4, end - cs - 4).Split(';');
            if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int w) && w > 0 && h > 0)
            {
              cellW = w;
              cellH = h;
            }
          }
        }

        return new SixelCapabilities(supported, cellW, cellH);
      }
      catch
      {
        return SixelCapabilities.Default;
      }
      finally
      {
        if (windows && stdin != IntPtr.Zero && inputMode != 0)
          SetConsoleMode(stdin, inputMode);
      }
    }

    private static string ReadResponse(int timeoutMs)
    {
      StringBuilder sb = new();
      Stopwatch sw = Stopwatch.StartNew();
      bool sawDa = false;
      bool sawCell = false;

      while (sw.ElapsedMilliseconds < timeoutMs && !(sawDa && sawCell))
      {
        if (!Console.KeyAvailable)
        {
          System.Threading.Thread.Sleep(5);
          continue;
        }

        ConsoleKeyInfo k = Console.ReadKey(true);
        sb.Append(k.KeyChar);
        string s = sb.ToString();
        if (s.Contains("\x1b[?") && s.IndexOf('c', s.IndexOf("\x1b[?", StringComparison.Ordinal)) > 0) sawDa = true;
        if (s.Contains("\x1b[6;") && s.IndexOf('t', s.IndexOf("\x1b[6;", StringComparison.Ordinal)) > 0) sawCell = true;
      }

      return sb.ToString();
    }
  }
}
