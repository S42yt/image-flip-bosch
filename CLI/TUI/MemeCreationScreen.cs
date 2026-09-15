using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.TUI.Core;
using image_flip_bosch.CLI.Utils.Ai;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.MemeFeed;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using OpenAI.Chat;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;

namespace image_flip_bosch.CLI.TUI
{
  internal sealed record CreationContext(
    ImgflipSession Imgflip,
    ImgFlipConfig Options,
    Meme[] Memes,
    AiAssistant Ai,
    ImageCache Cache,
    MemeFeedClient Feed,
    string? User);

  internal sealed record MemeCreationResult(MemeCreationBox[]? Boxes, string? AiUrl);

  internal sealed class MemeCreationScreen : NanoScreen
  {
    private sealed class Box
    {
      public int X;
      public int Y;
      public int Width;
      public int Height;
      public required string ColorHex;
    }

    private static readonly string[] BoxColors = ["#FF3B30", "#34C759", "#0A84FF", "#FFD60A", "#FF9F0A", "#BF5AF2", "#5AC8FA", "#FF2D55"];
    private const int WordDelayMs = 60;
    private const int IdleDelayMs = 400;
    private readonly Meme _meme;
    private readonly string? _imagePath;
    private readonly CreationContext _ctx;
    private readonly int? _maxFontSize;
    private readonly List<PromptControl> _inputs = [];
    private readonly List<Box> _boxes = [];
    private readonly List<MarkupControl> _boxLabels = [];
    private readonly string[] _committed;
    private readonly string[] _typed;
    private readonly ImagePreview _preview;
    private readonly TaskCompletionSource<MemeCreationResult> _result = new();
    private readonly CheckboxControl _customMode;
    private readonly MarkupControl _hint;
    private readonly List<ChatMessage> _chatHistory = [];
    private int _active;
    private bool _submitted;
    private bool _aiOpen;
    private string? _aiUrl;
    private CancellationTokenSource? _renderCts;
    private readonly bool _ready;
    private bool _previewBroken;
    private readonly bool _keepPreviousBoxes;
    private TemplateBox?[]? _layout;
    private bool _layoutFromImgflip;

    public bool CustomPositions => _customMode.Checked;

    public MemeCreationScreen(ConsoleWindowSystem ws, Meme meme, string? imagePath, bool customPositions, CreationContext ctx, MemeCreationBox[]? previous = null, int? maxFontSize = null)
      : base(ws, "Meme Creation")
    {
      _meme = meme;
      _imagePath = imagePath;
      _ctx = ctx;
      _maxFontSize = maxFontSize;
      int count = Math.Clamp(meme.BoxCount, 1, 20);
      _committed = new string[count];
      _typed = new string[count];

      bool previousHadPositions = previous is not null && previous.Any(b => b.X is not null);
      _customMode = Controls.Checkbox("Custom box positions (otherwise ImgFlip's template layout is used)")
        .Checked(customPositions || previousHadPositions)
        .OnCheckedChanged((_, _) => ModeChanged())
        .Build();
      _hint = Controls.Markup(string.Empty).Build();

      for (int i = 0; i < count; i++)
      {
        Box box = DefaultBox(i, count, meme.Width, meme.Height);
        if (previous is not null && i < previous.Length)
        {
          MemeCreationBox p = previous[i];
          if (p.X is { } x) box.X = x;
          if (p.Y is { } y) box.Y = y;
          if (p.Width is { } w) box.Width = w;
          if (p.Height is { } h) box.Height = h;
        }
        _boxes.Add(box);
      }

      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Say($"Preview failed: {msg}", NotificationSeverity.Danger);

      List<IWindowControl> column =
      [
        Controls.Markup(string.Empty).Build(),
        Controls.Markup($" {Chrome.HighlightText(meme.Name)} {Chrome.MutedText($"{meme.Width}x{meme.Height}  font: {ImageTextLiveUpdate.FontName}")}").Build(),
        Controls.Markup(string.Empty).Build(),
      ];

      for (int i = 0; i < count; i++)
      {
        int index = i;
        MarkupControl label = Controls.Markup(BoxLabel(i)).Build();
        _boxLabels.Add(label);

        PromptControl prompt = Controls.Prompt($" {Label(i, count),-8}: ")
          .WithPlaceholder(count > 1 ? "leave empty to skip" : "text")
          .UnfocusOnEnter(false)
          .OnEntered((_, _) => Advance(index))
          .OnInputChanged((sender, _) => InputChanged(index, sender))
          .Build();
        if (previous is not null && i < previous.Length)
          prompt.Input = previous[i].Text;
        _committed[index] = CommittedText(prompt.Input);
        _typed[index] = prompt.Input;
        _inputs.Add(prompt);

        column.Add(label);
        column.Add(prompt);
      }

      column.Add(Controls.Markup(string.Empty).Build());
      column.Add(_customMode);
      column.Add(_hint);
      _hint.SetContent([HintText()]);

      GridControl left = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows([.. column.Select(_ => GridLength.Auto())])
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      for (int i = 0; i < column.Count; i++) left.Place(column[i], i, 0);

      GridControl body = Controls.Grid()
        .Columns(GridLength.Star(), GridLength.Star())
        .Rows(GridLength.Star())
        .ColumnGap(2)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      body.Place(left, 0, 0);
      body.Place(_preview, 0, 1);

      BuildWindow([body], modal: true);
      _keepPreviousBoxes = previousHadPositions;
      _ready = true;
      ApplyLayout(TemplateLayoutProbe.Cached(meme.Id));
      RenderPreview();
      if (_layout is null) _ = LoadLayoutAsync();
    }

