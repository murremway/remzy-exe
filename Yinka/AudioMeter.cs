using System.Windows.Threading;
using NAudio.Wave;

namespace Yinka;

/// <summary>
/// Lightweight microphone level meter. Opens a WaveInEvent at 16kHz/16-bit mono,
/// computes RMS amplitude per ~50ms buffer, and raises <see cref="LevelChanged"/>
/// on the supplied dispatcher with a 0..1 normalized peak value.
///
/// This intentionally lives next to the engines but is independent of any of them
/// so the user can see live mic activity even when no engine is running. Useful
/// to quickly verify "is the mic actually picking up audio?".
/// </summary>
public sealed class AudioMeter : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private WaveInEvent? _wave;
    private int _deviceIndex = -1;

    public event Action<float>? LevelChanged;

    public AudioMeter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start(int deviceIndex)
    {
        Stop();
        _deviceIndex = deviceIndex;
        try
        {
            _wave = new WaveInEvent
            {
                DeviceNumber = deviceIndex < 0 ? 0 : deviceIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _wave.DataAvailable += OnData;
            _wave.RecordingStopped += OnStopped;
            _wave.StartRecording();
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("AudioMeter", $"Couldn't open mic #{deviceIndex}: {ex.Message}");
            _wave?.Dispose();
            _wave = null;
        }
    }

    public void Stop()
    {
        if (_wave is null)
            return;
        try
        {
            _wave.DataAvailable -= OnData;
            _wave.RecordingStopped -= OnStopped;
            _wave.StopRecording();
            _wave.Dispose();
        }
        catch { /* ignore */ }
        _wave = null;
        _dispatcher.BeginInvoke(new Action(() => LevelChanged?.Invoke(0f)));
    }

    public void Dispose() => Stop();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded < 2)
            return;

        // 16-bit signed PCM little-endian. Compute RMS, scale to 0..1.
        long sumSq = 0;
        int samples = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sumSq += s * s;
        }
        double rms = Math.Sqrt(sumSq / (double)samples);
        float level = (float)Math.Min(1.0, rms / 16384.0); // 16384 ~= half of full-scale int16
        _dispatcher.BeginInvoke(new Action(() => LevelChanged?.Invoke(level)));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            SpeechDiagnostics.Warn("AudioMeter", "Stopped with: " + e.Exception.Message);
    }

    public int DeviceIndex => _deviceIndex;
}
