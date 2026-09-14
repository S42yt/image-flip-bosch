using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Catppuccin.Theme;

internal sealed class MacchiatoTheme : CatppuccinTheme<MacchiatoTheme>, ICatppuccinPalette
{
  public static string ThemeName => "Macchiato";
  public static string ThemeDesc => "Catppuccin Macchiato Theme";
  public static ThemeMode ThemeMode => ThemeMode.Dark;

  public static Color Red => Color.FromHex("#ed8796");
  public static Color Blue => Color.FromHex("#8aadf4");
  public static Color Green => Color.FromHex("#a6da95");
  public static Color Yellow => Color.FromHex("#eed49f");
  public static Color Text => Color.FromHex("#cad3f5");
  public static Color Subtext1 => Color.FromHex("#b8c0e0");
  public static Color Subtext0 => Color.FromHex("#a5adcb");
  public static Color Overlay2 => Color.FromHex("#939ab7");
  public static Color Overlay1 => Color.FromHex("#8087a2");
  public static Color Overlay0 => Color.FromHex("#6e738d");
  public static Color Surface2 => Color.FromHex("#5b6078");
  public static Color Surface1 => Color.FromHex("#494d64");
  public static Color Surface0 => Color.FromHex("#363a4f");
  public static Color Base => Color.FromHex("#24273a");
  public static Color Mantle => Color.FromHex("#1e2030");
  public static Color Crust => Color.FromHex("#181926");
}
