using System.Globalization;
using System.Windows.Threading;
using SysSpeech = System.Speech.Recognition;

namespace Yinka;

/// <summary>
/// Caption engine backed by the legacy desktop SAPI 5.4 engine via System.Speech. The
/// engine ships with every desktop Windows install, doesn't require Online Speech to
/// be enabled, doesn't depend on Microsoft cloud services, and works without the
/// modern WinRT speech contracts. This makes it the most reliable fallback when the
/// WinRT engine refuses to start due to language pack or privacy gating.
///
/// Trade-off: lower accuracy than WinRT when a real language pack is installed.
/// </summary>
public sealed class SapiSpeechEngine : ICaptionEngine
{
    private readonly Dispatcher _dispatcher;
    private SysSpeech.SpeechRecognitionEngine? _engine;

    public CaptionEngineKind Kind => CaptionEngineKind.Sapi;
    public CaptionEngineState State { get; private set; } = CaptionEngineState.Idle;
    public bool IsRunning => _engine is not null;

    public event Action<string>? PhraseCommitted;
    public event Action<string>? Hypothesis;
    public event Action<string>? SessionMessage;
    public event Action<CaptionEngineState>? StateChanged;
    public event Action<EngineFailure>? Failed;

    public SapiSpeechEngine(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public EngineAvailability Probe()
    {
        try
        {
            var installed = SysSpeech.SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0)
                return new EngineAvailability(false, "No SAPI recognizers installed on this Windows build.");
            return new EngineAvailability(true, null);
        }
        catch (Exception ex)
        {
            return new EngineAvailability(false, ex.Message);
        }
    }

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        SetState(CaptionEngineState.Starting);
        Status("Starting SAPI speech…");
        SpeechDiagnostics.Info("SAPI", "StartAsync");

        try
        {
            var ri = PickRecognizer();
            if (ri is null)
            {
                Fail(new EngineFailure(
                    "No SAPI recognizer is installed.",
                    "SAPI 5.4 normally ships with Windows. Try installing the optional 'Windows Speech Recognition Macros' or any English speech recognizer.",
                    SettingsLinks.SpeechPage,
                    "Open Speech settings"));
                return Task.CompletedTask;
            }

            var engine = new SysSpeech.SpeechRecognitionEngine(ri);
            try
            {
                engine.SetInputToDefaultAudioDevice();
            }
            catch (Exception ex)
            {
                engine.Dispose();
                Fail(new EngineFailure(
                    "SAPI couldn't open the default microphone.",
                    "Pick a default capture device in Sound settings, then try again.",
                    SettingsLinks.SoundDevices,
                    "Open Sound devices",
                    SettingsLinks.MicrophonePrivacy,
                    "Open Microphone privacy",
                    ex));
                return Task.CompletedTask;
            }

            engine.LoadGrammar(new SysSpeech.DictationGrammar());
            engine.SpeechHypothesized += OnHypothesized;
            engine.SpeechRecognized += OnRecognized;
            engine.RecognizeCompleted += OnRecognizeCompleted;
            engine.AudioStateChanged += OnAudioStateChanged;
            engine.RecognizeAsync(SysSpeech.RecognizeMode.Multiple);

            _engine = engine;
            SetState(CaptionEngineState.Listening);
            Status("Listening (SAPI).");
            SpeechDiagnostics.Info("SAPI", $"Started with recognizer '{ri.Name}' ({ri.Culture}).");
        }
        catch (Exception ex)
        {
            Fail(new EngineFailure(
                "Could not start SAPI recognition.",
                ex.Message,
                SettingsLinks.SpeechPage,
                "Open Speech settings",
                Inner: ex));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        var engine = _engine;
        _engine = null;
        if (engine is null)
        {
            SetState(CaptionEngineState.Idle);
            return Task.CompletedTask;
        }

        SetState(CaptionEngineState.Stopping);
        SpeechDiagnostics.Info("SAPI", "StopAsync");
        try
        {
            engine.SpeechHypothesized -= OnHypothesized;
            engine.SpeechRecognized -= OnRecognized;
            engine.RecognizeCompleted -= OnRecognizeCompleted;
            engine.AudioStateChanged -= OnAudioStateChanged;
            engine.RecognizeAsyncCancel();
            engine.Dispose();
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("SAPI", "Stop encountered: " + ex.Message);
        }

        SetState(CaptionEngineState.Idle);
        Status("Speech caption stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
    }

    /// <summary>Prefer an English recognizer matching the current culture; fall back to the first installed one.</summary>
    private static SysSpeech.RecognizerInfo? PickRecognizer()
    {
        var installed = SysSpeech.SpeechRecognitionEngine.InstalledRecognizers();
        if (installed.Count == 0)
            return null;

        var cur = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var match = installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == cur);
        if (match is not null)
            return match;

        var en = installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
        return en ?? installed[0];
    }

    private void OnHypothesized(object? sender, SysSpeech.SpeechHypothesizedEventArgs e)
    {
        var t = e.Result?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke(text)));
    }

    private void OnRecognized(object? sender, SysSpeech.SpeechRecognizedEventArgs e)
    {
        var t = e.Result?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        SpeechDiagnostics.Info("SAPI", $"Recognized (conf={e.Result.Confidence:F2}): {text}");
        _dispatcher.BeginInvoke(new Action(() => PhraseCommitted?.Invoke(text)));
    }

    private void OnRecognizeCompleted(object? sender, SysSpeech.RecognizeCompletedEventArgs e)
    {
        if (e.Error is not null)
            SpeechDiagnostics.Error("SAPI", e.Error);
        if (e.Cancelled)
            SpeechDiagnostics.Info("SAPI", "RecognizeCompleted (cancelled).");
    }

    private void OnAudioStateChanged(object? sender, SysSpeech.AudioStateChangedEventArgs e) =>
        SpeechDiagnostics.Info("SAPI", "AudioState=" + e.AudioState);

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
            SpeechDiagnostics.Error("SAPI", f.Inner);
        else
            SpeechDiagnostics.Error("SAPI", f.Title + " — " + f.Message);
        SetState(CaptionEngineState.Error);
        Status(f.Title);
        _dispatcher.BeginInvoke(new Action(() => Failed?.Invoke(f)));
    }
}
