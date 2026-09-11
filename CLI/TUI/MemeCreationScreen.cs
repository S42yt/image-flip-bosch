using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace image_flip_bosch.CLI.TUI
{
  internal sealed class MemeCreationScreen : IDisposable
  {
    private enum Mode { Templates, Text }

    private const int PreviewDelayMs = 150;
    private static readonly GridLength Auto = GridLength.Auto();
    private static readonly GridLength Fill = GridLength.Star();

    private readonly ConsoleWindowSystem _ws;
    private readonly ImageCache _cache;
    private readonly ImgflipSession _imgflip;

    // Header
    private readonly ButtonControl _templatesTab;
    private readonly ButtonControl _textTab;

    // Template mode
    private readonly PromptControl _search;
    private readonly ListControl _templates;
    private readonly ImagePreview _preview;
    private readonly MarkupControl _details;
    private readonly PromptControl _topText;
    private readonly PromptControl _bottomText;
    private readonly PromptControl _fontSize;
    private readonly CheckboxControl _noWatermark;
    private readonly GridControl _templateLayout;

    // Text mode
    private readonly PromptControl _idea;
    private readonly CheckboxControl _ideaNoWatermark;
    private readonly ImagePreview _result;
    private readonly MarkupControl _resultInfo;
    private readonly GridControl _textLayout;

    private readonly GridControl _body;
    private readonly MarkupControl _status;
    private readonly Window _window;

    private Meme[] _all = Array.Empty<Meme>();
    private Meme? _selected;
    private Mode _mode = Mode.Templates;
    private CancellationTokenSource? _previewCts;
    private bool _creating;
    private bool _disposed;

    public MemeCreationScreen(ConsoleWindowSystem ws, ImageCache cache, ImgflipSession imgflip)
    {
      _ws = ws ?? throw new ArgumentNullException(nameof(ws));
      _cache = cache ?? throw new ArgumentNullException(nameof(cache));
      _imgflip = imgflip ?? throw new ArgumentNullException(nameof(imgflip));

      // ── Template mode: list | preview | captions ─────────────────
      _search = Input(" Filter ", "type to filter templates");
      _search.InputChanged += (_, _) => ApplyFilter();

      _templates = Controls.List()
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .OnSelectedItemChanged((_, item) => { if (item?.Tag is Meme meme) Select(meme); })
        .OnItemActivated((_, _) => _ = CreateAsync())
        .Build();

      _preview = Preview();
      _details = Controls.Label("[dim]No template selected.[/]");

      _topText = Input(" Top    ", "Text oben");
      _bottomText = Input(" Bottom ", "Text unten");
      _fontSize = Input(" Font size ", "auto");
      _noWatermark = Controls.Checkbox("Remove watermark").Build();

      _templateLayout = Columns(
        new[] { GridLength.Star(), GridLength.Star(2), GridLength.Star() },
        Stack(
          (Section("Templates"), Auto),
          (_search, Auto),
          (_templates, Fill)),
        Stack(
          (Section("Preview"), Auto),
          (_preview, Fill),
          (_details, Auto)),
        Stack(
          (Section("Captions"), Auto),
          (_topText, Auto),
          (_bottomText, Auto),
          (Section("Options"), Auto),
          (_fontSize, Auto),
          (_noWatermark, Auto),
          (Controls.Label(string.Empty), Fill),
          (Actions(), Auto)));

      // ── Text mode: input | result ────────────────────────────────
      _idea = Input(" Text ", "What should the meme say?");
      _idea.Entered += (_, _) => _ = CreateAsync();
      _ideaNoWatermark = Controls.Checkbox("Remove watermark").Build();

      _result = Preview();
      _resultInfo = Controls.Label("[dim]Your meme appears here after creating it.[/]");

      _textLayout = Columns(
        new[] { GridLength.Star(), GridLength.Star(2) },
        Stack(
          (Section("From text"), Auto),
          (_idea, Auto),
          (Controls.Label("[dim]Imgflip picks a matching template and places your text.[/]"), Auto),
          (_ideaNoWatermark, Auto),
          (Controls.Label(string.Empty), Fill),
          (Actions(), Auto)),
        Stack(
          (Section("Result"), Auto),
          (_result, Fill),
          (_resultInfo, Auto)));

      // ── Body swaps between the two layouts ───────────────────────
      _body = Controls.Grid()
        .Columns(Fill)
        .Rows(Fill)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      _body.Place(_templateLayout, 0, 0);

      // ── Header ───────────────────────────────────────────────────
      _templatesTab = Controls.Button(TabLabel("Templates", true)).OnClick((_, _) => SetMode(Mode.Templates)).Build();
      _textTab = Controls.Button(TabLabel("From text", false)).OnClick((_, _) => SetMode(Mode.Text)).Build();

      ToolbarControl header = Controls.Toolbar()
        .Add(Controls.Label("[bold cyan]Meme Creator[/]"))
        .AddSeparator()
        .AddButton(_templatesTab)
        .AddButton(_textTab)
        .AddSeparator()
        .AddButton(Controls.Button("↻ Reload").OnClick((_, _) => _ = LoadAsync()).Build())
        .WithBelowLine()
        .StickyTop()
        .Build();

      // ── Footer ───────────────────────────────────────────────────
      _status = Controls.Markup("[dim]Ready[/]").StickyBottom().Build();

      StatusBarControl statusBar = Controls.StatusBar()
        .AddLeft("F2", "Templates", () => SetMode(Mode.Templates))
        .AddLeft("F3", "From text", () => SetMode(Mode.Text))
        .AddLeft("Ctrl+Enter", "Create", () => _ = CreateAsync())
        .AddLeft("Ctrl+R", "Reset", Reset)
        .AddLeft("Esc", "Close", () => _window.Close())
        .StickyBottom()
        .Build();

      _window = new WindowBuilder(_ws)
        .WithTitle("Meme Creator")
        .WithSize(118, 34)
        .Centered()
        .AsModal()
        .Resizable(true)
        .Maximized()
        .AddControls(header, _body, _status, statusBar)
        .Build();

      _window.PreviewKeyPressed += OnKey;
    }

    public void Show()
    {
      _ws.AddWindow(_window);
      _window.FocusControl(_search);
      _ = LoadAsync();
    }

    // ── Mode ────────────────────────────────────────────────────────

    private void SetMode(Mode mode)
    {
      _mode = mode;
      _templatesTab.Text = TabLabel("Templates", mode == Mode.Templates);
      _textTab.Text = TabLabel("From text", mode == Mode.Text);
      _body.Cell(0, 0).Content = mode == Mode.Templates ? _templateLayout : _textLayout;
      _window.FocusControl(mode == Mode.Templates ? _search : _idea);
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
      ConsoleKeyInfo key = e.KeyInfo;
      bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
      bool browsing = _mode == Mode.Templates && _search.HasFocus;

      switch (key.Key)
      {
        case ConsoleKey.Escape: _window.Close(); break;
        case ConsoleKey.F2: SetMode(Mode.Templates); break;
        case ConsoleKey.F3: SetMode(Mode.Text); break;
        case ConsoleKey.Enter when ctrl: _ = CreateAsync(); break;
        case ConsoleKey.R when ctrl: Reset(); break;
        // Arrow keys move the list only while typing in the filter
        case ConsoleKey.UpArrow when browsing: Move(-1); break;
        case ConsoleKey.DownArrow when browsing: Move(+1); break;
        default: return;
      }

      e.Handled = true;
    }

    private void Move(int delta)
    {
      int count = _templates.Items.Count;
      if (count == 0) return;
      _templates.SelectedIndex = ((_templates.SelectedIndex + delta) % count + count) % count;
    }

    // ── Templates ───────────────────────────────────────────────────

    private async Task LoadAsync()
    {
      SetStatus("[yellow]Loading templates…[/]");
      try
      {
        Meme[] memes = await _imgflip.GetMemes();
        await _ws.InvokeAsync(() => { _all = memes; ApplyFilter(); });
        SetStatus($"[green]{memes.Length} templates loaded[/]");
      }
      catch (Exception ex)
      {
        SetStatus($"[red]Could not load templates:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }

    private void ApplyFilter()
    {
      string term = _search.Input?.Trim() ?? string.Empty;
      Meme[] visible = term.Length == 0
        ? _all
        : _all.Where(m => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();

      _templates.Items = visible
        .Select(m => new ListItem($"{MarkupParser.Escape(m.Name)} [dim]({m.BoxCount})[/]") { Tag = m })
        .ToList();

      if (visible.Length == 0)
      {
        _selected = null;
        _details.SetContent(new List<string> { "[yellow]No matching templates.[/]", "[dim]Try a different search term.[/]" });
        return;
      }

      int index = _selected is null ? -1 : Array.IndexOf(visible, _selected);
      _templates.SelectedIndex = Math.Max(index, 0);
      Select(visible[_templates.SelectedIndex]);
    }

    private void Select(Meme meme)
    {
      if (ReferenceEquals(meme, _selected)) return;
      _selected = meme;

      _details.SetContent(new List<string>
      {
        $"[bold]{MarkupParser.Escape(meme.Name)}[/]",
        $"[dim]{meme.Width}×{meme.Height}, {meme.BoxCount} text boxes, ID {meme.Id}[/]",
      });

      _previewCts?.Cancel();
      _previewCts?.Dispose();
      _previewCts = new CancellationTokenSource();
      _ = ShowImageAsync(_preview, meme.Url, _previewCts.Token, PreviewDelayMs);
    }

    // ── Create ──────────────────────────────────────────────────────

    private Task CreateAsync() => _mode == Mode.Templates ? CreateFromTemplateAsync() : CreateFromTextAsync();

    private Task CreateFromTemplateAsync()
    {
      if (_selected is not Meme meme) { ShowError("Select a template first."); return Task.CompletedTask; }

      string top = _topText.Input?.Trim() ?? string.Empty;
      string bottom = _bottomText.Input?.Trim() ?? string.Empty;
      if (top.Length == 0 && bottom.Length == 0) { ShowError("Enter at least one caption."); return Task.CompletedTask; }

      return RunCreateAsync(async () =>
      {
        await _imgflip.CaptionImage(meme.Id, top, bottom, ReadFontSize(), _noWatermark.Checked);
        SetStatus($"[green]Meme created:[/] {MarkupParser.Escape(meme.Name)}");
      });
    }

    private Task CreateFromTextAsync()
    {
      string text = _idea.Input?.Trim() ?? string.Empty;
      if (text.Length == 0) { ShowError("Enter a text first."); return Task.CompletedTask; }

      return RunCreateAsync(async () =>
      {
        string url = await _imgflip.AutoMeme(text, _ideaNoWatermark.Checked);
        await ShowImageAsync(_result, url, CancellationToken.None);
        await _ws.InvokeAsync(() => _resultInfo.SetContent(new List<string> { $"[dim]{MarkupParser.Escape(text)}[/]" }));
        SetStatus("[green]Meme created from text[/]");
      });
    }

    private async Task RunCreateAsync(Func<Task> create)
    {
      if (_creating) return;
      _creating = true;
      SetStatus("[yellow]Creating meme…[/]");
      try
      {
        // Result URL is intentionally not displayed
        await create();
        _ws.ToastService.Show("Meme created", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        ShowError(ex.Message);
      }
      finally
      {
        _creating = false;
      }
    }

    private async Task ShowImageAsync(ImagePreview target, string url, CancellationToken ct, int delayMs = 0)
    {
      try
      {
        if (delayMs > 0) await Task.Delay(delayMs, ct); // debounce fast scrolling
        string path = await _cache.GetAsync(url, ct);
        await target.LoadWhenReadyAsync(path, ct: ct);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        SetStatus($"[red]Image download failed:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }

    private int? ReadFontSize() =>
      int.TryParse(_fontSize.Input?.Trim(), out int size) && size > 0 ? size : null;

    private void Reset()
    {
      _topText.Input = string.Empty;
      _bottomText.Input = string.Empty;
      _fontSize.Input = string.Empty;
      _noWatermark.Checked = false;
      _idea.Input = string.Empty;
      _ideaNoWatermark.Checked = false;
      SetStatus("[dim]Form reset[/]");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void SetStatus(string markup) =>
      _ws.InvokeAsync(() => _status.SetContent(new List<string> { markup }));

    private void ShowError(string message)
    {
      SetStatus($"[red]{MarkupParser.Escape(message)}[/]");
      _ws.ToastService.Show(message, NotificationSeverity.Danger);
    }

    private static string TabLabel(string text, bool active) => active ? $"● {text}" : $"○ {text}";

    private static RuleControl Section(string title) =>
      Controls.RuleBuilder().WithTitle($"[bold]{title}[/]").TitleLeft().Build();

    private static PromptControl Input(string label, string placeholder) => Controls.Prompt(label)
      .WithPlaceholder(placeholder)
      .UnfocusOnEnter(false)
      .WithAlignment(HorizontalAlignment.Stretch)
      .Build();

    private ImagePreview Preview()
    {
      var preview = new ImagePreview(_ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      preview.LoadFailed += (_, msg) => SetStatus($"[red]Preview failed:[/] {MarkupParser.Escape(msg)}");
      return preview;
    }

    private HorizontalGridControl Actions() => Controls.HorizontalGrid()
      .Column(c => c.Add(Controls.Button("Create").OnClick((_, _) => _ = CreateAsync()).Build()))
      .Column(c => c.Add(Controls.Button("Reset").OnClick((_, _) => Reset()).Build()))
      .Build();

    /// <summary>Side-by-side panes separated by single divider lines (no nested borders).</summary>
    private static GridControl Columns(GridLength[] widths, params IWindowControl[] panes)
    {
      GridControl grid = Controls.Grid()
        .Columns(widths)
        .Rows(Fill)
        .ColumnGridlines()
        .GridlineStyle(BorderStyle.Single)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      for (int col = 0; col < panes.Length; col++)
      {
        grid.Place(panes[col], 0, col);
        grid.Cell(0, col).Padding = new Padding(1, 0);
      }
      return grid;
    }

    /// <summary>Single-column grid, one control per row.</summary>
    private static GridControl Stack(params (IWindowControl Control, GridLength Height)[] rows)
    {
      GridControl grid = Controls.Grid()
        .Columns(Fill)
        .Rows(rows.Select(r => r.Height).ToArray())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      for (int i = 0; i < rows.Length; i++) grid.Place(rows[i].Control, i, 0);
      return grid;
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _window.PreviewKeyPressed -= OnKey;
      _previewCts?.Cancel();
      _previewCts?.Dispose();
    }
  }
}
