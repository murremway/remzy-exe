using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using NAudio.Wave;
using Whisper.net;

namespace Yinka;

/// <summary>
/// Caption engine backed by Whisper.NET (a managed wrapper around whisper.cpp).
/// Captures 16kHz/16-bit/mono audio with NAudio, buffers it, and runs inference
/// on rolling windows in a background loop. Fully offline once the model is on
/// disk; the model is lazily downloaded from Hugging Face into
/// %LOCALAPPDATA%\Yinka\models\ on first use.
///
/// Design notes:
///   - Inference cadence is ~3s; each pass runs Whisper on the last ~8s of audio
///     to give the decoder enough context for word boundaries.
///   - Results from each pass become the Hypothesis (live partial) text, and a
///     simple RMS-based VAD finalizes a phrase after ~1.2s of silence.
///   - Whisper produces no diarization; we emit one continuous transcript.
/// </summary>
public sealed class WhisperEngine : ICaptionEngine
{
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;
    private readonly Func<int> _getMicIndex;

    private WaveInEvent? _wave;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly object _bufferLock = new();
    private readonly List<byte> _pcm = new(16000 * 2 * 30);
    private string _committedSinceLastFlush = "";
    private DateTime _lastVoiceUtc = DateTime.UtcNow;

    private const int SampleRate = 16000;
    private const int BytesPerSample = 2;
    private const int Channels = 1;
    private const int WindowSeconds = 8;
    private const int CadenceMs = 3000;
    private const double SilenceRmsThreshold = 0.012;
    private const int SilenceMsToFinalize = 1200;

    public CaptionEngineKind Kind => CaptionEngineKind.Whisper;
    public CaptionEngineState State { get; private set; } = CaptionEngineState.Idle;
    public bool IsRunning => _wave is not null;

    public event Action<string>? PhraseCommitted;
    public event Action<string>? Hypothesis;
    public event Action<string>? SessionMessage;
    public event Action<CaptionEngineState>? StateChanged;
    public event Action<EngineFailure>? Failed;

    /// <summary>Model download progress, 0..1. UI may bind a progress bar to this.</summary>
    public event Action<double>? ModelDownloadProgress;

