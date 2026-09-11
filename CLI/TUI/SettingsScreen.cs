using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Drivers;

namespace image_flip_bosch.CLI.TUI;

public class SettingsScreen
{
  private static readonly NetConsoleDriver Driver = new NetConsoleDriver(RenderMode.Buffer);
  private static ConsoleWindowSystem window = new ConsoleWindowSystem(Driver);

  private Window? settingsWindow = null;
}
