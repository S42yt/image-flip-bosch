using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Catppuccin.Theme;

internal sealed class FrappeTheme : CatppuccinTheme<FrappeTheme>, ICatppuccinPalette
{
  public static string ThemeName => "Frappe";
  public static string ThemeDesc => "Catppuccin Frappe Theme";
  public static ThemeMode ThemeMode => ThemeMode.Dark;

  public static Color Red => Color.FromHex("#e78284");
  public static Color Blue => Color.FromHex("#8caaee");
  public static Color Green => Color.FromHex("#a6d189");
  public static Color Yellow => Color.FromHex("#e5c890");
  public static Color Text => Color.FromHex("#c6d0f5");
  public static Color Subtext1 => Color.FromHex("#b5bfe2");
  public static Color Subtext0 => Color.FromHex("#a5adce");
  public static Color Overlay2 => Color.FromHex("#949cbb");
  public static Color Overlay1 => Color.FromHex("#838ba7");
  public static Color Overlay0 => Color.FromHex("#737994");
  public static Color Surface2 => Color.FromHex("#626880");
  public static Color Surface1 => Color.FromHex("#51576d");
  public static Color Surface0 => Color.FromHex("#414559");
  public static Color Base => Color.FromHex("#303446");
  public static Color Mantle => Color.FromHex("#292c3c");
  public static Color Crust => Color.FromHex("#232634");
}
