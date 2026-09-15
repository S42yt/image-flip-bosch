using OpenAI.Chat;

namespace image_flip_bosch.CLI.Utils.Ai
{
  internal enum AiState { Idle, Installing, Starting, Ready, Failed }

  internal sealed class AiAssistant : IAsyncDisposable
  {
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

    public async Task<string> CompleteAsync(List<ChatMessage> history, ChatResponseFormat format, CancellationToken ct)
    {
      if (_client is null) throw new InvalidOperationException("AI assistant is not ready");
      ChatCompletionOptions options = new() { ResponseFormat = format, Temperature = 0.8f };
      ChatCompletion completion = await _client.CompleteChatAsync(history, options, ct);
      string text = string.Concat(completion.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
      history.Add(new AssistantChatMessage(text));
      return text;
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
