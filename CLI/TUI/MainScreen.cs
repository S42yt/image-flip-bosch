using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.Native;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;

namespace image_flip_bosch.CLI.TUI
{

  internal sealed class MainScreen
  {
    private readonly ConsoleWindowSystem _ws;
    private readonly ImageCache _cache;
    private readonly ImgflipSession _imgflip;
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly ImgflipSetup _setup;

    private readonly PromptControl _filter;
    private readonly ListControl _templates;
    private readonly ImagePreview _preview;
    private readonly MarkupControl _details;
    private readonly MarkupControl _log;
    private readonly Window _window;

    private Meme[] _allMemes = Array.Empty<Meme>();
    private Meme? _selected;
    private string? _resultUrl;
    private string[]? _lastCaptions;
    private bool _busy;
    private CancellationTokenSource? _previewCts;

    public MainScreen(ConsoleWindowSystem ws, ImageCache cache, ImgflipSession imgflip, ConfigStore<AppConfig> configStore, ImgflipSetup setup)
    {
      _ws = ws;
      _cache = cache;
      _imgflip = imgflip;
      _configStore = configStore;
      _setup = setup;

      _filter = Controls.Prompt(" Filter ")
        .WithPlaceholder("type to filter templates")
        .UnfocusOnEnter(false)
        .OnInputChanged((_, text) => ApplyFilter(text))
        .Build();

      _templates = Controls.List("Templates")
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .WithAlignment(HorizontalAlignment.Stretch)
        .OnSelectedItemChanged((_, item) => OnTemplateSelected(item))
        .OnItemActivated((_, _) => _ = OpenCaptionsAsync())
        .Build();

      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Log($"[red]Preview failed:[/] {MarkupParser.Escape(msg)}");

      _details = Controls.Markup("[dim]No template selected.[/]").Build();
      _log = Controls.Markup("[dim]Loading templates...[/]").Build();

      ScrollablePanelControl logPanel = Controls.ScrollablePanel()
        .AddControl(_log)
        .WithAutoScroll()
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      GridControl side = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Auto(), GridLength.Star())
        .RowGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      side.Place(_details, 0, 0);
      side.Place(logPanel, 1, 0);

      GridControl content = Controls.Grid()
        .Columns(GridLength.Star(1), GridLength.Star(2), GridLength.Star(1))
        .Rows(GridLength.Star())
        .ColumnGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      content.Place(_templates, 0, 0);
      content.Place(_preview, 0, 1);
      content.Place(side, 0, 2);
      content.Cell(0, 0).Border = BorderStyle.Rounded;
      content.Cell(0, 1).Border = BorderStyle.Rounded;
      content.Cell(0, 2).Border = BorderStyle.Rounded;

      StatusBarControl statusBar = Controls.StatusBar()
        .AddLeft("F5", "Caption", () => _ = OpenCaptionsAsync())
        .AddLeft("F6", "Copy URL", () => CopyResultUrl())
        .AddLeft("F7", "Save image", () => _ = SaveCurrentAsync())
        .AddLeft("F8", "Settings", () => OpenSettings())
        .AddLeft("F9", "Reload", () => _ = LoadTemplatesAsync())
        .AddLeft("F3", "Theme", () => CycleTheme(false))
        .AddRight("F10", "Quit", () => _ws.Shutdown())
        .StickyBottom()
        .Build();

      _window = new WindowBuilder(ws)
        .WithTitle("image_flip_bosch")
        .HideTitleButtons()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(_filter, content, statusBar)
        .Build();

      _window.PreviewKeyPressed += OnKey;
    }

