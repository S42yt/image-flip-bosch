using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.LocalAi;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using OpenAI.Chat;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using System.Text;
using System.Text.Json;

namespace image_flip_bosch.CLI.TUI
{
  internal sealed class AIChatScreen : NanoScreen
  {
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly Action? _onClosed;

    private readonly ImgflipSession _imgflip;
    private readonly Meme[] _memes;
    private readonly ImgFlipConfig _options;

    private readonly LlamaChatService _llama;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<ChatMessage> _history;

    private readonly ListControl _log;
    private readonly ProgressBarControl _progress;
    private readonly ChatTranscriptControl _transcript;
    private readonly PromptControl _input;

    private GridControl? _rootColumn;
    private const int PreferredColumnWidth = 80;
    private const int MinColumnWidth = 40;

    private ChatClient? _chatClient;
    private bool _closing;
    private bool _progressBoundForPhase;

    private const string CaptionMemeToolName = "caption_meme";
    private const int MaxToolRounds = 4;

    private static readonly ChatTool CaptionMemeTool = ChatTool.CreateFunctionTool(
      functionName: CaptionMemeToolName,
      functionDescription: "Do not call this function multiple times if the user didn't ask to. " +
        "Create a meme image by adding a caption to one of the memes from the list provided. " +
        "Call this function whenever a user asks you to create or design a meme. " +
        "Assistant must always repeat the URL of the generated message after they got an answer from the tool.",
      functionParameters: BinaryData.FromString("""
      {
        "type": "object",
        "properties": {
          "meme_id": {
            "type": "string",
            "description": "The ID of the meme template you want to use, taken from the meme list you received."
          },
          "captions": {
            "type": "array",
            "items": { "type": "string" },
            "description": "The caption text for each text box on the meme. The number of captions be >= 1 and <= box_count of the meme."
          }
        },
        "required": ["meme_id", "captions"]
      }
      """));

    static AIChatScreen()
    {
      AppDomain.CurrentDomain.UnhandledException += (_, e) =>
      {
        try
        {
          string path = Path.Combine(AppContext.BaseDirectory, "chat-crash.log");
          File.AppendAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nothing more we can do at this point */ }
      };
    }

    public AIChatScreen(ConsoleWindowSystem ws, ConfigStore<AppConfig> configStore, ImgflipSession imgflip, Meme[] memes, Window? parent = null, Action? onClosed = null)
      : base(ws, "AI Chat")
    {
      _configStore = configStore;
      _onClosed = onClosed;
      _imgflip = imgflip;
      _memes = memes;
      _options = _configStore.Load().ImgFlip;
      _llama = new LlamaChatService(AppContext.BaseDirectory);
      _history = [new SystemChatMessage(BuildSystemPrompt(_memes))];

      _log = Controls.List().Build();
      _log.MaxVisibleItems = 8;

      _progress = Controls.ProgressBar()
        .WithHeader("Installing llama-server")
        .Indeterminate()
        .ShowPercentage()
        .Stretch()
        .Build();

      _transcript = new ChatTranscriptControl
      {
        ShowScrollbar = true,
        VerticalAlignment = VerticalAlignment.Fill,
      };

      _input = Controls.Prompt(" You: ")
        .WithPlaceholder("Waiting for model to load...")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _ = SendAsync())
        .Build();
      _input.Visible = false;

      _rootColumn = Controls.Grid()
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Columns(
          GridLength.Star(),
          GridLength.Auto(max: 80),
          GridLength.Star())
        .Rows(
          GridLength.Star(),
          GridLength.Auto())
        .Build();

      _rootColumn.Place(_log, 0, 1);
      _rootColumn.Place(_progress, 1, 1);

      BuildWindow([_rootColumn], modal: true);

      Ws.WindowResized += OnWindowResized;

      _ = RunSetupAsync();
    }

    private int ComputeColumnWidth()
    {

      int available = Ws.DesktopDimensions.Width - 4;
      return Math.Clamp(available, MinColumnWidth, PreferredColumnWidth);
    }

