using NAudio.Wave;

namespace image_flip_bosch.CLI.Sixel.Audio
{
  public sealed class AudioOutput : IDisposable
  {
    public const int SampleRate = 48000;
    public const int Channels = 2;
    private const int BytesPerSecond = SampleRate * Channels * 2;

    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOutEvent _device;
    private bool _started;
    private bool _muted;

    public static bool Supported => OperatingSystem.IsWindows();

    public AudioOutput()
    {
      _buffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels))
      {
        BufferLength = BytesPerSecond * 10,
        DiscardOnBufferOverflow = false,
        ReadFully = true,
      };
      _device = new WaveOutEvent { DesiredLatency = 120 };
      _device.Init(_buffer);
    }

    public bool Muted
    {
      get => _muted;
      set
      {
        _muted = value;
        _device.Volume = value ? 0f : 1f;
      }
    }

    public TimeSpan Buffered => TimeSpan.FromSeconds((double)_buffer.BufferedBytes / BytesPerSecond);

    public void Start()
    {
      if (_started) return;
      _started = true;
      _device.Play();
    }

    public async Task PushAsync(byte[] data, int count, CancellationToken ct)
    {
      while (_buffer.BufferLength - _buffer.BufferedBytes < count)
        await Task.Delay(20, ct);
      _buffer.AddSamples(data, 0, count);
    }

    public void Dispose()
    {
      _device.Stop();
      _device.Dispose();
    }
  }
}
