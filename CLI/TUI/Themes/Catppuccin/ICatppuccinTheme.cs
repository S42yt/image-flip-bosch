using SharpConsoleUI;
using SharpConsoleUI.Themes;

public interface ICatppuccinPalette
{
  static abstract string ThemeName { get; }
  static abstract string ThemeDesc { get; }
  static abstract ThemeMode ThemeMode { get; }

  static abstract Color Red { get; }
  static abstract Color Blue { get; }
  static abstract Color Green { get; }
  static abstract Color Yellow { get; }
  static abstract Color Text { get; }
  static abstract Color Subtext1 { get; }
  static abstract Color Subtext0 { get; }
  static abstract Color Overlay2 { get; }
  static abstract Color Overlay1 { get; }
  static abstract Color Overlay0 { get; }
  static abstract Color Surface2 { get; }
  static abstract Color Surface1 { get; }
  static abstract Color Surface0 { get; }
  static abstract Color Base { get; }
  static abstract Color Mantle { get; }
  static abstract Color Crust { get; }
}
