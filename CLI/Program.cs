using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using image_flip_bosch.CLI.Sixel;
using image_flip_bosch.CLI.TUI;
using image_flip_bosch.CLI.Utils;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.Backend;
using image_flip_bosch.ImgFlip;
using image_flip_bosch.ImgFlip.Auth;
using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using System.Text;

namespace image_flip_bosch.CLI
{
  public abstract class Program
  {
    public static async Task<int> Main(string[] args)
    {
      Console.OutputEncoding = Encoding.UTF8;
      Console.InputEncoding = Encoding.UTF8;

      bool showDebug = args.Any(a => a.Equals("-DShowDebugScreen", StringComparison.OrdinalIgnoreCase) || a == "--debug");
      AppOptions appOptions = new(showDebug);

      Logger.Info("Starting the application...");

      ConfigStore<AppConfig> configStore = new();
      AppConfig config = configStore.Load();
      config.ApplyProxy();
      ImgflipSetup setup = new(configStore);

      switch (args.Length)
      {
        case > 0 when args[0] == "serve":
          return await BackendHost.RunAsync(args[1..]);
        case > 0 when args[0] is "vidtest" or "stream":
          return await Tests.SixelVideoTest.RunAsync(args.Length > 1 ? args[1] : null, args.Length > 2 && int.TryParse(args[2], out int fps) ? fps : 24, args.Length > 3 && int.TryParse(args[3], out int colors) ? colors : 128);
        case > 0 when args[0] == "giftest":
          return await Tests.SixelGifTest.RunAsync(args.Length > 1 ? args[1] : null);
        case > 0 when args[0] == "logout":
          Logger.Info(setup.Clear() ? "Imgflip credentials removed." : "No credentials stored.");
          return 0;
        case > 0 when args[0] == "login" && !setup.PromptInteractive() && !setup.PromptInteractive():
          return 1;
      }

      await using ImageCache cache = new(
        config.Cache.Directory,
        TimeSpan.FromHours(config.Cache.TtlHours),
        TimeSpan.FromMinutes(config.Cache.SweepMinutes));

      ImgflipSession imgflip = new(new ImgFlipApi(), setup.GetCredentials);

      SixelCapabilities sixel = SixelTerminal.Probe();
      Logger.Info($"Sixel: supported={sixel.Supported} cell={sixel.CellWidth}x{sixel.CellHeight}");

      Logger.UseFile(ConfigPaths.File("app.log"));
      Logger.MinimumLevel = showDebug ? LogLevel.Debug : LogLevel.Info;

      ConsoleWindowSystem ws = new(
        new SixelDriver(new NetConsoleDriver(RenderMode.Buffer), sixel),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60, DirtyTrackingMode: DirtyTrackingMode.Cell));

      string theme = AppThemes.ApplyFromConfig(ws, configStore);
      Logger.Info($"Theme: {theme}");

      MainScreen main = new(ws, cache, imgflip, configStore, setup, appOptions);
      main.Show();

      int code = await Task.Run(ws.Run);
      Logger.MinimumLevel = LogLevel.Error;
      Logger.UseConsole();
      //Logger.Info("Application finished.");
      cache.Clear();
      
      return code;
    }
  }
}
