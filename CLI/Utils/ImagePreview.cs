using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Color = Terminal.Gui.Drawing.Color;


//mal gucken ob das so bleibt oder ob die andere version geiler ist mashallah :P

namespace image_flip_bosch.CLI.Utils
{
  public static class ImageDecoder
  {
    public static Color[,] FromBytes(byte[] data)
    {
      using Image<Rgba32> img = Image.Load<Rgba32>(data);
      return FromImage(img);
    }

    public static Color[,] FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    public static Color[,] FromImage(Image<Rgba32> img)
    {
      Color[,] pixels = new Color[img.Width, img.Height];
      img.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < accessor.Height; y++)
        {
          Span<Rgba32> row = accessor.GetRowSpan(y);
          for (int x = 0; x < row.Length; x++)
          {
            Rgba32 p = row[x];
            pixels[x, y] = new Color(p.R, p.G, p.B);
          }
        }
      });
      return pixels;
    }
  }

  public class ImagePreview : ImageView
  {
    private int _loadVersion;

    public string? CurrentPath { get; private set; }

    public event EventHandler<string>? LoadFailed;

    public bool TryLoad(string path)
    {
      try
      {
        Image = ImageDecoder.FromFile(path);
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

    public Task LoadAsync(IApplication app, string path, CancellationToken ct = default)
    {
      int version = Interlocked.Increment(ref _loadVersion);
      return Task.Run(() => LoadCore(app, path, version, ct), ct);
    }

    public async Task LoadWhenReadyAsync(IApplication app, string path, TimeSpan? timeout = null, CancellationToken ct = default)
    {
      int version = Interlocked.Increment(ref _loadVersion);
      DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));

      while (!IsReadable(path))
      {
        if (ct.IsCancellationRequested || version != _loadVersion) return;
        if (DateTime.UtcNow > deadline)
        {
          app.Invoke(() => LoadFailed?.Invoke(this, $"Timed out waiting for {path}"));
          return;
        }
        await Task.Delay(200, ct);
      }

      await Task.Run(() => LoadCore(app, path, version, ct), ct);
    }

    public void Clear()
    {
      Interlocked.Increment(ref _loadVersion);
      Image = null;
      CurrentPath = null;
    }

    private void LoadCore(IApplication app, string path, int version, CancellationToken ct)
    {
      Color[,]? pixels = null;
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

      app.Invoke(() =>
      {
        if (version != _loadVersion) return;
        if (pixels is null)
        {
          Clear();
          LoadFailed?.Invoke(this, error ?? "Unknown error");
          return;
        }
        Image = pixels;
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
