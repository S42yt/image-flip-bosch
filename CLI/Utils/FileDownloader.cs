namespace image_flip_bosch.CLI.Utils
{
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;
  
  public static class FileDownloader
  {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task DownloadAsync(
      string url,
      string destinationPath,
      IProgress<(long received, long? total)>? progress = null,
      CancellationToken ct = default)
    {
      using HttpResponseMessage response =
        await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
      response.EnsureSuccessStatusCode();

      long? total = response.Content.Headers.ContentLength;
      string tempPath = destinationPath + ".part";

      Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

      try
      {
        await using Stream body = await response.Content.ReadAsStreamAsync(ct);
        await using FileStream file = File.Create(tempPath);

        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
          await file.WriteAsync(buffer.AsMemory(0, read), ct);
          received += read;
          progress?.Report((received, total));
        }
      }
      catch
      {
        try { File.Delete(tempPath); } catch {}
        throw;
      }

      File.Move(tempPath, destinationPath, overwrite: true);
    }
  }
}
