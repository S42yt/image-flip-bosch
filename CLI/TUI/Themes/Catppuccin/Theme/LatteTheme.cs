using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Catppuccin.Theme;

internal sealed class LatteTheme : CatppuccinTheme<LatteTheme>, ICatppuccinPalette
{
  public static string ThemeName => "Latte";
  public static string ThemeDesc => "Catppuccin Latte Theme";
  public static ThemeMode ThemeMode => ThemeMode.Light;

  public static Color Red => Color.FromHex("#d20f39");
  public static Color Blue => Color.FromHex("#1e66f5");
  public static Color Green => Color.FromHex("#40a02b");
  public static Color Yellow => Color.FromHex("#df8e1d");
  public static Color Text => Color.FromHex("#4c4f69");
  public static Color Subtext1 => Color.FromHex("#5c5f77");
  public static Color Subtext0 => Color.FromHex("#6c6f85");
  public static Color Overlay2 => Color.FromHex("#7c7f93");
  public static Color Overlay1 => Color.FromHex("#8c8fa1");
  public static Color Overlay0 => Color.FromHex("#9ca0b0");
  public static Color Surface2 => Color.FromHex("#acb0be");
  public static Color Surface1 => Color.FromHex("#bcc0cc");
  public static Color Surface0 => Color.FromHex("#ccd0da");
  public static Color Base => Color.FromHex("#eff1f5");
  public static Color Mantle => Color.FromHex("#e6e9ef");
  public static Color Crust => Color.FromHex("#dce0e8");
}