    public WhisperEngine(Dispatcher dispatcher, AppSettings settings, Func<int> getMicIndex)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _getMicIndex = getMicIndex;
    }

    public EngineAvailability Probe()
    {
        try
        {
            // The native whisper.cpp runtime is shipped via Whisper.net.Runtime; it lazily
            // resolves at first WhisperFactory.FromPath. Fully testing it here would download
            // the model, so we just confirm the managed binding loaded. The native side
            // requires the Visual C++ 2022 runtime and (for the default AVX runtime)
            // Windows 11 / Server 2022+ — the NoAvx runtime is included as a fallback.
            _ = typeof(WhisperFactory).FullName;
            return new EngineAvailability(
                true,
                "Whisper model will download on first use (~470 MB). Requires Windows 11/Server 2022+ and the Visual C++ 2022 runtime.");
        }
        catch (Exception ex)
        {
            return new EngineAvailability(false, "Whisper.NET failed to load: " + ex.Message);
        }
    }

    public string ModelPath => Path.Combine(SpeechDiagnostics.LogDirectory, "models", _settings.WhisperModel);

    public bool ModelDownloaded => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 1_000_000;

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        SetState(CaptionEngineState.Starting);
        Status("Starting Whisper engine…");
        SpeechDiagnostics.Info("Whisper", "StartAsync");

        try
        {
            await EnsureModelDownloaded(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Fail(new EngineFailure(
                "Could not download the Whisper model.",
                ex.Message + "\n\nDownload " + _settings.WhisperModel + " manually from https://huggingface.co/ggerganov/whisper.cpp/tree/main and place it at " + ModelPath,
                Inner: ex));
            return;
        }

        try
        {
            _factory = WhisperFactory.FromPath(ModelPath);
            _processor = _factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();
        }
        catch (Exception ex)
        {
            Fail(new EngineFailure(
                "Whisper failed to initialize.",
                ex.Message,
                Inner: ex));
            return;
        }

        try
        {
            var idx = MicEnumerator.ResolveDeviceIndex(_getMicIndex(), _settings.MicDeviceName);
            _wave = new WaveInEvent
            {
                DeviceNumber = idx < 0 ? 0 : idx,
                WaveFormat = new WaveFormat(SampleRate, 16, Channels),
                BufferMilliseconds = 100,
            };
            _wave.DataAvailable += OnData;
            _wave.RecordingStopped += OnStopped;
            _wave.StartRecording();
        }
        catch (Exception ex)
        {
            DisposeWhisper();
            Fail(new EngineFailure(
                "Whisper couldn't open the microphone.",
                ex.Message,
                SettingsLinks.SoundDevices,
                "Open Sound devices",
                SettingsLinks.MicrophonePrivacy,
                "Open Microphone privacy",
                ex));
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => InferenceLoop(_loopCts.Token));

        SetState(CaptionEngineState.Listening);
        Status("Listening (Whisper, offline).");
    }

    public async Task StopAsync()
    {
        if (_wave is null && _processor is null && _loopTask is null)
        {
            SetState(CaptionEngineState.Idle);
            return;
        }

        SetState(CaptionEngineState.Stopping);
        SpeechDiagnostics.Info("Whisper", "StopAsync");

        try { _loopCts?.Cancel(); } catch { /* ignore */ }

        // Stop the mic capture first so the inference loop sees no new audio,
        // then wait for the loop to actually exit before we dispose the processor.
        try
        {
            if (_wave is not null)
            {
                _wave.DataAvailable -= OnData;
                _wave.RecordingStopped -= OnStopped;
                _wave.StopRecording();
                _wave.Dispose();
            }
        }
        catch (Exception ex) { SpeechDiagnostics.Warn("Whisper", "Stop wave: " + ex.Message); }
        _wave = null;

        // Wait up to ~3s for the inference loop to honor cancellation before we
        // dispose the processor it may still be using. After that we give up and
        // rely on the broad try/catch inside the loop to swallow access errors.
        var loop = _loopTask;
        _loopTask = null;
        if (loop is not null)
        {
            try { await Task.WhenAny(loop, Task.Delay(3000)).ConfigureAwait(true); }
            catch { /* ignore */ }
        }

        _loopCts?.Dispose();
        _loopCts = null;

        DisposeWhisper();

        lock (_bufferLock) _pcm.Clear();
        _committedSinceLastFlush = "";

        SetState(CaptionEngineState.Idle);
        Status("Speech caption stopped.");
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
    }

    private void DisposeWhisper()
    {
        try { _processor?.Dispose(); } catch { /* ignore */ }
        _processor = null;
        try { _factory?.Dispose(); } catch { /* ignore */ }
        _factory = null;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;

        var voice = ComputeRms(e.Buffer, e.BytesRecorded) > SilenceRmsThreshold;
        if (voice)
            _lastVoiceUtc = DateTime.UtcNow;

        lock (_bufferLock)
        {
            _pcm.AddRange(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));

            // Cap buffer at 2 * window to bound memory.
            var maxBytes = SampleRate * BytesPerSample * Channels * (WindowSeconds * 2);
            if (_pcm.Count > maxBytes)
                _pcm.RemoveRange(0, _pcm.Count - maxBytes);
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            SpeechDiagnostics.Warn("Whisper", "Wave stopped: " + e.Exception.Message);
    }

    private static double ComputeRms(byte[] buffer, int count)
    {
        long sumSq = 0;
        var samples = count / 2;
        for (var i = 0; i < count; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSq += s * s;
        }
        var rms = Math.Sqrt(sumSq / (double)samples);
        return rms / 32768.0;
    }

    private async Task InferenceLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CadenceMs, ct).ConfigureAwait(false);

                byte[] snapshot;
                lock (_bufferLock)
                {
                    var bytesPerSecond = SampleRate * BytesPerSample * Channels;
                    var maxBytes = bytesPerSecond * WindowSeconds;
                    var startIdx = Math.Max(0, _pcm.Count - maxBytes);
                    var len = _pcm.Count - startIdx;
                    if (len < bytesPerSecond)
                        continue;
                    snapshot = new byte[len];
                    _pcm.CopyTo(startIdx, snapshot, 0, len);
                }

                var text = await TranscribeAsync(snapshot, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                _committedSinceLastFlush = text.Trim();
                _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke(_committedSinceLastFlush)));

                if ((DateTime.UtcNow - _lastVoiceUtc).TotalMilliseconds > SilenceMsToFinalize && !string.IsNullOrEmpty(_committedSinceLastFlush))
                {
                    var final = _committedSinceLastFlush;
                    _committedSinceLastFlush = "";
                    SpeechDiagnostics.Info("Whisper", "Final: " + final);
                    _dispatcher.BeginInvoke(new Action(() => PhraseCommitted?.Invoke(final)));
                    _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke("")));
                    lock (_bufferLock) _pcm.Clear();
                }
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                SpeechDiagnostics.Error("Whisper", ex);
            }
        }
    }

    private async Task<string?> TranscribeAsync(byte[] pcm, CancellationToken ct)
    {
        var processor = _processor;
        if (processor is null)
            return null;

        using var ms = new MemoryStream();
        WriteWav(ms, pcm);
        ms.Position = 0;

        var sb = new System.Text.StringBuilder(256);
        await foreach (var seg in processor.ProcessAsync(ms, ct))
        {
            if (!string.IsNullOrWhiteSpace(seg.Text))
                sb.Append(seg.Text);
        }
        return sb.ToString();
    }

    /// <summary>Wrap raw 16kHz/16-bit/mono PCM in a minimal RIFF/WAV container.</summary>
    private static void WriteWav(Stream output, byte[] pcm)
    {
        using var bw = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        var byteRate = SampleRate * Channels * BytesPerSample;
        var blockAlign = (short)(Channels * BytesPerSample);
        var dataSize = pcm.Length;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);                 // PCM
        bw.Write((short)Channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)(BytesPerSample * 8));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(pcm);
    }

    private async Task EnsureModelDownloaded(CancellationToken ct)
    {
        if (ModelDownloaded)
            return;

        var dir = Path.GetDirectoryName(ModelPath)!;
        Directory.CreateDirectory(dir);

        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{_settings.WhisperModel}";
        Status($"Downloading {_settings.WhisperModel}…");
        SpeechDiagnostics.Info("Whisper", "Downloading model from " + url);

        using var http = new HttpClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(true);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        var tmp = ModelPath + ".part";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(true))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(true)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(true);
                received += read;
                if (total > 0)
                {
                    var p = received / (double)total;
                    _dispatcher.BeginInvoke(new Action(() => ModelDownloadProgress?.Invoke(p)));
                    if ((received & 0xFFFFF) == 0)
                        Status($"Downloading {_settings.WhisperModel}: {p:P0}");
                }
            }
        }
        File.Move(tmp, ModelPath, overwrite: true);
        SpeechDiagnostics.Info("Whisper", $"Model downloaded to {ModelPath} ({new FileInfo(ModelPath).Length:N0} bytes).");
        Status("Whisper model ready.");
    }

    private void SetState(CaptionEngineState s)
    {
        State = s;
        _dispatcher.BeginInvoke(new Action(() => StateChanged?.Invoke(s)));
    }

    private void Status(string msg) =>
        _dispatcher.BeginInvoke(new Action(() => SessionMessage?.Invoke(msg)));

    private void Fail(EngineFailure f)
    {
        if (f.Inner is not null)
            SpeechDiagnostics.Error("Whisper", f.Inner);
        else
            SpeechDiagnostics.Error("Whisper", f.Title + " — " + f.Message);
        SetState(CaptionEngineState.Error);
        Status(f.Title);
        _dispatcher.BeginInvoke(new Action(() => Failed?.Invoke(f)));
    }
}
