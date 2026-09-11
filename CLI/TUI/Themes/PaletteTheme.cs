namespace image_flip_bosch.CLI.TUI.Themes
{
  using SharpConsoleUI.Themes;
  using System.Reflection;

  internal abstract class PaletteTheme : ThemeBase
  {
    protected PaletteTheme(string name, string description, Palette palette)
    {
      MutableTheme generated = Theme.FromPalette(palette);
      foreach (PropertyInfo source in typeof(ThemeBase).GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!source.CanRead || !source.CanWrite) continue;
        source.SetValue(this, source.GetValue(generated));
      }

      Name = name;
      Description = description;
      if (palette.Mode is ThemeMode mode) Mode = mode;
    }
  }
}
