namespace image_flip_bosch.CLI
{
  using image_flip_bosch.Bot.Utils;
  using System;
  using System.IO;
  using System.Threading.Tasks;
  using Terminal.Gui.App;
  using Terminal.Gui.Drawing;
  using Terminal.Gui.Input;
  using Terminal.Gui.ViewBase;
  using Terminal.Gui.Views;

  public class Program
  {
    public static async Task Main(string[] args)
    {
      Logger.Info("Starting the application...");

      await using ImageCache cache = new();

      using IApplication app = Application.Create();
      app.Init();
      using Window window = new() { BorderStyle = LineStyle.None };

      Scheme inverted = new() { Normal = new Terminal.Gui.Drawing.Attribute(StandardColor.Black, StandardColor.White) };
      Label header = new()
      {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = 1,
        TextAlignment = Alignment.Center,
      };
      header.SetScheme(inverted);

      TextView body = new()
      {
        X = 0,
        Y = 1,
        Width = Dim.Fill(),
        Height = Dim.Fill(3),
        ReadOnly = true,
        WordWrap = true,
      };

      void Say(string line)
      {
        body.Text += line + "\n";
        body.MoveEnd();
      }
      View linkScreen = new() { X = 0, Y = Pos.AnchorEnd(3), Width = Dim.Fill(), Height = 3 };

      Label prompt = new() { X = 0, Y = 0, Text = "Image URL: " };
      prompt.SetScheme(inverted);

      TextField urlField = new() { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill() };

      StatusBar linkBar = new() { Y = 1, CanFocus = false };
      linkBar.Add(
        new Shortcut { Title = "Continue", Key = Key.Enter, CanFocus = false, Action = () => OpenActions() },
        new Shortcut { Title = "Exit", Key = Key.X.WithCtrl, CanFocus = false, Action = () => app.RequestStop() }
      );

      linkScreen.Add(prompt, urlField, linkBar);
      View actionScreen = new() { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 2, Visible = false };

      StatusBar actionBar1 = new() { Y = 0, CanFocus = false };
      StatusBar actionBar2 = new() { Y = 1, CanFocus = false };

      actionBar1.Add(
        new Shortcut { Title = "Download", Key = Key.D.WithCtrl, CanFocus = false, Action = () => Download() },
        new Shortcut { Title = "Copy from cache", Key = Key.S.WithCtrl, CanFocus = false, Action = () => CopyFromCache() }
      ); ;
      actionBar2.Add(
        new Shortcut { Title = "Delete from cache", Key = Key.K.WithCtrl, CanFocus = false, Action = () => DeleteFromCache() },
        new Shortcut { Title = "Back", Key = Key.X.WithCtrl, CanFocus = false, Action = () => BackToLink() }
      );

      actionScreen.Add(actionBar1, actionBar2);

      window.Add(header, body, linkScreen, actionScreen);
      string currentUrl = "";
      bool busy = false;

      void ShowLinkScreen()
      {
        header.Text = "  image_flip_bosch  —  New image link";
        actionScreen.Visible = false;
        linkScreen.Visible = true;
        urlField.SetFocus();
      }

      void OpenActions()
      {
        string url = urlField.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
          Say("! Please enter a valid absolute URL.");
          return;
        }

        currentUrl = url;
        header.Text = $"  {url}";
        linkScreen.Visible = false;
        actionScreen.Visible = true;
        body.SetFocus();
        ReportStatus();
      }

      void BackToLink()
      {
        if (busy) { Say("! Wait for the current operation to finish."); return; }
        urlField.Text = "";
        Say("");
        ShowLinkScreen();
      }

      void ReportStatus()
      {
        string? path = cache.TryGetPath(currentUrl);
        Say(path is null
          ? "Not in cache."
          : $"In cache: {path} ({new FileInfo(path).Length / 1024} KB)");
      }
      void RunInBackground(string label, Func<Task> work)
      {
        if (busy) { Say("! Busy."); return; }
        busy = true;
        Say($"> {label}…");

        _ = Task.Run(async () =>
        {
          try
          {
            await work();
          }
          catch (Exception ex)
          {
            app.Invoke(() => Say($"! {ex.Message}"));
          }
          finally
          {
            app.Invoke(() => { busy = false; ReportStatus(); });
          }
        });
      }

      void Download()
      {
        if (cache.TryGetPath(currentUrl) is not null)
        {
          Say("Already cached – use Delete first to re-download.");
          return;
        }

        RunInBackground("Downloading", async () =>
        {
          string path = await cache.GetAsync(currentUrl);
          app.Invoke(() => Say($"Downloaded to cache: {path}"));
        });
      }

      void CopyFromCache()
      {
        string? source = cache.TryGetPath(currentUrl);
        if (source is null) { Say("! Nothing cached for this URL – download it first."); return; }

        using SaveDialog dialog = new()
        {
          Title = "Copy cached image to",
          Path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.GetFileName(new Uri(currentUrl).AbsolutePath)),
          AllowedTypes = [new AllowedType("Images", ".png", ".jpg", ".jpeg", ".gif", ".webp"), new AllowedTypeAny()],
        };
        app.Run(dialog);
        if (dialog.Canceled || string.IsNullOrWhiteSpace(dialog.Path)) { Say("Copy cancelled."); return; }

        string destination = dialog.Path;
        RunInBackground("Copying", () =>
        {
          Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
          File.Copy(source, destination, overwrite: true);
          app.Invoke(() => Say($"Copied to {destination}"));
          return Task.CompletedTask;
        });
      }

      void DeleteFromCache()
      {
        if (busy) { Say("! Busy."); return; }

        if (cache.TryGetPath(currentUrl) is null) { Say("Nothing to delete."); return; }

        int? choice = MessageBox.Query(app, "Delete", "Remove this image from the cache?", "Yes", "No");
        if (choice != 0) return;

        Say(cache.Remove(currentUrl) ? "Deleted from cache." : "! Could not delete.");
        Say("");
        ShowLinkScreen();
      }
      Say("Enter an image link below and press Enter.");
      ShowLinkScreen();

      app.Run(window);

      Logger.Info("Application finished.");
    }
  }
}
