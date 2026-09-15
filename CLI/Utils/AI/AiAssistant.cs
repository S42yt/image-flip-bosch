using OpenAI.Chat;
using System.Text;

namespace image_flip_bosch.CLI.Utils.Ai
{
  internal enum AiState { Idle, Installing, Starting, Ready, Failed }

  internal sealed class AiAssistant : IAsyncDisposable
  {
    private const int MaxToolRounds = 4;

    private readonly LlamaChatService _llama = new(AppContext.BaseDirectory);
    private readonly SemaphoreSlim _setupGate = new(1, 1);
    private ChatClient? _client;

    public AiState State { get; private set; } = AiState.Idle;

    public string StatusText { get; private set; } = "AI assistant not started";

    public string? Error { get; private set; }

    public event Action<AiState, string>? StatusChanged;

    public event Action<DownloadProgress>? Progress;

    public bool IsReady => State == AiState.Ready && _client is not null;

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
      if (IsReady) return;
      await _setupGate.WaitAsync(ct);
      try
      {
        if (IsReady) return;
        Error = null;
        if (!_llama.IsServerBinaryInstalled)
        {
          Set(AiState.Installing, "Downloading llama-server...");
          await _llama.InstallServerBinaryAsync(new Progress<DownloadProgress>(p => Progress?.Invoke(p)), ct);
        }
        if (!LlamaChatService.IsModelInstalled)
        {
          Set(AiState.Installing, "Downloading model, this takes a while...");
          await _llama.InstallModelAsync(new Progress<DownloadProgress>(p => Progress?.Invoke(p)), ct);
        }
        Set(AiState.Starting, "Starting llama-server...");
        _llama.StartServer(_ => { });
        Set(AiState.Starting, "Loading model...");
        await _llama.WaitUntilReadyAsync(ct);
        _client = _llama.CreateChatClient();
        Set(AiState.Ready, "AI ready");
      }
      catch (OperationCanceledException)
      {
        Set(AiState.Idle, "AI start cancelled");
        throw;
      }
      catch (Exception ex)
      {
        Error = ex.Message;
        _llama.StopServer();
        Set(AiState.Failed, $"AI unavailable: {ex.Message}");
        throw;
      }
      finally
      {
        _setupGate.Release();
      }
    }

    public async Task ChatAsync(
      List<ChatMessage> history,
      ChatTool tool,
      Func<string, string, Task<string>> executeTool,
      Action<string> onReplyText,
      Action<string> onToolStarted,
      CancellationToken ct)
    {
      if (_client is null) throw new InvalidOperationException("AI assistant is not ready");

      for (int round = 0; round < MaxToolRounds; round++)
      {
        StringBuilder reply = new();
        Dictionary<int, string> ids = [];
        Dictionary<int, string> names = [];
        Dictionary<int, StringBuilder> args = [];
        bool announced = false;

        ChatCompletionOptions options = new();
        options.Tools.Add(tool);

        await foreach (StreamingChatCompletionUpdate update in _client.CompleteChatStreamingAsync(history, options, cancellationToken: ct))
        {
          foreach (StreamingChatToolCallUpdate call in update.ToolCallUpdates)
          {
            if (call.ToolCallId is not null) ids[call.Index] = call.ToolCallId;
            if (call.FunctionName is not null) names[call.Index] = call.FunctionName;
            if (call.FunctionArgumentsUpdate is not null)
            {
              if (!args.TryGetValue(call.Index, out StringBuilder? sb)) args[call.Index] = sb = new StringBuilder();
              sb.Append(call.FunctionArgumentsUpdate.ToString());
            }
            if (announced) continue;
            announced = true;
            onToolStarted(reply.ToString());
          }

          foreach (ChatMessageContentPart part in update.ContentUpdate)
          {
            if (string.IsNullOrEmpty(part.Text)) continue;
            reply.Append(part.Text);
            onReplyText(reply.ToString());
          }
        }

        if (names.Count == 0)
        {
          history.Add(new AssistantChatMessage(reply.Length > 0 ? reply.ToString() : "(no response)"));
          return;
        }

        List<ChatToolCall> calls = names.Keys.OrderBy(i => i)
          .Select(i => ChatToolCall.CreateFunctionToolCall(
            ids.TryGetValue(i, out string? id) ? id : $"call_{i}",
            names[i],
            BinaryData.FromString(args.TryGetValue(i, out StringBuilder? a) ? a.ToString() : "{}")))
          .ToList();

        AssistantChatMessage assistant = new(calls);
        if (reply.Length > 0) assistant.Content.Add(ChatMessageContentPart.CreateTextPart(reply.ToString()));
        history.Add(assistant);

        foreach (ChatToolCall call in calls)
        {
          string result = await executeTool(call.FunctionName, call.FunctionArguments.ToString());
          history.Add(new ToolChatMessage(call.Id, result));
        }
      }
    }

    private void Set(AiState state, string text)
    {
      State = state;
      StatusText = text;
      StatusChanged?.Invoke(state, text);
    }

    public ValueTask DisposeAsync()
    {
      _llama.StopServer();
      return ValueTask.CompletedTask;
    }
  }
}
