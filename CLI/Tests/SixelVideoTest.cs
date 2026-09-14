using image_flip_bosch.CLI.Sixel;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using System.Text;

namespace image_flip_bosch.CLI.Tests
{
  public static class SixelVideoTest
  {
    public static async Task<int> RunAsync(string? path, int fps, int colors)
    {
      Console.OutputEncoding = Encoding.UTF8;
      Console.InputEncoding = Encoding.UTF8;

      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        await Console.Error.WriteLineAsync("usage: image_flip_bosch vidtest <file.mp4> [fps] [colors]");
        return 2;
      }

      SixelCapabilities caps = SixelTerminal.Probe();
      Console.WriteLine($"sixel supported={caps.Supported} cell={caps.CellWidth}x{caps.CellHeight}");
      Console.WriteLine("Esc or F4 quits, Space pauses, R restarts, +/- changes fps.");
      await Task.Delay(800);

      ConsoleWindowSystem ws = new(
        new SixelDriver(new NetConsoleDriver(RenderMode.Buffer), caps),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      MarkupControl status = Controls.Markup("starting ffmpeg...").StickyBottom().Build();
      SixelVideoControl video = new()
      {
        MaxColors = colors,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };

      string name = Path.GetFileName(path);
      video.Progress += (_, p) => status.SetContent([
        $"{name}  {p.Elapsed:mm\\:ss}  frame {p.Frame}  dropped {p.Dropped}  {p.Fps:F1} fps shown / {fps} target  {colors} colors{(p.Finished ? "  [end]" : string.Empty)}"]);
      video.PlaybackFailed += (_, msg) => status.SetContent([$"[red]{msg}[/]"]);

      Window win = new WindowBuilder(ws)
        .WithTitle("vidtest")
        .Frameless()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .AddControls(video, status)
        .Build();

      win.PreviewKeyPressed += (_, e) =>
      {
        switch (e.KeyInfo.Key)
        {
          case ConsoleKey.Escape:
          case ConsoleKey.F4:
            video.Stop();
            ws.Shutdown();
            e.Handled = true;
            break;
          case ConsoleKey.Spacebar:
            if (video.IsPlaying) video.Pause(); else video.Resume();
            status.SetContent([video.IsPlaying ? "playing" : $"paused at {video.Position:mm\\:ss}"]);
            e.Handled = true;
            break;
          case ConsoleKey.R:
            video.Play(path, fps);
            e.Handled = true;
            break;
          case ConsoleKey.Add or ConsoleKey.OemPlus:
            fps = Math.Min(30, fps + 2);
            video.Play(path, fps, video.Position);
            e.Handled = true;
            break;
          case ConsoleKey.Subtract or ConsoleKey.OemMinus:
            fps = Math.Max(1, fps - 2);
            video.Play(path, fps, video.Position);
            e.Handled = true;
            break;
        }
      };

      ws.AddWindow(win);
      win.State = WindowState.Maximized;
      video.Play(path, fps);

      return await Task.Run(ws.Run);
    }
  }
}