    private async Task LoadLayoutAsync()
    {
      if (!_ctx.Imgflip.IsAuthenticated)
      {
        Say("Login (F8) to load the real text positions from Imgflip", NotificationSeverity.Warning);
        return;
      }
      Say("Loading text positions from Imgflip...");
      try
      {
        TemplateBox?[]? layout = await TemplateLayoutProbe.GetAsync(_ctx.Imgflip, _ctx.Cache, _meme);
        if (layout is null || _result.Task.IsCompleted) return;
        await Ws.InvokeAsync(() =>
        {
          ApplyLayout(layout);
          RefreshLabels();
          RenderPreview();
        });
        Say(_layoutFromImgflip ? "Text positions loaded from Imgflip" : "Imgflip returned no usable positions, using estimates", _layoutFromImgflip ? NotificationSeverity.Success : NotificationSeverity.Warning);
      }
      catch (Exception ex)
      {
        Say($"Could not load text positions: {ex.Message}", NotificationSeverity.Warning);
      }
    }

    private void ApplyLayout(TemplateBox?[]? layout)
    {
      if (layout is null) return;
      _layout = layout;
      _layoutFromImgflip = layout.Any(b => b is not null);
      if (_keepPreviousBoxes) return;
      for (int i = 0; i < _boxes.Count && i < layout.Length; i++)
      {
        if (layout[i] is not { } b) continue;
        _boxes[i].X = b.X;
        _boxes[i].Y = b.Y;
        _boxes[i].Width = b.Width;
        _boxes[i].Height = b.Height;
        Clamp(_boxes[i]);
      }
    }

    private Box TemplateBoxFor(int i)
    {
      if (_layout is not null && i < _layout.Length && _layout[i] is { } b)
        return new Box { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, ColorHex = BoxColors[i % BoxColors.Length] };
      return DefaultBox(i, _inputs.Count, _meme.Width, _meme.Height);
    }

    protected override string HeaderCenter => $"Caption: {_meme.Name}";

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("F5", "Create"),
      ("F2", "AI"),
      ("F6", "Layout"),
      ("Esc", "Back"),
      ("F7", "Prev box"),
      ("F8", "Next box"),
    ];

    private string HintText() => Chrome.MutedText(CustomPositions
      ? " Enter: next box. Ctrl+Arrows: move box. Alt+Arrows: resize box. F7/F8: switch box. F2: AI assistant."
      : " Enter: next box. Text is placed by ImgFlip's template layout; F6 for custom positions. F2: AI assistant.");

    private void ModeChanged()
    {
      _hint.SetContent([HintText()]);
      RefreshLabels();
      Flush();
    }

    public Task<MemeCreationResult> ShowAsync(bool openAi = false)
    {
      Window.OnClosed += (_, _) =>
      {
        _renderCts?.Cancel();
        _result.TrySetResult(new MemeCreationResult(_submitted ? Collect() : null, _aiUrl));
      };
      Show();
      Window.FocusControl(_inputs[0]);
      if (openAi) _ = OpenAiAsync();
      return _result.Task;
    }

