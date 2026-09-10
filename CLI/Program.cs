namespace image_flip_bosch.CLI
{
  using image_flip_bosch.Bot.Utils;
  using System;
  using System.Threading.Tasks;
  using Terminal.Gui.App;
  using Terminal.Gui.Input;
  using Terminal.Gui.ViewBase;
  using Terminal.Gui.Views;

  public class Program
  {
    public static async Task Main(string[] args)
    {
      Logger.Info("Starting the application...");

      using IApplication app = Application.Create();
      app.Init();

      using Window window = new() { Title = "image_flip_bosch  (Esc to quit)" };

      FrameView controls = new()
      {
        Title = "Actions",
        X = 0,
        Y = 0,
        Width = 30,
        Height = Dim.Fill(1),
      };

      Button runButton = new()
      {
        Text = "_Run job",
        X = 1,
        Y = 1,
        IsDefault = true,
      };

      ProgressBar progress = new()
      {
        X = 1,
        Y = Pos.Bottom(runButton) + 1,
        Width = Dim.Fill(1),
        Fraction = 0f,
      };

      controls.Add(runButton, progress);

      FrameView logFrame = new()
      {
        Title = "Log",
        X = Pos.Right(controls),
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(1),
      };

      TextView log = new()
      {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        ReadOnly = true,
        WordWrap = true,
      };

      logFrame.Add(log);

      StatusBar statusBar = new()
      {
        Y = Pos.AnchorEnd(),
        CanFocus = false,
      };
      statusBar.Add(
        new Shortcut { Title = "Quit", Key = Key.Esc, Action = () => app.RequestStop(), CanFocus = false },
        new Shortcut { Title = "Run", Key = Key.F5, Action = () => runButton.InvokeCommand(Command.Accept), CanFocus = false }
      );

      window.Add(controls, logFrame, statusBar);

      void Append(string level, string message)
      {
        log.Text += $"[{DateTime.Now:HH:mm:ss}] {level,-5} {message}\n";
        log.MoveEnd();
      }

      bool running = false;

      runButton.Accepting += (_, e) =>
      {
        e.Handled = true;
        if (running) return;
        running = true;
        runButton.Enabled = false;

        Append("INFO", "Job started.");

        _ = Task.Run(async () =>
        {
          for (int i = 1; i <= 10; i++)
          {
            await Task.Delay(300);
            int step = i;
            app.Invoke(() =>
            {
              progress.Fraction = step / 10f;
              Append("DEBUG", $"Step {step}/10 done.");
            });
          }

          app.Invoke(() =>
          {
            Append("INFO", "Job finished.");
            running = false;
            runButton.Enabled = true;
            runButton.SetFocus();
          });
        });
      };

      Append("INFO", "Ready. Press Run or F5.");
      runButton.SetFocus();

      app.Run(window);

      Logger.Info("Application finished.");
      await Task.CompletedTask;
    }
  }
}
