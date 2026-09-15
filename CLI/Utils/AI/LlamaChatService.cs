using System.ClientModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using OpenAI.Chat;

namespace image_flip_bosch.CLI.Utils.Ai
{

    internal sealed record DownloadProgress(string Label, long BytesDownloaded, long TotalBytes)
    {
        public double Percent => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100.0 : 0.0;
    }

    internal sealed class LlamaServerExitedException(int exitCode) : Exception($"llama-server exited with code {exitCode}.")
    {
        public int ExitCode { get; } = exitCode;
    }

    internal sealed class LlamaChatService(string appDir) : IAsyncDisposable
    {
        private const string ZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b10951/llama-b10951-bin-win-cpu-x64.zip";
        private const string HfRepo = "unsloth/gemma-4-E2B-it-GGUF";
        private const string GgufFileName = "gemma-4-E2B-it-Q4_K_M.gguf";
        private const int BufferSize = 81920;

        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        private readonly string _appDir = appDir;
        private Process? _serverProcess;
        private WindowsJobObject? _jobObject;
        private int _serverPort;

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public string ServerExePath => Path.Combine(_appDir, "llama-server.exe");
        public bool IsServerBinaryInstalled => File.Exists(ServerExePath);

        public static string ModelCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "llama.cpp");
        public static string ModelPath => Path.Combine(ModelCacheDir, GgufFileName);
        public static bool IsModelInstalled => File.Exists(ModelPath);

        public async Task InstallServerBinaryAsync(IProgress<DownloadProgress> progress, CancellationToken token)
        {
            if (IsServerBinaryInstalled) return;

            string zipPath = Path.Combine(_appDir, "llama-server.zip");

            using var request = new HttpRequestMessage(HttpMethod.Get, ZipUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LlamaServerDownloader", "1.0"));

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            using (var sourceStream = await response.Content.ReadAsStreamAsync(token))
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                await CopyWithProgressAsync(sourceStream, fileStream, totalBytes, 0, "llama-server.exe", progress, token);
            }

            ZipFile.ExtractToDirectory(zipPath, _appDir, overwriteFiles: true);
            File.Delete(zipPath);
        }

        public async Task InstallModelAsync(IProgress<DownloadProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(ModelCacheDir);
            if (IsModelInstalled) return;

            string modelUrl = $"https://huggingface.co/{HfRepo}/resolve/main/{GgufFileName}";
            string partPath = ModelPath + ".part";
            long existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, modelUrl);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LlamaDownloader/1.0)");
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            bool append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append) existingBytes = 0;

            long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;

            using (Stream sourceStream = await response.Content.ReadAsStreamAsync(token))
            using (var fileStream = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                await CopyWithProgressAsync(sourceStream, fileStream, totalBytes, existingBytes, GgufFileName, progress, token);
            }

            File.Move(partPath, ModelPath, overwrite: true);
        }

        public void StartServer(Action<string> onLogLine)
        {
            _serverPort = GetFreeTcpPort();

            var startInfo = new ProcessStartInfo
            {
                FileName = ServerExePath,
                Arguments = $"-m \"{ModelPath}\" --port {_serverPort} --host 127.0.0.1 -c 16384 --cache-type-k q4_0 --cache-type-v q4_0 -fa on --reasoning off",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _serverProcess = new Process { StartInfo = startInfo };
            _serverProcess.OutputDataReceived += (_, e) => SafeInvokeLogLine(onLogLine, e.Data);
            _serverProcess.ErrorDataReceived += (_, e) => SafeInvokeLogLine(onLogLine, e.Data);

            if (!_serverProcess.Start())
            {
                throw new InvalidOperationException("Failed to start llama-server process.");
            }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _jobObject = new WindowsJobObject();
                    _jobObject.Assign(_serverProcess);
                }
                catch
                {
                    _jobObject?.Dispose();
                    _jobObject = null;
                }
            }

            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
        }

        private static void SafeInvokeLogLine(Action<string> onLogLine, string? data)
        {
            if (data is null) return;
            try
            {
                onLogLine(data);
            }
            catch
            {

            }
        }

        public async Task WaitUntilReadyAsync(CancellationToken token)
        {
            string healthUrl = $"http://127.0.0.1:{_serverPort}/health";

            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (_serverProcess is null)
                {
                    throw new InvalidOperationException("Server was not started.");
                }
                if (_serverProcess.HasExited)
                {
                    throw new LlamaServerExitedException(_serverProcess.ExitCode);
                }

                try
                {
                    HttpResponseMessage response = await Http.GetAsync(healthUrl, token);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException)
                {

                }

                await Task.Delay(500, token);
            }
        }


        public ChatClient CreateChatClient()
        {
            var clientOptions = new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{_serverPort}/v1")
            };
            return new ChatClient("gemma-4", new ApiKeyCredential("local-key"), clientOptions);
        }

        public void StopServer()
        {
            if (_serverProcess is null) return;

            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {

            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
                _jobObject?.Dispose();
                _jobObject = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            StopServer();
            return ValueTask.CompletedTask;
        }

        private static async Task CopyWithProgressAsync(
            Stream source, Stream destination, long totalBytes, long startingBytes,
            string label, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            byte[] buffer = new byte[BufferSize];
            long downloaded = startingBytes;
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                downloaded += bytesRead;
                progress.Report(new DownloadProgress(label, downloaded, totalBytes));
            }
        }
    }
}