    protected override void OnKey(KeyPressedEventArgs e)
    {
      bool ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
      bool alt = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt);
      int step = Math.Max(2, _meme.Width / 50);

      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.Tab:
          CycleFocus(e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
          e.Handled = true;
          return;
        case ConsoleKey.Escape: Window.Close(); e.Handled = true; return;
        case ConsoleKey.F2: _ = OpenAiAsync(); e.Handled = true; return;
        case ConsoleKey.F5: Submit(); e.Handled = true; return;
        case ConsoleKey.F6: _customMode.Checked = !_customMode.Checked; ModeChanged(); e.Handled = true; return;
        case ConsoleKey.F7: FocusBox(_active - 1); e.Handled = true; return;
        case ConsoleKey.F8: FocusBox(_active + 1); e.Handled = true; return;
      }

      if (!CustomPositions || (!ctrl && !alt)) return;
      Box box = _boxes[_active];
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.LeftArrow: if (alt) box.Width -= step; else box.X -= step; break;
        case ConsoleKey.RightArrow: if (alt) box.Width += step; else box.X += step; break;
        case ConsoleKey.UpArrow: if (alt) box.Height -= step; else box.Y -= step; break;
        case ConsoleKey.DownArrow: if (alt) box.Height += step; else box.Y += step; break;
        default: return;
      }
      Clamp(box);
      e.Handled = true;
      RefreshLabels();
      RenderPreview();
    }

    protected override void OnChromeChanged() => RefreshLabels();

    private async Task OpenAiAsync()
    {
      if (_aiOpen) return;
      _aiOpen = true;
      try
      {
        AiChatResult r = await new AiChatScreen(Ws, _meme, _ctx, _chatHistory).ShowAsync();
        if (r.Captions is not null && (r.Meme is null || ReferenceEquals(r.Meme, _meme)))
          for (int i = 0; i < _inputs.Count && i < r.Captions.Length; i++) _inputs[i].Input = r.Captions[i];
        if (r.Url is null)
        {
          if (r.Captions is not null) Say("AI captions filled in, F5 creates the meme", NotificationSeverity.Success);
          return;
        }

        _aiUrl = r.Url;
        Say("AI captions filled in, F5 recreates them or Esc keeps the AI meme", NotificationSeverity.Success);
        _renderCts?.Cancel();
        string path = await _ctx.Cache.GetAsync(r.Url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Say($"AI result failed: {ex.Message}", NotificationSeverity.Danger);
      }
      finally
      {
        _aiOpen = false;
        Window.FocusControl(_inputs[0]);
      }
    }

    private void InputChanged(int index, object? sender)
    {
      if (!_ready) return;

      bool activeChanged = index != _active;
      if (activeChanged)
      {
        _active = index;
        RefreshLabels();
      }

      bool textChanged = false;
      bool wordChanged = false;

      if (sender is PromptControl prompt)
      {
        string text = prompt.Input;
        textChanged = text != _typed[index];
        _typed[index] = text;

        string committed = CommittedText(text);
        if (committed != _committed[index])
        {
          _committed[index] = committed;
          wordChanged = true;
        }
      }

      if (!activeChanged && !textChanged) return;
      RenderPreview(activeChanged || wordChanged ? WordDelayMs : IdleDelayMs);
    }

   
    private static string CommittedText(string text)
    {
      for (int i = text.Length - 1; i >= 0; i--)
        if (char.IsWhiteSpace(text[i]))
          return text[..(i + 1)];
      return string.Empty;
    }

    private void Flush()
    {
      if (!_ready) return;
      for (int i = 0; i < _inputs.Count; i++)
      {
        _typed[i] = _inputs[i].Input;
        _committed[i] = CommittedText(_typed[i]);
      }
      RenderPreview();
    }

    private static string Label(int index, int count) => count switch
    {
      1 => "Text",
      2 => index == 0 ? "Top" : "Bottom",
      _ => $"Box {index + 1}",
    };

    private static Box DefaultBox(int index, int count, int width, int height)
    {
      int marginX = Math.Max(4, width / 25);
      int marginY = Math.Max(4, height / 40);
      string color = BoxColors[index % BoxColors.Length];

      switch (count)
      {
        case 1:
          return new Box { X = marginX, Y = marginY, Width = width - 2 * marginX, Height = height / 4, ColorHex = color };
        case 2:
          {
            int h = height / 4;
            return index == 0
              ? new Box { X = marginX, Y = marginY, Width = width - 2 * marginX, Height = h, ColorHex = color }
              : new Box { X = marginX, Y = height - h - marginY, Width = width - 2 * marginX, Height = h, ColorHex = color };
          }
        default:
          {
            int band = height / count;
            return new Box
            {
              X = marginX,
              Y = index * band + marginY,
              Width = width - 2 * marginX,
              Height = Math.Max(10, band - 2 * marginY),
              ColorHex = color,
            };
          }
      }
    }

    private void Clamp(Box box)
    {
      box.Width = Math.Clamp(box.Width, 10, _meme.Width);
      box.Height = Math.Clamp(box.Height, 10, _meme.Height);
      box.X = Math.Clamp(box.X, 0, _meme.Width - box.Width);
      box.Y = Math.Clamp(box.Y, 0, _meme.Height - box.Height);
    }

    private string BoxLabel(int i)
    {
      Box b = _boxes[i];
      string marker = i == _active ? "▶" : " ";
      return !CustomPositions
        ? $" [{b.ColorHex}]{marker} ■[/] {Chrome.MutedText($"{Label(i, _boxes.Count)}  {(_layoutFromImgflip ? "imgflip position" : "estimated position")}")}"
        : $" [{b.ColorHex}]{marker} ■[/] {Chrome.MutedText($"{Label(i, _boxes.Count)}  x{b.X} y{b.Y}  {b.Width}x{b.Height}")}";
    }

    private void RefreshLabels()
    {
      for (int i = 0; i < _boxLabels.Count; i++)
        _boxLabels[i].SetContent([BoxLabel(i)]);
    }

    private void CycleFocus(int direction)
    {
      int stops = _inputs.Count + 1;
      int current = _customMode.HasFocus ? _inputs.Count : _active;
      int next = (current + direction + stops) % stops;

      if (next == _inputs.Count) Window.FocusControl(_customMode);
      else FocusBox(next);
    }

    private void FocusBox(int index)
    {
      index = (index + _inputs.Count) % _inputs.Count;
      Window.FocusControl(_inputs[index]);
      if (index != _active)
      {
        _active = index;
        RefreshLabels();
      }
      Flush();
    }

    private void Advance(int index)
    {
      if (index + 1 < _inputs.Count) FocusBox(index + 1);
      else Submit();
    }

    private List<MemeTextArea> TextAreas()
    {
      bool uppercase = !CustomPositions && _inputs.Count <= 2;

      List<MemeTextArea> areas = [];
      for (int i = 0; i < _inputs.Count; i++)
      {
        string text = _inputs[i].Input.Trim();
        if (text.Length == 0) continue;
        if (uppercase) text = text.ToUpperInvariant();

        Box source = CustomPositions ? _boxes[i] : TemplateBoxFor(i);
        areas.Add(new MemeTextArea(text, source.X, source.Y, source.Width, source.Height));
      }
      return areas;
    }

    private void CancelRender()
    {
      CancellationTokenSource? previous = _renderCts;
      _renderCts = null;
      if (previous is null) return;
      previous.Cancel();
      previous.Dispose();
    }

    private void RenderPreview(int delayMs = WordDelayMs)
    {
      if (_imagePath is null) return;

      CancelRender();
      CancellationTokenSource cts = new();
      _renderCts = cts;

      List<MemeBoxOverlay> boxes = CustomPositions
        ? [.. _boxes.Select((b, i) => new MemeBoxOverlay(b.X, b.Y, b.Width, b.Height, b.ColorHex, i == _active))]
        : [];
      List<MemeTextArea> texts = TextAreas();
      string path = _imagePath;
      int width = _meme.Width;
      int height = _meme.Height;
      int? maxFont = _maxFontSize;

      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(delayMs, cts.Token);
          byte[] png = ImageTextLiveUpdate.Compose(path, width, height, texts, boxes, maxFont);
          if (cts.Token.IsCancellationRequested) return;
          await Ws.InvokeAsync(() => _preview.SetImage(png));
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
          if (_previewBroken) return;
          _previewBroken = true;
          Say($"Preview failed: {ex.Message}", NotificationSeverity.Danger);
        }
      }, cts.Token);
    }

    

    private MemeCreationBox[] Collect()
    {
      bool custom = CustomPositions;
      return _inputs.Select((p, i) => custom
        ? new MemeCreationBox
        {
          Text = p.Input.Trim(),
          X = _boxes[i].X,
          Y = _boxes[i].Y,
          Width = _boxes[i].Width,
          Height = _boxes[i].Height,
          Color = "#ffffff",
          OutlineColor = "#000000",
        }
        : new MemeCreationBox { Text = p.Input.Trim() }).ToArray();
    }

    private void Submit()
    {
      if (_inputs.All(p => p.Input.Trim().Length == 0))
      {
        Say("Enter at least one caption", NotificationSeverity.Warning);
        return;
      }
      _submitted = true;
      Window.Close();
    }
  }
}
