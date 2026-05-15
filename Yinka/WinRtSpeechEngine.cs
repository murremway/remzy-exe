using System.Windows.Threading;
using Windows.Media.SpeechRecognition;

namespace Yinka;

/// <summary>
/// Caption engine backed by Windows.Media.SpeechRecognition (UWP/WinRT). This is the
/// engine the original prototype used. Improvements over the prototype:
///
///   - Branches on <see cref="SpeechRecognitionResultStatus"/> from CompileConstraintsAsync
///     and surfaces friendly errors with `ms-settings:` deep-link buttons.
///   - Increases the silence/babble timeouts (defaults are ~5s, far too short for sermons).
///   - Auto-restarts the session when it ends with Timeout / NotStarted reasons.
///   - Threads everything through SpeechDiagnostics.
/// </summary>
public sealed class WinRtSpeechEngine : ICaptionEngine
{
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;
    private SpeechRecognizer? _recognizer;
    private CancellationTokenSource? _restartCts;
    private int _consecutiveRestarts;
    private const int MaxConsecutiveRestarts = 6;

    public CaptionEngineKind Kind => CaptionEngineKind.WinRt;
    public CaptionEngineState State { get; private set; } = CaptionEngineState.Idle;
    public bool IsRunning => _recognizer is not null;

    public event Action<string>? PhraseCommitted;
    public event Action<string>? Hypothesis;
    public event Action<string>? SessionMessage;
    public event Action<CaptionEngineState>? StateChanged;
    public event Action<EngineFailure>? Failed;

    public WinRtSpeechEngine(Dispatcher dispatcher, AppSettings settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
    }

    public EngineAvailability Probe()
    {
        // The WinRT recognizer type itself is part of the OS contracts targeted by
        // net8.0-windows10.0.19041.0; if it loads we trust it can at least try.
        try
        {
            _ = typeof(SpeechRecognizer).FullName;
            return new EngineAvailability(true, null);
        }
        catch (Exception ex)
        {
            return new EngineAvailability(false, "Windows speech contracts unavailable: " + ex.Message);
        }
    }

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        SetState(CaptionEngineState.Starting);
        Status("Starting Windows speech…");
        SpeechDiagnostics.Info("WinRT", "StartAsync");

        SpeechRecognizer recognizer;
        try
        {
            recognizer = new SpeechRecognizer();
        }
        catch (Exception ex)
        {
            Fail(new EngineFailure(
                "Could not create the Windows speech recognizer.",
                ex.Message,
                SettingsLinks.SpeechPage,
                "Open Windows Speech settings",
                Inner: ex));
            return;
        }

