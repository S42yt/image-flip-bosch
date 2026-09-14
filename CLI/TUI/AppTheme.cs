using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.TUI.Themes;
using image_flip_bosch.CLI.TUI.Themes.Bosch;
using image_flip_bosch.CLI.TUI.Themes.Catppuccin;
using image_flip_bosch.CLI.TUI.Themes.Catppuccin.Theme;
using image_flip_bosch.CLI.TUI.Themes.ImgFlip;
using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI
{

  internal static class AppThemes
  {
    private const string DefaultName = BoschTheme.ThemeName;

    private static readonly (string Name, string Description, Func<ITheme> Factory)[] Custom =
    [
      (BoschTheme.ThemeName, "Bosch red on anthracite", () => new BoschTheme()),
      (BoschLightTheme.ThemeName, "Bosch red on light grey", () => new BoschLightTheme()),
      (ImgFlipTheme.ThemeName, "Imgflip blue on dark", () => new ImgFlipTheme()),
      (LatteTheme.ThemeName, LatteTheme.ThemeDesc, () => new LatteTheme()),
      (FrappeTheme.ThemeName, FrappeTheme.ThemeDesc, () => new FrappeTheme()),
      (MacchiatoTheme.ThemeName, MacchiatoTheme.ThemeDesc, () => new MacchiatoTheme()),
      (MochaTheme.ThemeName, MochaTheme.ThemeDesc, () => new MochaTheme()),
    ];

    private static void Register(ConsoleWindowSystem ws)
    {
      ThemeRegistryStateService registry = ws.ThemeRegistryService;
      foreach ((string name, string description, Func<ITheme> factory) in Custom)
      {
        if (!registry.IsThemeRegistered(name))
          registry.RegisterTheme(name, description, factory);
      }
    }

    public static IReadOnlyList<string> Names(ConsoleWindowSystem ws)
    {
      var ordered = Custom.Select(c => c.Name).ToList();
      ordered.AddRange(ws.ThemeRegistryService.GetAvailableThemeNames().Where(n => !ordered.Contains(n)));
      return ordered;
    }

    public static string Current(ConsoleWindowSystem ws) => ws.Theme.Name;

    public static bool Apply(ConsoleWindowSystem ws, string? name)
    {
      string target = string.IsNullOrWhiteSpace(name) ? DefaultName : name;
      ITheme? theme = ws.ThemeRegistryService.GetTheme(target);
      if (theme is null)
      {
        return target != DefaultName && Apply(ws, DefaultName);
      }

      ws.ThemeStateService.SetTheme(theme);
      return true;
    }

    public static string Next(ConsoleWindowSystem ws, bool backward = false)
    {
      IReadOnlyList<string> names = Names(ws);
      int index = names.ToList().FindIndex(n => string.Equals(n, Current(ws), StringComparison.OrdinalIgnoreCase));
      int next = index < 0 ? 0 : (index + (backward ? names.Count - 1 : 1)) % names.Count;
      Apply(ws, names[next]);
      return names[next];
    }

    public static string ApplyFromConfig(ConsoleWindowSystem ws, ConfigStore<AppConfig> config)
    {
      Register(ws);
      string wanted = config.Load().Theme ?? DefaultName;
      if (!Apply(ws, wanted) || !string.Equals(Current(ws), wanted, StringComparison.OrdinalIgnoreCase))
        config.Update(c => c.Theme = Current(ws));
      return Current(ws);
    }

    public static void Save(ConfigStore<AppConfig> config, string name) => config.Update(c => c.Theme = name);
  }
}
