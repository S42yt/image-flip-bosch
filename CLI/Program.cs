using image_flip_bosch.Bot.Utils;
using image_flip_bosch.CLI.Utils;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Dialogs;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Imaging;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using System;
using System.IO;
using System.Threading.Tasks;


namespace image_flip_bosch.CLI
{

  public class Program
  {
    public static async Task<int> Main(string[] args)
    {
      Logger.Info("Starting the application...");

      await using ImageCache cache = new();

      ConsoleWindowSystem ws = new(
        new NetConsoleDriver(RenderMode.Buffer),
        options: new ConsoleWindowSystemOptions(TargetFPS: 60));

      string currentUrl = string.Empty;
      string? currentPath = null;
      bool busy = false;

      MarkupControl log = Controls.Markup("[dim]Enter an image URL and press Enter.[/]").Build();

      ScrollablePanelControl logPanel = Controls.ScrollablePanel()
        .AddControl(log)
        .WithAutoScroll()
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      ImageControl preview = Controls.Image()
        .Fit()
        .WithAlignment(HorizontalAlignment.Center)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      GridControl content = Controls.Grid()
        .Columns(GridLength.Star(3), GridLength.Star(2))
        .Rows(GridLength.Star())
        .ColumnGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      content.Place(preview, 0, 0);
      content.Place(logPanel, 0, 1);
      content.Cell(0, 0).Border = BorderStyle.Rounded;
      content.Cell(0, 1).Border = BorderStyle.Rounded;

      PromptControl urlPrompt = Controls.Prompt(" URL ")
        .WithPlaceholder("https://example.com/image.png")
        .UnfocusOnEnter(false)
        .StickyTop()
        .Build();

      Window win = new WindowBuilder(ws)
        .WithTitle("image_flip_bosch")
        .HideTitleButtons()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(urlPrompt, content)
        .Build();


      StatusBarControl statusBar = Controls.StatusBar()
        .AddLeft("^C", "Copy", () => _ = CopyToClipboardAsync())
        .AddLeft("^S", "Save as", () => _ = SaveAsAsync())
        .AddLeft("^K", "Delete", () => _ = DeleteAsync())
        .AddRight("^X", "Quit", () => ws.Shutdown())
        .StickyBottom()
        .Build();

      win.AddControl(statusBar);



      void Say(string markup) => ws.InvokeAsync(() => log.AppendLine($"[dim]{DateTime.Now:HH:mm:ss}[/] {markup}"));

      void Toast(string message, NotificationSeverity severity) =>
        ws.InvokeAsync(() => ws.ToastService.Show(message, severity));

      async Task LoadPreviewAsync(string path)
      {
        try
        {
          PixelBuffer pixels = await Task.Run(() => PixelBuffer.FromFile(path));
          await ws.InvokeAsync(() => preview.Source = pixels);
          currentPath = path;
        }
        catch (Exception ex)
        {
          Say($"[red]Could not render image:[/] {MarkupParser.Escape(ex.Message)}");
        }
      }

      async Task OpenUrlAsync(string url)
      {
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
          Say("[yellow]Please enter a valid absolute URL.[/]");
          return;
        }
        if (busy) { Say("[yellow]Busy, wait for the current operation.[/]"); return; }

        busy = true;
        currentUrl = url;
        currentPath = null;
        await ws.InvokeAsync(() => preview.Source = null);

        try
        {
          string? cached = cache.TryGetPath(url);
          if (cached is null)
          {
            Say($"Downloading [cyan]{MarkupParser.Escape(url)}[/]");
            cached = await cache.GetAsync(url);
            Say($"[green]Cached:[/] {MarkupParser.Escape(cached)}");
          }
          else
          {
            Say($"[green]In cache:[/] {MarkupParser.Escape(cached)}");
          }
          await LoadPreviewAsync(cached);
        }
        catch (Exception ex)
        {
          Say($"[red]Download failed:[/] {MarkupParser.Escape(ex.Message)}");
          Toast("Download failed", NotificationSeverity.Danger);
        }
        finally
        {
          busy = false;
        }
      }

      async Task CopyToClipboardAsync()
      {
        if (currentPath is null) { Say("[yellow]Nothing to copy yet.[/]"); return; }

        bool ok = await ImageClipboard.CopyFileAsync(currentPath);
        if (ok)
        {
          Toast("Image copied to clipboard", NotificationSeverity.Success);
          Say("Image copied to clipboard.");
          return;
        }

        ClipboardHelper.SetText(currentPath);
        Toast("Copied file path instead", NotificationSeverity.Warning);
        Say("Image clipboard unavailable, copied the file path.");
      }

      async Task SaveAsAsync()
      {
        if (currentPath is null) { Say("[yellow]Nothing to save yet.[/]"); return; }

        string defaultName = Path.GetFileName(new Uri(currentUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(defaultName)) defaultName = Path.GetFileName(currentPath);

        string? target = await FileDialogs.ShowSaveFileAsync(
          ws,
          Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
          "*.png;*.jpg;*.jpeg;*.gif;*.webp",
          defaultName,
          win);

        if (target is null) { Say("Save cancelled."); return; }

        try
        {
          Directory.CreateDirectory(Path.GetDirectoryName(target)!);
          File.Copy(currentPath, target, overwrite: true);
          Say($"[green]Saved:[/] {MarkupParser.Escape(target)}");
          Toast("Saved", NotificationSeverity.Success);
        }
        catch (Exception ex)
        {
          Say($"[red]Save failed:[/] {MarkupParser.Escape(ex.Message)}");
        }
      }

      async Task DeleteAsync()
      {
        if (currentPath is null) { Say("[yellow]Nothing to delete.[/]"); return; }

        bool confirmed = await Dialogs.ConfirmAsync(
          ws, "Delete", "Remove this image from the cache?", "Delete", "Cancel",
          NotificationSeverityEnum.Warning, win);
        if (!confirmed) return;

        bool removed = cache.Remove(currentUrl);
        Say(removed ? "[green]Deleted from cache.[/]" : "[red]Could not delete.[/]");
        currentPath = null;
        currentUrl = string.Empty;
        await ws.InvokeAsync(() =>
        {
          preview.Source = null;
          urlPrompt.Input = string.Empty;
          win.FocusControl(urlPrompt);
        });
      }

      urlPrompt.Entered += (_, text) => _ = OpenUrlAsync(text);

      win.PreviewKeyPressed += (_, e) =>
      {
        if (!e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)) return;
        switch (e.KeyInfo.Key)
        {
          case ConsoleKey.C: _ = CopyToClipboardAsync(); e.Handled = true; break;
          case ConsoleKey.S: _ = SaveAsAsync(); e.Handled = true; break;
          case ConsoleKey.K: _ = DeleteAsync(); e.Handled = true; break;
          case ConsoleKey.X: ws.Shutdown(); e.Handled = true; break;
        }
      };

      ws.AddWindow(win);
      win.State = WindowState.Maximized;
      win.FocusControl(urlPrompt);

      int code = await Task.Run(() => ws.Run());

      Logger.Info("Application finished.");
      return code;
    }
  }
}
