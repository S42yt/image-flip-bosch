namespace image_flip_bosch.Backend
{
  public abstract class Program
  {
    public static Task<int> Main(string[] args) => BackendHost.RunAsync(args);
  }
}
