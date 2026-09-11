using SharpConsoleUI;
using SharpConsoleUI.Themes;
using System;
using System.Collections.Generic;
using System.Text;

namespace image_flip_bosch.CLI.Config.Themes
{
  public class BoschTheme : ThemeBase
  {
    public override string Name { get; set; } = "BoschTheme";
    public override string Description { get; set; } = "Offiziele Themen von Bosch";
    public override Color WindowBackgroundColor { get; set; } = Color.Wheat4;

    public override Color WindowForegroundColor { get; set; } = Color.IndianRed;
  }
}