        try
        {
            recognizer.Constraints.Add(
                new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        }
        catch (Exception ex)
        {
            recognizer.Dispose();
            Fail(new EngineFailure(
                "Could not add dictation constraint.",
                ex.Message,
                SettingsLinks.SpeechPage,
                "Open Windows Speech settings",
                Inner: ex));
            return;
        }

        try
        {
            ApplyTimeouts(recognizer);
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("WinRT", "Could not set timeouts (continuing): " + ex.Message);
        }

        SpeechRecognitionCompilationResult compile;
        try
        {
            compile = await recognizer.CompileConstraintsAsync();
        }
        catch (Exception ex)
        {
            recognizer.Dispose();
            Fail(new EngineFailure(
                "Speech constraints failed to compile.",
                ex.Message,
                SettingsLinks.SpeechPage,
                "Open Speech settings",
                Inner: ex));
            return;
        }

        if (compile.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            EmitCompileFailure(compile.Status);
            return;
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
        recognizer.ContinuousRecognitionSession.Completed += OnCompleted;
        // HypothesisGenerated lives on SpeechRecognizer itself, NOT on the continuous session.
        recognizer.HypothesisGenerated += OnHypothesisGenerated;

        try
        {
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            UnhookAndDispose(recognizer);
            Fail(new EngineFailure(
                "Could not start the speech session.",
                ex.Message + "\n\nTip: ensure no other app is holding the microphone exclusively.",
                SettingsLinks.MicrophonePrivacy,
                "Open Microphone settings",
                SettingsLinks.SoundDevices,
                "Open Sound devices",
                ex));
            return;
        }

        _recognizer = recognizer;
        SetState(CaptionEngineState.Listening);
        Status("Listening (Windows speech).");
        SpeechDiagnostics.Info("WinRT", "Session started.");
    }

    public async Task StopAsync()
    {
        var r = _recognizer;
        _recognizer = null;
        _restartCts?.Cancel();
        _restartCts = null;

        if (r is null)
        {
            SetState(CaptionEngineState.Idle);
            return;
        }

        SetState(CaptionEngineState.Stopping);
        SpeechDiagnostics.Info("WinRT", "StopAsync");
        try
        {
            UnhookAndDispose(r, alsoStop: true);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("WinRT", "Stop encountered: " + ex.Message);
        }

        SetState(CaptionEngineState.Idle);
        Status("Speech caption stopped.");
    }

    public void Dispose()
    {
        try
        {
            StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }

    private void ApplyTimeouts(SpeechRecognizer recognizer)
    {
        var t = recognizer.Timeouts;
        t.InitialSilenceTimeout = TimeSpan.FromSeconds(Math.Max(1, _settings.WinRtInitialSilenceSeconds));
        t.EndSilenceTimeout = TimeSpan.FromSeconds(Math.Max(1, _settings.WinRtEndSilenceSeconds));
        t.BabbleTimeout = TimeSpan.FromSeconds(Math.Max(1, _settings.WinRtBabbleSeconds));
    }

    private void EmitCompileFailure(SpeechRecognitionResultStatus status)
    {
        SpeechDiagnostics.Error("WinRT", $"CompileConstraints status = {status}");

        switch (status)
        {
            case SpeechRecognitionResultStatus.TopicLanguageNotSupported:
                Fail(new EngineFailure(
                    "Dictation isn't available for your Windows speech language.",
                    "Install an English (or your preferred) speech pack: Settings → Time & language → Speech → Add a voice. After installing, restart Yinka and click Start again.",
                    SettingsLinks.SpeechPage,
                    "Open Windows Speech settings"));
                return;

            case SpeechRecognitionResultStatus.UserCanceled:
                Fail(new EngineFailure(
                    "Microphone access was denied.",
                    "Allow Yinka to use the microphone, then try again.",
                    SettingsLinks.MicrophonePrivacy,
                    "Open Microphone privacy"));
                return;

            case SpeechRecognitionResultStatus.NetworkFailure:
                Fail(new EngineFailure(
                    "Couldn't reach the Microsoft online speech service.",
                    "Check your internet connection. If you have an English speech pack installed, you can also switch the engine to SAPI for fully offline recognition.",
                    SettingsLinks.OnlineSpeechPrivacy,
                    "Open Online Speech privacy"));
                return;

            case SpeechRecognitionResultStatus.MicrophoneUnavailable:
                Fail(new EngineFailure(
                    "No microphone was available.",
                    "Plug in a microphone (or set a default capture device) and try again.",
                    SettingsLinks.SoundDevices,
                    "Open Sound devices",
                    SettingsLinks.MicrophonePrivacy,
                    "Open Microphone privacy"));
                return;

            case SpeechRecognitionResultStatus.AudioQualityFailure:
                Fail(new EngineFailure(
                    "Microphone audio quality is too poor for dictation.",
                    "Try a different microphone, move closer, or reduce background noise. You can also switch to the SAPI engine which is more tolerant of low-quality audio.",
                    SettingsLinks.SoundDevices,
                    "Open Sound devices"));
                return;

            // Unknown / GrammarLanguageMismatch / GrammarCompilationFailure / etc. don't have
            // dedicated enum values for "online speech is off" or "privacy statement declined" —
            // those typically present as Unknown. Surface a generic message that points at the
            // most likely fix (online speech privacy) plus a secondary link to Speech settings.
            default:
                Fail(new EngineFailure(
                    "Speech constraints failed to compile.",
                    $"Status: {status}\n\nThe most common causes are 'Online speech recognition' being off in Privacy settings, or no English speech pack installed. If neither applies, switch to the SAPI engine for an offline fallback.",
                    SettingsLinks.OnlineSpeechPrivacy,
                    "Open Online Speech privacy",
                    SettingsLinks.SpeechPage,
                    "Open Speech settings"));
                return;
        }
    }

    private void OnHypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        var t = args.Hypothesis?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        _consecutiveRestarts = 0;
        _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke(text)));
    }

    private void OnResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var t = args.Result?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        _consecutiveRestarts = 0;
        SpeechDiagnostics.Info("WinRT", "Result: " + text);
        _dispatcher.BeginInvoke(new Action(() => PhraseCommitted?.Invoke(text)));
    }

    private void OnCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        SpeechDiagnostics.Info("WinRT", "Completed: " + args.Status);

        if (args.Status == SpeechRecognitionResultStatus.Success && _settings.AutoRestartWinRt && _recognizer is not null)
        {
            ScheduleRestart("session ended (timeout / silence)");
            return;
        }

        if (args.Status != SpeechRecognitionResultStatus.Success)
        {
            _recognizer = null;
            SetState(CaptionEngineState.Idle);
            Status("Speech session ended: " + args.Status);
            return;
        }

        _recognizer = null;
        SetState(CaptionEngineState.Idle);
        Status("Speech session ended.");
    }

    private void ScheduleRestart(string reason)
    {
        if (_consecutiveRestarts >= MaxConsecutiveRestarts)
        {
            SpeechDiagnostics.Warn("WinRT", $"Auto-restart bail after {_consecutiveRestarts} attempts.");
            _recognizer = null;
            SetState(CaptionEngineState.Idle);
            Status("Stopped after several auto-restarts. Click Start to retry.");
            return;
        }

        _consecutiveRestarts++;
        SetState(CaptionEngineState.Reconnecting);
        Status($"Reconnecting Windows speech ({_consecutiveRestarts})…");
        SpeechDiagnostics.Info("WinRT", $"Auto-restart #{_consecutiveRestarts} ({reason})");

        _restartCts?.Cancel();
        _restartCts = new CancellationTokenSource();
        var ct = _restartCts.Token;
        var delay = TimeSpan.FromMilliseconds(Math.Min(2500, 250 * Math.Pow(2, _consecutiveRestarts - 1)));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    return;
                await _dispatcher.InvokeAsync(async () =>
                {
                    var r = _recognizer;
                    _recognizer = null;
                    if (r is not null)
                        UnhookAndDispose(r, alsoStop: false);
                    await StartAsync().ConfigureAwait(true);
                });
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                SpeechDiagnostics.Error("WinRT", ex);
            }
        }, ct);
    }

    private void UnhookAndDispose(SpeechRecognizer r, bool alsoStop = false)
    {
        try
        {
            r.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            r.ContinuousRecognitionSession.Completed -= OnCompleted;
            r.HypothesisGenerated -= OnHypothesisGenerated;
        }
        catch { /* ignore */ }

        if (alsoStop)
        {
            try
            {
                r.ContinuousRecognitionSession.StopAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { /* ignore */ }
        }

        try { r.Dispose(); } catch { /* ignore */ }
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
            SpeechDiagnostics.Error("WinRT", f.Inner);
        else
            SpeechDiagnostics.Error("WinRT", f.Title + " — " + f.Message);
        SetState(CaptionEngineState.Error);
        Status(f.Title);
        _dispatcher.BeginInvoke(new Action(() => Failed?.Invoke(f)));
    }
}