    public void Show()
    {
      _ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.S, () => _ = SaveCurrentAsync());
      _ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.O, () => OpenSettings());
      _ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.R, () => _ = LoadTemplatesAsync());
      _ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.X, () => _ws.Shutdown());

      _ws.AddWindow(_window);
      _window.State = WindowState.Maximized;
      _window.FocusControl(_filter);
      if (!_setup.IsConfigured)
        Log("[dim]No Imgflip login. Memes get the imgflip watermark; add an account under ^O to change settings.[/]");
      _ = LoadTemplatesAsync();
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
      bool ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.F5: _ = OpenCaptionsAsync(); e.Handled = true; break;
        case ConsoleKey.F6: CopyResultUrl(); e.Handled = true; break;
        case ConsoleKey.F7: _ = SaveCurrentAsync(); e.Handled = true; break;
        case ConsoleKey.F8: OpenSettings(); e.Handled = true; break;
        case ConsoleKey.F9: _ = LoadTemplatesAsync(); e.Handled = true; break;
        case ConsoleKey.F3: CycleTheme(e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift)); e.Handled = true; break;
        case ConsoleKey.F10: _ws.Shutdown(); e.Handled = true; break;
        case ConsoleKey.C when ctrl: CopyResultUrl(); e.Handled = true; break;
        case ConsoleKey.S when ctrl: _ = SaveCurrentAsync(); e.Handled = true; break;
        case ConsoleKey.R when ctrl: _ = LoadTemplatesAsync(); e.Handled = true; break;
        case ConsoleKey.O when ctrl: OpenSettings(); e.Handled = true; break;
        case ConsoleKey.X when ctrl: _ws.Shutdown(); e.Handled = true; break;
      }
    }

    private void CycleTheme(bool backward)
    {
      string name = AppThemes.Next(_ws, backward);
      AppThemes.Save(_configStore, name);
      Toast($"Theme: {name}", NotificationSeverity.Info);
    }

    private void OpenSettings() =>
      new SettingsScreen(_ws, _configStore, _setup, _window, () => Log(_setup.IsConfigured
        ? $"Logged in as [cyan]{MarkupParser.Escape(_setup.Username!)}[/]."
        : "[yellow]No Imgflip login. Browsing only.[/]")).Show();

    private void Log(string markup) =>
      _ws.InvokeAsync(() => _log.AppendLine($"[dim]{DateTime.Now:HH:mm:ss}[/] {markup}"));

    private void Toast(string message, NotificationSeverity severity) =>
      _ws.InvokeAsync(() => _ws.ToastService.Show(message, severity));

    private async Task LoadTemplatesAsync()
    {
      try
      {
        _allMemes = await _imgflip.GetMemes();
        Log($"Loaded [cyan]{_allMemes.Length}[/] templates.");
        await _ws.InvokeAsync(() => ApplyFilter(_filter.Input));
      }
      catch (Exception ex)
      {
        Log($"[red]Could not load templates:[/] {MarkupParser.Escape(ex.Message)}");
        Toast("Loading templates failed", NotificationSeverity.Danger);
      }
    }

    private void ApplyFilter(string text)
    {
      string needle = text.Trim();
      IEnumerable<Meme> visible = string.IsNullOrEmpty(needle)
        ? _allMemes
        : _allMemes.Where(m => m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));

      List<ListItem> items = visible
        .Select(meme => new ListItem($"{MarkupParser.Escape(meme.Name)} [dim]({meme.BoxCount})[/]") { Tag = meme })
        .ToList();

      _templates.Items = items;
      if (items.Count > 0)
        _templates.SelectedIndex = 0;
    }

    private void OnTemplateSelected(ListItem? item)
    {
      if (item?.Tag is not Meme meme || ReferenceEquals(meme, _selected)) return;

      _selected = meme;
      _resultUrl = null;
      _lastCaptions = null;
      _details.SetContent(
      [
        $"[bold]{MarkupParser.Escape(meme.Name)}[/]",
        $"ID [cyan]{meme.Id}[/]",
        $"{meme.Width}x{meme.Height}, {meme.BoxCount} text boxes",
        "[dim]F5 or Enter to caption, F7 to save[/]",
      ]);

      _previewCts?.Cancel();
      CancellationTokenSource cts = new();
      _previewCts = cts;
      _ = LoadTemplatePreviewAsync(meme, cts.Token);
    }

    private async Task LoadTemplatePreviewAsync(Meme meme, CancellationToken ct)
    {
      try
      {
        await Task.Delay(150, ct);
        string path = await _cache.GetAsync(meme.Url, ct);
        if (ct.IsCancellationRequested) return;
        await _preview.LoadWhenReadyAsync(path, ct: ct);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Log($"[red]Template download failed:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }

    private async Task OpenCaptionsAsync()
    {
      if (_busy) { Log("[yellow]Busy.[/]"); return; }
      if (_selected is null) { Log("[yellow]Select a template first.[/]"); return; }

      Meme meme = _selected;
      string[]? texts = await new CaptionScreen(_ws, meme, _lastCaptions).ShowAsync();
      if (texts is null) return;

      _lastCaptions = texts;
      await CreateMemeAsync(meme, texts);
    }

    private async Task CreateMemeAsync(Meme meme, string[] texts)
    {
      if (_busy) { Log("[yellow]Busy.[/]"); return; }

      _busy = true;
      Log($"Creating meme with [cyan]{MarkupParser.Escape(meme.Name)}[/]...");

      try
      {
        ImgFlipConfig options = _configStore.Load().ImgFlip;
        _previewCts?.Cancel();

        string url;
        if (texts.Length <= 2)
        {
          url = await _imgflip.CaptionImage(
            meme.Id,
            texts.ElementAtOrDefault(0) ?? string.Empty,
            texts.ElementAtOrDefault(1) ?? string.Empty,
            options.MaxFontSize,
            options.NoWatermark && _imgflip.IsAuthenticated ? true : null);
        }
        else
        {
          MemeCreationBox[] boxes = texts
            .Select(t => new MemeCreationBox { Text = t })
            .ToArray();
          url = await _imgflip.CaptionImage(
            meme.Id,
            string.Empty,
            string.Empty,
            options.MaxFontSize,
            options.NoWatermark && _imgflip.IsAuthenticated ? true : null,
            boxes);
        }

        _resultUrl = url;
        Log($"[green]Created:[/] {MarkupParser.Escape(url)}");
        Toast("Meme created", NotificationSeverity.Success);

        string path = await _cache.GetAsync(url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Log($"[red]Create failed:[/] {MarkupParser.Escape(ex.Message)}");
        Toast("Create failed", NotificationSeverity.Danger);
      }
      finally
      {
        _busy = false;
      }
    }

    private void CopyResultUrl()
    {
      if (_resultUrl is null) { Log("[yellow]No meme created yet.[/]"); return; }
      ClipboardHelper.SetText(_resultUrl);
      Toast("URL copied", NotificationSeverity.Success);
      Log("URL copied to clipboard.");
    }

    private async Task SaveCurrentAsync()
    {
      string? source = _preview.CurrentPath;
      string? sourceUrl = _resultUrl ?? _selected?.Url;
      if (source is null || sourceUrl is null) { Log("[yellow]Nothing to save yet.[/]"); return; }

      string defaultName = Path.GetFileName(new Uri(sourceUrl).AbsolutePath);
      if (_resultUrl is null && _selected is not null)
        defaultName = $"{Sanitize(_selected.Name)}{Path.GetExtension(defaultName)}";

      Log("Opening file explorer...");
      string? target = await NativeFileDialog.SaveFileAsync(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        defaultName);

      if (target is null) { Log("Save cancelled."); return; }

      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        Log($"[green]Saved:[/] {MarkupParser.Escape(target)}");
        Toast("Saved", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Log($"[red]Save failed:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }

    private static string Sanitize(string name)
    {
      char[] invalid = Path.GetInvalidFileNameChars();
      return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
  }
}
