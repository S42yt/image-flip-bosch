using image_flip_bosch.CLI.Config.ImgFlip;
using image_flip_bosch.CLI.Utils;
using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Sixel;
using image_flip_bosch.CLI.TUI;
using image_flip_bosch.CLI.Utils;
using image_flip_bosch.ImgFlip;
using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using System;
using System.Threading.Tasks;

namespace image_flip_bosch.CLI
{
  public class Program
  {
    public static async Task<int> Main(string[] args)
    {
      Logger.Info("Starting the application...");

      ConfigStore<AppConfig> configStore = new();
      AppConfig config = configStore.Load();
      ImgflipSetup setup = new(configStore);

      if (args.Length > 0 && args[0] == "logout")
      {
        Logger.Info(setup.Clear() ? "Imgflip credentials removed." : "No credentials stored.");
        return 0;
      }

      if (args.Length > 0 && args[0] == "login")
      {
        if (!setup.PromptInteractive()) return 1;
      }

      await using ImageCache cache = new(
        config.Cache.Directory,
        TimeSpan.FromHours(config.Cache.TtlHours),
        TimeSpan.FromMinutes(config.Cache.SweepMinutes));

      ImgflipSession imgflip = new(new ImgFlipApi(), setup.RequireCredentials);

      SixelCapabilities sixel = SixelTerminal.Probe();
      Logger.Info($"Sixel: supported={sixel.Supported} cell={sixel.CellWidth}x{sixel.CellHeight}");

      ConsoleWindowSystem ws = new(
        new SixelDriver(new NetConsoleDriver(RenderMode.Buffer), sixel),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      MainScreen main = new(ws, cache, imgflip, configStore, setup);
      main.Show();

      int code = await Task.Run(() => ws.Run());

      Logger.Info("Application finished.");
      return code;
    }
  }
}
