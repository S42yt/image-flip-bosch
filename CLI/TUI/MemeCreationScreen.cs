using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.TUI.Core;
using image_flip_bosch.CLI.Utils.Ai;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.MemeFeed;
using image_flip_bosch.CLI.Utils.Native;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using OpenAI.Chat;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;

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

    private const string CaptionMemeToolName = "caption_meme";
    private static readonly string[] BoxColors = ["#FF3B30", "#34C759", "#0A84FF", "#FFD60A", "#FF9F0A", "#BF5AF2", "#5AC8FA", "#FF2D55"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly ChatTool CaptionMemeTool = ChatTool.CreateFunctionTool(
      functionName: CaptionMemeToolName,
      functionDescription: "Create a meme image by adding captions to a template. Call it once when the user asks for a meme. Repeat the returned URL to the user afterwards.",
      functionParameters: BinaryData.FromString("""
      {
        "type": "object",
        "properties": {
          "meme_id": { "type": "string", "description": "Template id from the list. Use the current template unless the user asks for another." },
          "captions": { "type": "array", "items": { "type": "string" }, "description": "One caption per text box, between 1 and box_count entries." }
        },
        "required": ["meme_id", "captions"]
      }
      """));

    private readonly Meme _meme;
    private readonly string? _imagePath;
    private readonly CreationContext _ctx;
    private readonly List<PromptControl> _inputs = [];
    private readonly List<Box> _boxes = [];
    private readonly List<MarkupControl> _boxLabels = [];
    private readonly ImagePreview _preview;
    private readonly TaskCompletionSource<MemeCreationResult> _result = new();
    private readonly CheckboxControl _customMode;
    private readonly MarkupControl _hint;
    private readonly ChatTranscriptControl _transcript;
    private readonly PromptControl _chatInput;
    private readonly MarkupControl _chatStatus;
    private readonly ProgressBarControl _chatProgress;
    private readonly List<ChatMessage> _history;
    private readonly CancellationTokenSource _chatCts = new();
    private int _active;
    private bool _submitted;
    private bool _chatBusy;
    private bool _aiStarting;
    private string? _aiUrl;
    private CancellationTokenSource? _renderCts;

    public bool CustomPositions => _customMode.Checked;

    public MemeCreationScreen(ConsoleWindowSystem ws, Meme meme, string? imagePath, bool customPositions, CreationContext ctx, MemeCreationBox[]? previous = null)
      : base(ws, "Meme Creation")
    {
      _meme = meme;
      _imagePath = imagePath;
      _ctx = ctx;
      _history = [new SystemChatMessage(BuildSystemPrompt())];
      int count = Math.Clamp(meme.BoxCount, 1, 20);

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
        Controls.Markup($" {Chrome.HighlightText(meme.Name)} {Chrome.MutedText($"{meme.Width}x{meme.Height}")}").Build(),
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
          .OnInputChanged((_, _) => SetActive(index))
          .Build();
        if (previous is not null && i < previous.Length)
          prompt.Input = previous[i].Text;
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

      _transcript = new ChatTranscriptControl
      {
        ShowScrollbar = true,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _chatStatus = Controls.Markup(Chrome.MutedText(" F2 starts the AI assistant")).Build();
      _chatProgress = Controls.ProgressBar().WithHeader("AI").Indeterminate().ShowPercentage().Stretch().Build();
      _chatProgress.Visible = false;
      _chatInput = Controls.Prompt(" AI: ")
        .WithPlaceholder("ask for captions, e.g. make it about mondays")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _ = SendAsync())
        .Build();
      _chatInput.IsEnabled = false;

      GridControl chat = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Auto(), GridLength.Star(), GridLength.Auto(), GridLength.Auto(), GridLength.Auto())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      chat.Place(Controls.Markup(Chrome.SectionText(" AI assistant")).Build(), 0, 0);
      chat.Place(_transcript, 1, 0);
      chat.Place(_chatProgress, 2, 0);
      chat.Place(_chatStatus, 3, 0);
      chat.Place(_chatInput, 4, 0);

      GridControl body = Controls.Grid()
        .Columns(GridLength.Star(3), GridLength.Star(4), GridLength.Star(3))
        .Rows(GridLength.Star())
        .ColumnGap(2)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      body.Place(left, 0, 0);
      body.Place(_preview, 0, 1);
      body.Place(chat, 0, 2);

      BuildWindow([body], modal: true);
      _ctx.Ai.StatusChanged += OnAiStatus;
      _ctx.Ai.Progress += OnAiProgress;
      Window.OnClosed += (_, _) =>
      {
        _ctx.Ai.StatusChanged -= OnAiStatus;
        _ctx.Ai.Progress -= OnAiProgress;
        _chatCts.Cancel();
      };
      if (_ctx.Ai.IsReady) EnableChat();
      RenderOverlay();
    }

    protected override string HeaderCenter => $"Caption: {_meme.Name}";

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("F5", "Create"),
      ("F2", "AI"),
      ("F6", "Layout"),
      ("F9", "Copy AI"),
      ("F7", "Prev box"),
      ("F10", "Save AI"),
      ("F8", "Next box"),
      ("F12", "Upload AI"),
      ("Esc", "Back"),
    ];

    private string HintText() => Chrome.MutedText(CustomPositions
      ? " Enter: next box. Ctrl+Arrows: move box. Alt+Arrows: resize box. F7/F8: switch box."
      : " Enter: next box. Text is placed by ImgFlip's template layout; press F6 for custom positions.");

    private void ModeChanged()
    {
      _hint.SetContent([HintText()]);
      RefreshLabels();
      RenderOverlay();
    }

    public Task<MemeCreationResult> ShowAsync(bool focusAi = false)
    {
      Window.OnClosed += (_, _) =>
      {
        _renderCts?.Cancel();
        _result.TrySetResult(new MemeCreationResult(_submitted ? Collect() : null, _aiUrl));
      };
      Show();
      if (focusAi) StartAi();
      else Window.FocusControl(_inputs[0]);
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
        case ConsoleKey.F2: StartAi(); e.Handled = true; return;
        case ConsoleKey.F5: Submit(); e.Handled = true; return;
        case ConsoleKey.F6: _customMode.Checked = !_customMode.Checked; ModeChanged(); e.Handled = true; return;
        case ConsoleKey.F7: FocusBox(_active - 1); e.Handled = true; return;
        case ConsoleKey.F8: FocusBox(_active + 1); e.Handled = true; return;
        case ConsoleKey.F9: _ = CopyAiAsync(); e.Handled = true; return;
        case ConsoleKey.F10: _ = SaveAiAsync(); e.Handled = true; return;
        case ConsoleKey.F12: _ = UploadAiAsync(); e.Handled = true; return;
      }

      if (_chatInput.HasFocus) return;
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
      RenderOverlay();
    }

    protected override void OnChromeChanged() => RefreshLabels();

    private string BuildSystemPrompt()
    {
      var others = _ctx.Memes
        .Where(m => m.Id != _meme.Id)
        .Take(40)
        .Select(m => new { id = m.Id, name = m.Name, box_count = m.BoxCount });
      var current = new { id = _meme.Id, name = _meme.Name, box_count = _meme.BoxCount };
      return "You are a meme caption assistant inside a terminal app. Keep answers short. " +
        "When the user wants a meme, call caption_meme with witty captions. " +
        $"The user is currently editing this template: {JsonSerializer.Serialize(current, JsonOptions)}. Prefer it. " +
        "Other available templates: " + JsonSerializer.Serialize(others, JsonOptions);
    }

    private void StartAi()
    {
      if (_ctx.Ai.IsReady)
      {
        EnableChat();
        Window.FocusControl(_chatInput);
        return;
      }
      if (_aiStarting) return;
      _aiStarting = true;
      RunOnUi(() => _chatProgress.Visible = true);
      _ = Task.Run(async () =>
      {
        try
        {
          await _ctx.Ai.EnsureReadyAsync(_chatCts.Token);
          RunOnUi(() =>
          {
            EnableChat();
            Window.FocusControl(_chatInput);
          });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
          RunOnUi(() =>
          {
            _chatProgress.Visible = false;
            _chatStatus.SetContent([$"[{Chrome.Danger.ToMarkup()}] AI unavailable[/]"]);
            _transcript.AddMessage(ChatRole.Error, ChatMarkup.Render(ex.Message));
          });
        }
        finally
        {
          _aiStarting = false;
        }
      });
    }

    private void EnableChat()
    {
      _chatProgress.Visible = false;
      _chatInput.IsEnabled = true;
      _chatStatus.SetContent([Chrome.MutedText(_ctx.Imgflip.IsAuthenticated
        ? " AI ready. Ask for captions, Enter sends."
        : " AI ready. Not logged in to Imgflip, the AI can suggest captions but not create memes.")]);
    }

    private void OnAiStatus(AiState state, string text)
    {
      RunOnUi(() =>
      {
        _chatStatus.SetContent([state == AiState.Failed ? $"[{Chrome.Danger.ToMarkup()}] {text}[/]" : Chrome.MutedText($" {text}")]);
        _chatProgress.Header = text;
        _chatProgress.IsIndeterminate = true;
      });
    }

    private void OnAiProgress(DownloadProgress p)
    {
      if (p.TotalBytes <= 0) return;
      RunOnUi(() =>
      {
        _chatProgress.IsIndeterminate = false;
        _chatProgress.MaxValue = p.TotalBytes;
        _chatProgress.Value = p.BytesDownloaded;
        _chatProgress.Header = $"{p.Label} {p.BytesDownloaded / 1048576} / {p.TotalBytes / 1048576} MB";
      });
    }

    private async Task SendAsync()
    {
      if (!_ctx.Ai.IsReady) { StartAi(); return; }
      if (_chatBusy) return;
      string text = _chatInput.Input.Trim();
      if (text.Length == 0) return;

      _chatBusy = true;
      _chatInput.Input = string.Empty;
      _chatInput.IsEnabled = false;
      _history.Add(new UserChatMessage(text));
      ChatMessageId replyId = default;
      RunOnUi(() =>
      {
        _transcript.AddMessage(ChatRole.User, ChatMarkup.Render(text), author: "You", actions: [], status: null, markdown: false);
        replyId = _transcript.AddMessage(ChatRole.Assistant, string.Empty, author: "AI", actions: [], status: null, markdown: false);
        _transcript.SetMarkdownMode(replyId, false);
      });

      try
      {
        await _ctx.Ai.ChatAsync(
          _history,
          CaptionMemeTool,
          ExecuteToolAsync,
          full => RunOnUi(() => _transcript.UpdateMessage(replyId, ChatMarkup.Render(full))),
          preceding => RunOnUi(() => _transcript.SetStatus(replyId, "creating meme...", NotificationSeverity.Info)),
          _chatCts.Token);
        RunOnUi(() => _transcript.ClearStatus(replyId));
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        RunOnUi(() => _transcript.AddMessage(ChatRole.Error, ChatMarkup.Render($"Chat failed: {ex.Message}")));
      }
      finally
      {
        _chatBusy = false;
        RunOnUi(() =>
        {
          _chatInput.IsEnabled = true;
          Window.FocusControl(_chatInput);
        });
      }
    }

    private async Task<string> ExecuteToolAsync(string name, string argumentsJson)
    {
      if (name != CaptionMemeToolName) return $"Unknown tool '{name}'.";

      if (!_ctx.Imgflip.IsAuthenticated)
      {
        Say("Login (Esc, then F8) to let the AI create memes", NotificationSeverity.Warning);
        RunOnUi(() => _transcript.AddMessage(ChatRole.Error, "Not logged in to Imgflip. Press Esc, then F8 to log in. Captions can still be typed into the boxes."));
        return "The user is not logged in to Imgflip, so no meme can be created. Suggest the captions as text instead.";
      }

      try
      {
        using var args = JsonDocument.Parse(argumentsJson);
        string? memeId = args.RootElement.TryGetProperty("meme_id", out JsonElement idEl) ? idEl.GetString() : null;
        Meme meme = memeId == _meme.Id ? _meme : _ctx.Memes.FirstOrDefault(m => m.Id == memeId) ?? _meme;

        List<string> captions = [];
        if (args.RootElement.TryGetProperty("captions", out JsonElement captionsEl) && captionsEl.ValueKind == JsonValueKind.Array)
          captions.AddRange(captionsEl.EnumerateArray().Select(c => c.GetString() ?? string.Empty));

        if (captions.Count < 1 || captions.Count > meme.BoxCount)
          return $"'{meme.Name}' takes between 1 and {meme.BoxCount} captions, got {captions.Count}. Try again.";

        MemeCreationBox[] boxes = captions.Select(c => new MemeCreationBox { Text = c }).ToArray();
        bool? noWatermark = _ctx.Options.NoWatermark ? true : null;
        string url = await _ctx.Imgflip.CaptionImage(meme.Id, string.Empty, string.Empty, _ctx.Options.MaxFontSize, noWatermark, boxes);

        _aiUrl = url;
        RunOnUi(() =>
        {
          if (ReferenceEquals(meme, _meme))
            for (int i = 0; i < _inputs.Count && i < captions.Count; i++) _inputs[i].Input = captions[i];
          _transcript.AddMessage(ChatRole.Tool, $"[{Chrome.Success.ToMarkup()}]Meme created[/] {Chrome.MutedText(meme.Name)}", author: "Imgflip", actions: [], status: null, markdown: false);
        });
        Say("AI meme ready: F9 copy, F10 save, F12 upload", NotificationSeverity.Success);
        _ = ShowAiResultAsync(url);
        return $"Meme created with template '{meme.Name}': {url}";
      }
      catch (ImgFlipException ex)
      {
        RunOnUi(() => _transcript.AddMessage(ChatRole.Error, ChatMarkup.Render($"Imgflip refused: {ex.Message}")));
        return $"Imgflip refused the request: {ex.Message}";
      }
      catch (Exception ex)
      {
        RunOnUi(() => _transcript.AddMessage(ChatRole.Error, ChatMarkup.Render($"Failed to create meme: {ex.Message}")));
        return $"Failed to create meme: {ex.Message}";
      }
    }

    private async Task ShowAiResultAsync(string url)
    {
      try
      {
        await _renderCts?.CancelAsync()!;
        string path = await _ctx.Cache.GetAsync(url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Say($"Preview failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task CopyAiAsync()
    {
      if (_aiUrl is null) { Say("No AI meme yet, ask the assistant first (F2)", NotificationSeverity.Warning); return; }
      try
      {
        string path = await _ctx.Cache.GetAsync(_aiUrl);
        if (await ImageClipboard.CopyFileAsync(path))
        {
          Say("Image copied to clipboard", NotificationSeverity.Success);
          return;
        }
        ClipboardHelper.SetText(_aiUrl);
        Say("Image copy unsupported here, URL copied instead", NotificationSeverity.Warning);
      }
      catch (Exception ex)
      {
        Say($"Copy failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task SaveAiAsync()
    {
      if (_aiUrl is null) { Say("No AI meme yet, ask the assistant first (F2)", NotificationSeverity.Warning); return; }
      Say("Opening file explorer...");
      string? target = await NativeFileDialog.SaveFileAsync(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Path.GetFileName(new Uri(_aiUrl).AbsolutePath));
      if (target is null) { Say("Save cancelled"); return; }
      try
      {
        await FileDownloader.DownloadAsync(_aiUrl, target);
        Say($"Saved {target}", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Save failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task UploadAiAsync()
    {
      if (_aiUrl is null) { Say("No AI meme yet, ask the assistant first (F2)", NotificationSeverity.Warning); return; }
      if (_ctx.User is null) { Say("Login (Esc, then F8) to upload to the feed", NotificationSeverity.Warning); return; }
      try
      {
        Say("Uploading to feed...");
        string path = await _ctx.Cache.GetAsync(_aiUrl);
        byte[] data = await File.ReadAllBytesAsync(path);
        (long id, bool duplicate) = await _ctx.Feed.UploadAsync(_ctx.User, data, MemeFeedClient.ContentTypeFor(path));
        Say(duplicate ? $"Already in the feed as #{id}" : $"Uploaded to feed as #{id}", duplicate ? NotificationSeverity.Warning : NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Upload failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private void RunOnUi(Action action)
    {
      if (Ws.IsOnUIThread) action();
      else Ws.EnqueueOnUIThread(action);
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
        ? $" [{b.ColorHex}]{marker} ■[/] {Chrome.MutedText($"{Label(i, _boxes.Count)}  template position")}"
        : $" [{b.ColorHex}]{marker} ■[/] {Chrome.MutedText($"{Label(i, _boxes.Count)}  x{b.X} y{b.Y}  {b.Width}x{b.Height}")}";
    }

    private void RefreshLabels()
    {
      for (int i = 0; i < _boxLabels.Count; i++)
        _boxLabels[i].SetContent([BoxLabel(i)]);
    }

    private void SetActive(int index)
    {
      if (index == _active) return;
      _active = index;
      RefreshLabels();
      RenderOverlay();
    }

    private void CycleFocus(int direction)
    {
      int stops = _inputs.Count + 2;
      int current = _chatInput.HasFocus ? _inputs.Count + 1 : _customMode.HasFocus ? _inputs.Count : _active;
      int next = (current + direction + stops) % stops;

      if (next == _inputs.Count) Window.FocusControl(_customMode);
      else if (next == _inputs.Count + 1) Window.FocusControl(_chatInput);
      else FocusBox(next);
    }

    private void FocusBox(int index)
    {
      index = (index + _inputs.Count) % _inputs.Count;
      Window.FocusControl(_inputs[index]);
      SetActive(index);
    }

    private void Advance(int index)
    {
      if (index + 1 < _inputs.Count) FocusBox(index + 1);
      else Submit();
    }

    private void RenderOverlay()
    {
      if (_imagePath is null || _aiUrl is not null) return;

      _renderCts?.Cancel();
      CancellationTokenSource cts = new();
      _renderCts = cts;
      List<(int x, int y, int w, int h, string color, bool active)> snapshot = CustomPositions
        ? _boxes.Select((b, i) => (b.X, b.Y, b.Width, b.Height, b.ColorHex, i == _active)).ToList()
        : [];

      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(60, cts.Token);
          byte[] png = ComposeOverlay(_imagePath, _meme.Width, _meme.Height, snapshot);
          if (cts.Token.IsCancellationRequested) return;
          await Ws.InvokeAsync(() => _preview.SetImage(png));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
          Say($"Preview failed: {ex.Message}", NotificationSeverity.Danger);
        }
      }, cts.Token);
    }

    private static byte[] ComposeOverlay(string path, int templateW, int templateH, List<(int x, int y, int w, int h, string color, bool active)> boxes)
    {
      using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
      const int maxSide = 900;
      if (img.Width > maxSide || img.Height > maxSide)
      {
        double s = Math.Min((double)maxSide / img.Width, (double)maxSide / img.Height);
        img.Mutate(x => x.Resize((int)(img.Width * s), (int)(img.Height * s)));
      }

      double sx = (double)img.Width / Math.Max(1, templateW);
      double sy = (double)img.Height / Math.Max(1, templateH);

      foreach ((int bx, int by, int bw, int bh, string colorHex, bool active) in boxes)
      {
        Rgba32 color = HexToRgba(colorHex);
        int x0 = (int)(bx * sx), y0 = (int)(by * sy);
        int x1 = (int)((bx + bw) * sx) - 1, y1 = (int)((by + bh) * sy) - 1;
        int thickness = active ? 4 : 2;

        FillRect(img, x0, y0, x1, y1, new Rgba32(color.R, color.G, color.B, active ? (byte)70 : (byte)40));
        for (int t = 0; t < thickness; t++)
          Outline(img, x0 + t, y0 + t, x1 - t, y1 - t, color);
      }

      using MemoryStream ms = new();
      img.SaveAsPng(ms);
      return ms.ToArray();
    }

    private static Rgba32 HexToRgba(string hex)
    {
      hex = hex.TrimStart('#');
      byte r = Convert.ToByte(hex[..2], 16);
      byte g = Convert.ToByte(hex.Substring(2, 2), 16);
      byte b = Convert.ToByte(hex.Substring(4, 2), 16);
      return new Rgba32(r, g, b, 255);
    }

    private static void FillRect(Image<Rgba32> img, int x0, int y0, int x1, int y1, Rgba32 tint)
    {
      x0 = Math.Clamp(x0, 0, img.Width - 1); x1 = Math.Clamp(x1, 0, img.Width - 1);
      y0 = Math.Clamp(y0, 0, img.Height - 1); y1 = Math.Clamp(y1, 0, img.Height - 1);
      float a = tint.A / 255f;
      img.ProcessPixelRows(accessor =>
      {
        for (int y = y0; y <= y1; y++)
        {
          Span<Rgba32> row = accessor.GetRowSpan(y);
          for (int x = x0; x <= x1; x++)
          {
            Rgba32 p = row[x];
            row[x] = new Rgba32(
              (byte)(p.R + (tint.R - p.R) * a),
              (byte)(p.G + (tint.G - p.G) * a),
              (byte)(p.B + (tint.B - p.B) * a),
              255);
          }
        }
      });
    }

    private static void Outline(Image<Rgba32> img, int x0, int y0, int x1, int y1, Rgba32 color)
    {
      if (x1 < x0 || y1 < y0) return;
      for (int x = Math.Max(0, x0); x <= Math.Min(img.Width - 1, x1); x++)
      {
        if (y0 >= 0 && y0 < img.Height) img[x, y0] = color;
        if (y1 >= 0 && y1 < img.Height) img[x, y1] = color;
      }
      for (int y = Math.Max(0, y0); y <= Math.Min(img.Height - 1, y1); y++)
      {
        if (x0 >= 0 && x0 < img.Width) img[x0, y] = color;
        if (x1 >= 0 && x1 < img.Width) img[x1, y] = color;
      }
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
