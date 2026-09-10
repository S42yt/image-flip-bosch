using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Imaging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


//mal gucken ob das so bleibt oder ob die andere version geiler ist mashallah :P

namespace image_flip_bosch.CLI.Utils
{
  public static class ImageDecoder
  {
    public static PixelBuffer FromBytes(byte[] data)
    {
      using MemoryStream ms = new(data);
      return PixelBuffer.FromStream(ms);
    }

    public static PixelBuffer FromFile(string path) => PixelBuffer.FromFile(path);
  }

  public class ImagePreview : ImageControl
  {
    private readonly ConsoleWindowSystem _ws;
    private int _loadVersion;

    public ImagePreview(ConsoleWindowSystem ws)
    {
      _ws = ws;
      ScaleMode = ImageScaleMode.Fit;
    }

    public string? CurrentPath { get; private set; }

    public event EventHandler<string>? LoadFailed;

    public bool TryLoad(string path)
    {
      try
      {
        Source = ImageDecoder.FromFile(path);
        CurrentPath = path;
        return true;
      }
      catch (Exception ex)
      {
        Clear();
        LoadFailed?.Invoke(this, ex.Message);
        return false;
      }
    }

    public Task LoadAsync(string path, CancellationToken ct = default)
    {
      int version = Interlocked.Increment(ref _loadVersion);
      return Task.Run(() => LoadCoreAsync(path, version, ct), ct);
    }

    public async Task LoadWhenReadyAsync(string path, TimeSpan? timeout = null, CancellationToken ct = default)
    {
      int version = Interlocked.Increment(ref _loadVersion);
      DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));

      while (!IsReadable(path))
      {
        if (ct.IsCancellationRequested || version != _loadVersion) return;
        if (DateTime.UtcNow > deadline)
        {
          await _ws.InvokeAsync(() => LoadFailed?.Invoke(this, $"Timed out waiting for {path}"));
          return;
        }
        await Task.Delay(200, ct);
      }

      await Task.Run(() => LoadCoreAsync(path, version, ct), ct);
    }

    public void Clear()
    {
      Interlocked.Increment(ref _loadVersion);
      Source = null;
      CurrentPath = null;
    }

    private async Task LoadCoreAsync(string path, int version, CancellationToken ct)
    {
      PixelBuffer? pixels = null;
      string? error = null;
      try
      {
        pixels = ImageDecoder.FromFile(path);
      }
      catch (Exception ex)
      {
        error = ex.Message;
      }

      if (ct.IsCancellationRequested || version != _loadVersion) return;

      await _ws.InvokeAsync(() =>
      {
        if (version != _loadVersion) return;
        if (pixels is null)
        {
          Clear();
          LoadFailed?.Invoke(this, error ?? "Unknown error");
          return;
        }
        Source = pixels;
        CurrentPath = path;
      });
    }

    private static bool IsReadable(string path)
    {
      if (!File.Exists(path)) return false;
      try
      {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return fs.Length > 0;
      }
      catch (IOException)
      {
        return false;
      }
    }
  }
}
