using image_flip_bosch.CLI.Sixel;
using image_flip_bosch.CLI.Utils.Image;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using image_flip_bosch.CLI.Sixel.Core;

namespace image_flip_bosch.CLI.Tests
{

  public static class SixelGifTest
  {
    public static async Task<int> RunAsync(string? path)
    {
      Console.OutputEncoding = Encoding.UTF8;
      Console.InputEncoding = Encoding.UTF8;

      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        await Console.Error.WriteLineAsync("usage: image_flip_bosch giftest <file.gif>");
        return 2;
      }

      SixelCapabilities caps = SixelTerminal.Probe();
      Console.WriteLine($"sixel supported={caps.Supported} cell={caps.CellWidth}x{caps.CellHeight}");
      Console.WriteLine("Esc or F4 quits, Space pauses, R restarts.");
      await Task.Delay(800);

      ConsoleWindowSystem ws = new(
        new SixelDriver(new NetConsoleDriver(RenderMode.Buffer), caps),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      MarkupControl status = Controls.Markup("loading...").StickyBottom().Build();
      ImagePreview preview = new(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
        AnimationMaxColors = 128,
      };

      int frames = 0;
      preview.AnimationProgress += (_, p) =>
      {
        frames = p.Total;
        status.SetContent([$"encoding frame {p.Done}/{p.Total}"]);
        if (p.Done == p.Total) status.SetContent([$"{Path.GetFileName(path)}  {p.Total} frames  animated={preview.IsAnimated}"]);
      };
      preview.LoadFailed += (_, msg) => status.SetContent([$"[red]{msg}[/]"]);
      preview.EncodeFailed += (_, msg) => status.SetContent([$"[red]{msg}[/]"]);

      Window win = new WindowBuilder(ws)
        .WithTitle("giftest")
        .Frameless()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .AddControls(preview, status)
        .Build();

      bool paused = false;
      win.PreviewKeyPressed += (_, e) =>
      {
        switch (e.KeyInfo.Key)
        {
          case ConsoleKey.Escape:
          case ConsoleKey.F4:
            ws.Shutdown();
            e.Handled = true;
            break;
          case ConsoleKey.Spacebar:
            paused = !paused;
            if (paused) preview.StopAnimation();
            else _ = preview.LoadAsync(path);
            status.SetContent([paused ? "paused" : $"playing {frames} frames"]);
            e.Handled = true;
            break;
          case ConsoleKey.R:
            _ = preview.LoadAsync(path);
            e.Handled = true;
            break;
        }
      };

      ws.AddWindow(win);
      win.State = WindowState.Maximized;
      _ = preview.LoadAsync(path);

      return await Task.Run(ws.Run);
    }
  }
}