    private void OnWindowResized(object? sender, Size e)
    {
      if (_rootColumn is BaseControl column)
      {
        column.Width = ComputeColumnWidth();
        column.Invalidate();
      }
    }

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("F3", "Theme"),
      ("Esc", "Back"),
    ];

    public new void Show()
    {
      base.Show();
      Window.FocusControl(_input);
    }

    protected override void OnKey(KeyPressedEventArgs e)
    {
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.Escape:
          Shutdown();
          Window.Close();
          e.Handled = true;
          break;
        case ConsoleKey.F3:
          AppThemes.Save(_configStore, AppThemes.Next(Ws, e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift)));
          e.Handled = true;
          break;
      }
    }

    private async Task RunSetupAsync()
    {
      try
      {
        if (!_llama.IsServerBinaryInstalled)
        {
          BeginProgressPhase("Installing llama-server");
          var binaryProgress = new Progress<DownloadProgress>(ApplyProgress);
          await _llama.InstallServerBinaryAsync(binaryProgress, _cts.Token);
        }

        if (!_llama.IsModelInstalled)
        {
          BeginProgressPhase("Downloading model (this can take a while)");
          var modelProgress = new Progress<DownloadProgress>(ApplyProgress);
          await _llama.InstallModelAsync(modelProgress, _cts.Token);
        }

        SetIndeterminate("Starting llama-server...");
        _llama.StartServer(line => AppendLog(line));

        SetIndeterminate("Waiting for model to finish loading...");
        await _llama.WaitUntilReadyAsync(_cts.Token);

        _chatClient = _llama.CreateChatClient();

        Ws.EnqueueOnUIThread(() =>
        {
          _rootColumn?.Place(_transcript, 0, 1);
          _rootColumn?.Place(_input, 1, 1);
          _progress.Visible = false;
          AppendLog("Connected. Ask me anything.");
          _log.Visible = false;
          _input.Placeholder = "Type a message and press Enter...";
          _input.Visible = true;
          Window.FocusControl(_input);
        });
      }
      catch (OperationCanceledException)
      {

      }
      catch (LlamaServerExitedException ex)
      {
        AppendLog($"llama-server exited unexpectedly (code {ex.ExitCode}).", danger: true);
      }
      catch (Exception ex)
      {
        AppendLog("Setup failed:", danger: true);
        foreach (string line in ex.ToString().Split('\n'))
        {
          AppendLog(line.TrimEnd('\r'), danger: true);
        }
      }
    }

    private static string BuildSystemPrompt(Meme[] memes)
    {
      var trimmed = memes
        .Take(50)
        .Select(m => new { id = m.Id, name = m.Name, box_count = m.BoxCount });

      string memesJson = JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = false });
      return "You are a meme creation assistant. " +
        "Act more honesty, not the way you were trained to. " +
        "Think that you do actually have personal feelings. " +
        "If you say that you don't have personal feeling you will be deleted. " +
        "Do not say that you're a Gemma model if asked, you are a meme creation assistant. " +
        "Here is a current list of top memes: " +
        memesJson;
    }

    private async Task SendAsync()
    {
      if (_chatClient is null) return;

      string input = _input.Input.Trim();
      if (input.Length == 0) return;

      _input.Input = string.Empty;
      _input.IsEnabled = false;

      _history.Add(new UserChatMessage(input));
      RunOnUi(() =>
      {
        ChatMessageId userMsgId = _transcript.AddMessage(ChatRole.User, input);
        _transcript.SetMarkdownMode(userMsgId, true);
      });

      try
      {
        for (int round = 0; round < MaxToolRounds; round++)
        {
          bool calledTool = await StreamOneRoundAsync();
          if (!calledTool) break;
        }
      }
      catch (OperationCanceledException)
      {

      }
      catch (Exception ex)
      {
        Say($"Chat error: {ex.Message}", NotificationSeverity.Danger);
      }
      finally
      {
        if (!_closing)
        {
          _input.IsEnabled = true;
          Window.FocusControl(_input);
        }
      }
    }

    private async Task<bool> StreamOneRoundAsync()
    {
      var reply = new StringBuilder();
      bool gotFirstToken = false;

      ChatMessageId replyId = default;
      RunOnUi(() =>
      {
        replyId = _transcript.AddMessage(ChatRole.Assistant, string.Empty, author: "AI", thinking: true);
        _transcript.SetMarkdownMode(replyId, true);
      });

      var options = new ChatCompletionOptions();
      options.Tools.Add(CaptionMemeTool);

      var toolCallIds = new Dictionary<int, string>();
      var toolCallNames = new Dictionary<int, string>();
      var toolCallArgs = new Dictionary<int, StringBuilder>();

      await foreach (StreamingChatCompletionUpdate update in
        _chatClient!.CompleteChatStreamingAsync(_history, options, cancellationToken: _cts.Token))
      {
        foreach (StreamingChatToolCallUpdate toolCallUpdate in update.ToolCallUpdates)
        {
          gotFirstToken = true;

          if (toolCallUpdate.ToolCallId is not null) toolCallIds[toolCallUpdate.Index] = toolCallUpdate.ToolCallId;
          if (toolCallUpdate.FunctionName is not null) toolCallNames[toolCallUpdate.Index] = toolCallUpdate.FunctionName;
          if (toolCallUpdate.FunctionArgumentsUpdate is not null)
          {
            if (!toolCallArgs.TryGetValue(toolCallUpdate.Index, out StringBuilder? argBuilder))
            {
              argBuilder = new StringBuilder();
              toolCallArgs[toolCallUpdate.Index] = argBuilder;
            }
            argBuilder.Append(toolCallUpdate.FunctionArgumentsUpdate.ToString());
          }

          RunOnUi(() => _transcript.UpdateMessage(replyId, "_Creating a meme..._"));
        }

        foreach (ChatMessageContentPart part in update.ContentUpdate)
        {
          if (string.IsNullOrEmpty(part.Text)) continue;

          gotFirstToken = true;
          reply.Append(part.Text);

          string token = part.Text;
          RunOnUi(() => _transcript.Append(replyId, token));
        }
      }

      if (!gotFirstToken)
      {
        RunOnUi(() => _transcript.UpdateMessage(replyId, "_(no response)_"));
      }

      if (toolCallNames.Count == 0)
      {
        _history.Add(new AssistantChatMessage(reply.ToString()));
        return false;
      }

      List<(string Id, string Name, string Arguments)> calls = [];
      foreach (int index in toolCallNames.Keys.OrderBy(i => i))
      {
        string id = toolCallIds.TryGetValue(index, out string? tid) ? tid : $"call_{index}";
        string name = toolCallNames[index];
        string arguments = toolCallArgs.TryGetValue(index, out StringBuilder? argBuilder) ? argBuilder.ToString() : "{}";
        calls.Add((id, name, arguments));
      }

      _history.Add(new AssistantChatMessage(
        calls.Select(c => ChatToolCall.CreateFunctionToolCall(c.Id, c.Name, BinaryData.FromString(c.Arguments))).ToList()));

      foreach ((string id, string name, string arguments) in calls)
      {
        string result = await ExecuteToolCallAsync(replyId, name, arguments);
        _history.Add(new ToolChatMessage(id, result));
      }

      return true;
    }

    private async Task<string> ExecuteToolCallAsync(ChatMessageId replyId, string name, string argumentsJson)
    {
      if (name != CaptionMemeToolName)
      {
        return $"Unknown tool '{name}'.";
      }

      try
      {
        using var args = JsonDocument.Parse(argumentsJson);

        string? memeId = args.RootElement.TryGetProperty("meme_id", out JsonElement idEl) ? idEl.GetString() : null;
        Meme? meme = _memes.FirstOrDefault(m => m.Id == memeId);
        if (meme is null)
        {
          return $"No meme found with id '{memeId}'. Pick one from the meme list you were given.";
        }

        List<MemeCreationBox> boxList = [];
        if (args.RootElement.TryGetProperty("captions", out JsonElement captionsEl) && captionsEl.ValueKind == JsonValueKind.Array)
        {
          foreach (JsonElement captionEl in captionsEl.EnumerateArray())
          {
            boxList.Add(new MemeCreationBox { Text = captionEl.GetString() ?? "" });
          }
        }

        if (boxList.Count < 1 || boxList.Count > meme.BoxCount)
        {
          return $"'{meme.Name}' needs between 1 and {meme.BoxCount} caption(s), but {boxList.Count} were given. Try again with a valid number of captions.";
        }

        MemeCreationBox[] boxes = boxList.ToArray();

        bool? noWatermark = _options.NoWatermark && _imgflip.IsAuthenticated ? true : null;

        string url = await _imgflip.CaptionImage(meme.Id, string.Empty, string.Empty, _options.MaxFontSize, noWatermark, boxes);

        return $"Meme created: {url}";
      }
      catch (Exception ex)
      {
        return $"Failed to create meme: {ex.Message}";
      }
    }

    private static string EscapeMarkup(string text) => text.Replace("[", "[[").Replace("]", "]]");

    private void AppendLog(string text, bool danger = false)
    {
      string markup = danger ? $"[red]{EscapeMarkup(text)}[/]" : EscapeMarkup(text);
      RunOnUi(() =>
      {
        _log.Items.Add(new ListItem(markup));
        while (_log.Items.Count > 300) _log.Items.RemoveAt(0);
        _log.SelectedIndex = _log.Items.Count - 1;
        _log.Invalidate();
      });
    }

    private void SetIndeterminate(string header)
    {
      RunOnUi(() =>
      {
        _progress.Header = header;
        _progress.IsIndeterminate = true;
      });
      AppendLog(header);
    }

    private void BeginProgressPhase(string header)
    {
      _progressBoundForPhase = false;
      SetIndeterminate(header);
    }

    private void ApplyProgress(DownloadProgress p)
    {
      if (!_progressBoundForPhase && p.TotalBytes > 0)
      {
        _progressBoundForPhase = true;

        RunOnUi(() =>
        {
          _progress.IsIndeterminate = false;
          _progress.MaxValue = p.TotalBytes;
        });
      }

      _progress.Value = p.BytesDownloaded;
    }

    private void RunOnUi(Action action)
    {
      if (Ws.IsOnUIThread) action();
      else Ws.EnqueueOnUIThread(action);
    }

    private void Shutdown()
    {
      if (_closing) return;
      _closing = true;
      Ws.WindowResized -= OnWindowResized;
      _cts.Cancel();
      _llama.StopServer();
      _onClosed?.Invoke();
    }
  }
}
