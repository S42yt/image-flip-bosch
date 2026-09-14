using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Catppuccin.Theme;

internal sealed class MochaTheme : CatppuccinTheme<MochaTheme>, ICatppuccinPalette
{
  public static string ThemeName => "Mocha";
  public static string ThemeDesc => "Catppuccin Mocha Theme";
  public static ThemeMode ThemeMode => ThemeMode.Dark;

  public static Color Red => Color.FromHex("#f38ba8");
  public static Color Blue => Color.FromHex("#89b4fa");
  public static Color Green => Color.FromHex("#a6e3a1");
  public static Color Yellow => Color.FromHex("#f9e2af");
  public static Color Text => Color.FromHex("#cdd6f4");
  public static Color Subtext1 => Color.FromHex("#bac2de");
  public static Color Subtext0 => Color.FromHex("#a6adc8");
  public static Color Overlay2 => Color.FromHex("#9399b2");
  public static Color Overlay1 => Color.FromHex("#7f849c");
  public static Color Overlay0 => Color.FromHex("#6c7086");
  public static Color Surface2 => Color.FromHex("#585b70");
  public static Color Surface1 => Color.FromHex("#45475a");
  public static Color Surface0 => Color.FromHex("#313244");
  public static Color Base => Color.FromHex("#1e1e2e");
  public static Color Mantle => Color.FromHex("#181825");
  public static Color Crust => Color.FromHex("#11111b");
}
