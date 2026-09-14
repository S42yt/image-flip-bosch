using image_flip_bosch.CLI.Sixel;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using System.Text;
using image_flip_bosch.CLI.Sixel.Core;
using image_flip_bosch.CLI.Sixel.Video;

namespace image_flip_bosch.CLI.Tests
{
  public static class SixelVideoTest
  {
    public static async Task<int> RunAsync(string? path, int fps, int colors)
    {
      Console.OutputEncoding = Encoding.UTF8;
      Console.InputEncoding = Encoding.UTF8;

      if (string.IsNullOrWhiteSpace(path) || (!SixelVideoStream.IsUrl(path) && !File.Exists(path)))
      {
        await Console.Error.WriteLineAsync("usage: image_flip_bosch vidtest <file.mp4> [fps] [colors]");
        await Console.Error.WriteLineAsync("       image_flip_bosch stream <youtube/twitch url> [fps] [colors]");
        return 2;
      }

      SixelCapabilities caps = SixelTerminal.Probe();
      Console.WriteLine($"sixel supported={caps.Supported} cell={caps.CellWidth}x{caps.CellHeight}");
      Console.WriteLine("Esc or F4 quits, Space pauses, R restarts, +/- changes fps, M mutes.");
      await Task.Delay(800);

      SixelDriver driver = new(new NetConsoleDriver(RenderMode.Buffer), caps);
      ConsoleWindowSystem ws = new(driver, options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      MarkupControl status = Controls.Markup("starting ffmpeg...").StickyBottom().Build();
      SixelVideoControl video = new()
      {
        MaxColors = colors,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };

      string name = SixelVideoStream.IsUrl(path) ? new Uri(path).Host : Path.GetFileName(path);
      video.Progress += (_, p) =>
      {
        (double termFps, double mbps) = driver.Stats;
        status.SetContent([
          $"{name}  {p.Elapsed:mm\\:ss}  decode {p.Fps:F1}/{fps} fps  terminal {termFps:F1} fps {mbps:F1} MB/s  dropped {p.Dropped}  {colors} colors  {(video.AudioAvailable ? (video.Muted ? "muted" : "sound") : "no audio on this OS")}{(p.Finished ? "  [end]" : string.Empty)}"]);
      };
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
          case ConsoleKey.M:
            video.Muted = !video.Muted;
            status.SetContent([video.Muted ? "muted" : "sound on"]);
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
