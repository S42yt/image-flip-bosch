namespace image_flip_bosch.CLI
{
  using image_flip_bosch.Bot.Utils;
  using image_flip_bosch.CLI.Utils;
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

      string currentUrl = string.Empty;
      bool busy = false;
      bool onActionScreen = false;

      using Window window = new() { BorderStyle = LineStyle.None };

      Scheme inverted = new() { Normal = new Terminal.Gui.Drawing.Attribute(StandardColor.Black, StandardColor.White) };

      Label header = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, TextAlignment = Alignment.Center };
      header.SetScheme(inverted);

      ImagePreview preview = new()
      {
        X = 0,
        Y = 1,
        Width = Dim.Percent(60),
        Height = Dim.Fill(3),
      };

      TextView log = new()
      {
        X = Pos.Right(preview),
        Y = 1,
        Width = Dim.Fill(),
        Height = Dim.Fill(3),
        ReadOnly = true,
        WordWrap = true,
      };

      Label prompt = new() { X = 0, Y = Pos.AnchorEnd(3), Text = "Image URL: " };
      prompt.SetScheme(inverted);
      TextField urlField = new() { X = Pos.Right(prompt), Y = Pos.AnchorEnd(3), Width = Dim.Fill() };

      Label hintRow1 = new() { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill() };
      Label hintRow2 = new() { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

      window.Add(header, preview, log, prompt, urlField, hintRow1, hintRow2);
      preview.LoadFailed += (_, msg) => Say($"! Could not render image: {msg}");

      void Say(string line)
      {
        log.Text += line + "\n";
        log.MoveEnd();
      }

      void ShowLinkScreen()
      {
        onActionScreen = false;
        header.Text = "image_flip_bosch - New image link";
        prompt.Visible = true;
        urlField.Visible = true;
        urlField.Text = string.Empty;
        preview.Clear();
        hintRow1.Text = "Enter  Continue";
        hintRow2.Text = "^X     Exit";
        urlField.SetFocus();
      }

      void ShowActionScreen()
      {
        onActionScreen = true;
        header.Text = currentUrl;
        prompt.Visible = false;
        urlField.Visible = false;
        hintRow1.Text = "^C  Copy to clipboard   ^S  Save as...";
        hintRow2.Text = "^K  Delete from cache   ^X  Back";
        log.SetFocus();
        ReportStatus();
        if (cache.TryGetPath(currentUrl) is null)
          Download();
      }

      void ReportStatus()
      {
        string? path = cache.TryGetPath(currentUrl);
        if (path is null)
        {
          preview.Clear();
          Say("Not in cache.");
          return;
        }

        Say($"In cache: {path} ({new FileInfo(path).Length / 1024} KB)");
        if (preview.CurrentPath != path)
          _ = preview.LoadWhenReadyAsync(app, path);
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
        ShowActionScreen();
      }

      void RunInBackground(string label, Func<Task> work)
      {
        if (busy) { Say("! Busy."); return; }
        busy = true;
        Say($"> {label}...");

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
        RunInBackground("Downloading", async () =>
        {
          string path = await cache.GetAsync(currentUrl);
          app.Invoke(() => Say($"Downloaded to cache: {path}"));
          await preview.LoadWhenReadyAsync(app, path);
        });
      }

      void CopyToClipboard()
      {
        string? source = cache.TryGetPath(currentUrl);
        if (source is null) { Say("! Image is not cached yet."); return; }

        RunInBackground("Copying to clipboard", async () =>
        {
          bool ok = await ImageClipboard.CopyFileAsync(source);
          if (!ok && app.Clipboard is { IsSupported: true } clip && clip.TrySetClipboardData(source))
          {
            app.Invoke(() => Say("Image clipboard unavailable, copied file path instead."));
            return;
          }
          app.Invoke(() => Say(ok ? "Image copied to clipboard." : "! Could not copy to clipboard."));
        });
      }

      void SaveAs()
      {
        string? source = cache.TryGetPath(currentUrl);
        if (source is null) { Say("! Image is not cached yet."); return; }

        using SaveDialog dialog = new()
        {
          Title = "Save image as",
          Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            System.IO.Path.GetFileName(new Uri(currentUrl).AbsolutePath)),
          AllowedTypes = [new AllowedType("Images", ".png", ".jpg", ".jpeg", ".gif", ".webp"), new AllowedTypeAny()],
        };
        app.Run(dialog);
        if (dialog.Canceled || string.IsNullOrWhiteSpace(dialog.Path)) { Say("Save cancelled."); return; }

        string destination = dialog.Path;
        RunInBackground("Saving", () =>
        {
          Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
          File.Copy(source, destination, overwrite: true);
          app.Invoke(() => Say($"Saved to {destination}"));
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
        ShowLinkScreen();
      }

      void BackToLink()
      {
        if (busy) { Say("! Wait for the current operation to finish."); return; }
        ShowLinkScreen();
      }

      app.Keyboard.KeyDown += (_, key) =>
      {
        if (app.TopRunnableView != window) return;

        if (!onActionScreen)
        {
          if (key == Key.Enter) { OpenActions(); key.Handled = true; }
          else if (key == Key.X.WithCtrl) { app.RequestStop(); key.Handled = true; }
          return;
        }

        if (key == Key.C.WithCtrl) { CopyToClipboard(); key.Handled = true; }
        else if (key == Key.S.WithCtrl) { SaveAs(); key.Handled = true; }
        else if (key == Key.K.WithCtrl) { DeleteFromCache(); key.Handled = true; }
        else if (key == Key.X.WithCtrl) { BackToLink(); key.Handled = true; }
      };

      Say("Enter an image link below and press Enter.");
      ShowLinkScreen();

      app.Run(window);

      Logger.Info("Application finished.");
    }
  }
}
