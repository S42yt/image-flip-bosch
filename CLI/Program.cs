using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using image_flip_bosch.CLI.Sixel;
using image_flip_bosch.CLI.TUI;
using image_flip_bosch.ImgFlip;
using image_flip_bosch.ImgFlip.Auth;
using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using System.Text;

using image_flip_bosch.CLI.Utils;

namespace image_flip_bosch.CLI
{

  public class Program
  {
    public static async Task<int> Main(string[] args)
    {
      Console.OutputEncoding = Encoding.UTF8;
      Console.InputEncoding = Encoding.UTF8;

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

      ImgflipSession imgflip = new(new ImgFlipApi(), setup.GetCredentials);

      SixelCapabilities sixel = SixelTerminal.Probe();
      Logger.Info($"Sixel: supported={sixel.Supported} cell={sixel.CellWidth}x{sixel.CellHeight}");

      Logger.UseFile(ConfigPaths.File("app.log"));

      ConsoleWindowSystem ws = new(
        new SixelDriver(new NetConsoleDriver(RenderMode.Buffer), sixel),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      MainScreen main = new(ws, cache, imgflip, configStore, setup);
      main.Show();

      Logger.MinimumLevel = LogLevel.Error;
      //ConsoleTap.Start(ConfigPaths.File("tap.log"));
      int code = await Task.Run(ws.Run);

      Logger.UseConsole();
      Logger.Info("Application finished.");
      return code;
    }
  }
}
