namespace image_flip_bosch.ImgFlip
{
  using System;
  using System.IO;
  using System.Runtime.Versioning;
  using System.Security.Cryptography;
  using System.Text;
  using System.Text.Json;

  public sealed record ImgflipCredentials(string Username, string Password);

  public sealed class ImgflipCredentialStore
  {
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("image_flip_bosch.imgflip.v1");

    private sealed record Envelope(string Username, string Password, bool Protected);

    public ImgflipCredentialStore(string? path = null)
    {
      Path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "image_flip_bosch",
        "imgflip.cred");
    }

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public bool IsProtectedStorage => OperatingSystem.IsWindows();

    public void Save(ImgflipCredentials credentials)
    {
      if (string.IsNullOrWhiteSpace(credentials.Username))
        throw new ArgumentException("Username is required.", nameof(credentials));
      if (string.IsNullOrEmpty(credentials.Password))
        throw new ArgumentException("Password is required.", nameof(credentials));

      Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

      string password = credentials.Password;
      bool isProtected = false;

      if (OperatingSystem.IsWindows())
      {
        password = Protect(credentials.Password);
        isProtected = true;
      }

      string json = JsonSerializer.Serialize(new Envelope(credentials.Username, password, isProtected));
      string tmp = Path + ".tmp";
      File.WriteAllText(tmp, json, Encoding.UTF8);
      RestrictPermissions(tmp);
      File.Move(tmp, Path, overwrite: true);
      RestrictPermissions(Path);
    }

    public ImgflipCredentials? Load()
    {
      if (!File.Exists(Path)) return null;

      Envelope? env;
      try
      {
        env = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(Path, Encoding.UTF8));
      }
      catch (JsonException)
      {
        return null;
      }

      if (env is null) return null;

      string password = env.Password;
      if (env.Protected)
      {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
          password = Unprotect(env.Password);
        }
        catch (CryptographicException)
        {
          return null;
        }
      }

      return new ImgflipCredentials(env.Username, password);
    }

    public bool Remove()
    {
      if (!File.Exists(Path)) return false;

      try
      {
        byte[] noise = new byte[new FileInfo(Path).Length];
        RandomNumberGenerator.Fill(noise);
        File.WriteAllBytes(Path, noise);
      }
      catch (IOException) { }

      File.Delete(Path);
      return true;
    }

    [SupportedOSPlatform("windows")]
    private static string Protect(string plain)
    {
      byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
      return Convert.ToBase64String(data);
    }

    [SupportedOSPlatform("windows")]
    private static string Unprotect(string base64)
    {
      byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(base64), Entropy, DataProtectionScope.CurrentUser);
      return Encoding.UTF8.GetString(data);
    }

    private static void RestrictPermissions(string path)
    {
      if (OperatingSystem.IsWindows()) return;
      try
      {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
